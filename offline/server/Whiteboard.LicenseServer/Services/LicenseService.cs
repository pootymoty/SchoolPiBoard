using Microsoft.EntityFrameworkCore;
using Whiteboard.LicenseServer.Configuration;
using Whiteboard.LicenseServer.Data;

namespace Whiteboard.LicenseServer.Services;

public enum ActivationOutcome
{
    /// <summary>Слот занят этим устройством — всё в порядке.</summary>
    Activated,

    /// <summary>Ключ или идентификатор устройства пришли в неправильном виде.</summary>
    BadRequest,

    /// <summary>Ключа нет в базе либо он отозван.</summary>
    InvalidKey,

    /// <summary>Все слоты заняты другими устройствами.</summary>
    DeviceLimitReached
}

public sealed record ActivationResult(
    ActivationOutcome Outcome,
    License? License,
    LicenseActivation? Activation,
    int DevicesUsed,
    int DeviceLimit);

public sealed record ValidationResult(bool Valid, string Reason, int DevicesUsed, int DeviceLimit);

public enum DeactivationOutcome
{
    Released,
    NotActivated,
    InvalidKey,
    BadRequest
}

/// <summary>Вся работа с лицензиями: активация, проверка, освобождение слота, выпуск ключа.</summary>
public sealed class LicenseService
{
    private readonly LicenseDbContext _db;
    private readonly LicenseOptions _options;
    private readonly ILogger<LicenseService> _logger;

    public LicenseService(LicenseDbContext db, LicenseOptions options, ILogger<LicenseService> logger)
    {
        _db = db;
        _options = options;
        _logger = logger;
    }

    public int DeviceLimit => _options.DeviceLimit;

    public async Task<ActivationResult> ActivateAsync(string? key, string? hardwareId, CancellationToken cancellationToken)
    {
        var normalizedKey = LicenseKey.Normalize(key);
        var device = DeviceId.Normalize(hardwareId);

        if (!LicenseKey.IsWellFormed(normalizedKey) || device is null)
            return new ActivationResult(ActivationOutcome.BadRequest, null, null, 0, DeviceLimit);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Блокируем строку лицензии до конца транзакции. Без этого два запроса
        // с разных компьютеров, пришедшие одновременно, могли бы одновременно
        // увидеть «занят один слот» и занять третий.
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM licenses WHERE key = {normalizedKey} FOR UPDATE",
            cancellationToken);

        var license = await _db.Licenses
            .Include(x => x.Activations)
            .FirstOrDefaultAsync(x => x.Key == normalizedKey, cancellationToken);

        if (license is null || license.Revoked)
            return new ActivationResult(ActivationOutcome.InvalidKey, license, null, 0, DeviceLimit);

        var now = DateTime.UtcNow;
        var activation = license.Activations.FirstOrDefault(x => x.HardwareId == device);

        if (activation is null)
        {
            if (license.Activations.Count >= DeviceLimit)
            {
                return new ActivationResult(
                    ActivationOutcome.DeviceLimitReached, license, null, license.Activations.Count, DeviceLimit);
            }

            activation = new LicenseActivation
            {
                LicenseId = license.Id,
                HardwareId = device,
                ActivatedAt = now,
                LastValidatedAt = now
            };
            license.Activations.Add(activation);
        }
        else
        {
            // Повторная активация того же устройства — это же и есть проверка
            // лицензии: обновляем дату, слот не тратим.
            activation.LastValidatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ActivationResult(
            ActivationOutcome.Activated, license, activation, license.Activations.Count, DeviceLimit);
    }

    public async Task<ValidationResult> ValidateAsync(string? key, string? hardwareId, CancellationToken cancellationToken)
    {
        var normalizedKey = LicenseKey.Normalize(key);
        var device = DeviceId.Normalize(hardwareId);

        if (!LicenseKey.IsWellFormed(normalizedKey) || device is null)
            return new ValidationResult(false, "bad_request", 0, DeviceLimit);

        var license = await _db.Licenses
            .Include(x => x.Activations)
            .FirstOrDefaultAsync(x => x.Key == normalizedKey, cancellationToken);

        if (license is null)
            return new ValidationResult(false, "unknown_key", 0, DeviceLimit);

        if (license.Revoked)
            return new ValidationResult(false, "revoked", license.Activations.Count, DeviceLimit);

        var activation = license.Activations.FirstOrDefault(x => x.HardwareId == device);
        if (activation is null)
        {
            // Слот этого компьютера освободили с другого устройства.
            return new ValidationResult(false, "device_not_activated", license.Activations.Count, DeviceLimit);
        }

        activation.LastValidatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return new ValidationResult(true, "ok", license.Activations.Count, DeviceLimit);
    }

    public async Task<DeactivationOutcome> DeactivateAsync(string? key, string? hardwareId, CancellationToken cancellationToken)
    {
        var normalizedKey = LicenseKey.Normalize(key);
        var device = DeviceId.Normalize(hardwareId);

        if (!LicenseKey.IsWellFormed(normalizedKey) || device is null)
            return DeactivationOutcome.BadRequest;

        var license = await _db.Licenses
            .Include(x => x.Activations)
            .FirstOrDefaultAsync(x => x.Key == normalizedKey, cancellationToken);

        if (license is null)
            return DeactivationOutcome.InvalidKey;

        var activation = license.Activations.FirstOrDefault(x => x.HardwareId == device);
        if (activation is null)
            return DeactivationOutcome.NotActivated;

        _db.Activations.Remove(activation);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Освобождён слот устройства для лицензии {LicenseId}.", license.Id);
        return DeactivationOutcome.Released;
    }

    /// <summary>
    /// Выпускает ключ по оплате. Повторный вебхук с тем же платежом
    /// возвращает уже выпущенную лицензию, а не создаёт вторую.
    /// </summary>
    public async Task<License> IssueForPaymentAsync(string email, string paymentHash, CancellationToken cancellationToken)
    {
        var existing = await _db.Licenses
            .FirstOrDefaultAsync(x => x.PaymentIdHash == paymentHash, cancellationToken);

        if (existing is not null)
            return existing;

        var license = new License
        {
            Key = await GenerateUniqueKeyAsync(cancellationToken),
            Email = email,
            CreatedAt = DateTime.UtcNow,
            PaymentIdHash = paymentHash
        };

        _db.Licenses.Add(license);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Два вебхука об одном платеже пришли одновременно: уникальный
            // индекс не дал создать вторую лицензию — берём созданную.
            _db.Entry(license).State = EntityState.Detached;

            var created = await _db.Licenses
                .FirstOrDefaultAsync(x => x.PaymentIdHash == paymentHash, cancellationToken);

            if (created is null)
                throw;

            return created;
        }

        _logger.LogInformation("Выпущена лицензия {LicenseId}.", license.Id);
        return license;
    }

    public async Task MarkEmailSentAsync(License license, CancellationToken cancellationToken)
    {
        license.EmailSentAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> GenerateUniqueKeyAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = LicenseKey.Create();
            var taken = await _db.Licenses.AnyAsync(x => x.Key == candidate, cancellationToken);
            if (!taken)
                return candidate;
        }

        // 31^16 вариантов: пять совпадений подряд означают не совпадение,
        // а поломку генератора случайных чисел.
        throw new InvalidOperationException("Не удалось подобрать свободный ключ.");
    }
}
