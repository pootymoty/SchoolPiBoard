using Microsoft.EntityFrameworkCore;
using SchoolPiBoard.LicenseServer.Configuration;
using SchoolPiBoard.LicenseServer.Data;

namespace SchoolPiBoard.LicenseServer.Services;

public enum TrialOutcome
{
    /// <summary>Пробный период выдан впервые.</summary>
    Started,

    /// <summary>Период уже шёл и ещё не кончился — отдаём остаток.</summary>
    Resumed,

    /// <summary>Этот компьютер (или эта почта) пробный период уже использовал.</summary>
    AlreadyUsed,

    BadRequest
}

public sealed record TrialResult(TrialOutcome Outcome, DateTime StartedAt, DateTime ExpiresAt, int Days);

/// <summary>
/// Выдача пробного периода. Решение принимает сервер, а не клиент: локальные
/// метки на компьютере стираются переустановкой, запись в базе — нет.
/// </summary>
public sealed class TrialService
{
    private readonly LicenseDbContext _db;
    private readonly TrialOptions _options;
    private readonly ILogger<TrialService> _logger;

    public TrialService(LicenseDbContext db, TrialOptions options, ILogger<TrialService> logger)
    {
        _db = db;
        _options = options;
        _logger = logger;
    }

    public int Days => _options.Days;

    public async Task<TrialResult> StartAsync(string? hardwareId, string? email, CancellationToken cancellationToken)
    {
        var device = DeviceId.Normalize(hardwareId);
        var address = EmailAddress.Normalize(email);

        if (device is null || address is null)
            return new TrialResult(TrialOutcome.BadRequest, default, default, Days);

        var now = DateTime.UtcNow;

        var existing = await _db.Trials.FirstOrDefaultAsync(x => x.HardwareId == device, cancellationToken);
        if (existing is not null)
        {
            // Приложение переустановили посреди пробного периода — отдаём остаток,
            // но не начинаем отсчёт заново.
            return new TrialResult(
                existing.IsActive(now) ? TrialOutcome.Resumed : TrialOutcome.AlreadyUsed,
                existing.StartedAt,
                existing.ExpiresAt,
                Days);
        }

        if (_options.OneTrialPerEmail)
        {
            // Отсекает переустановку Windows и смену диска: отпечаток стал другим,
            // а почта осталась прежней.
            var byEmail = await _db.Trials.FirstOrDefaultAsync(x => x.Email == address, cancellationToken);
            if (byEmail is not null)
                return new TrialResult(TrialOutcome.AlreadyUsed, byEmail.StartedAt, byEmail.ExpiresAt, Days);
        }

        var trial = new TrialActivation
        {
            HardwareId = device,
            Email = address,
            StartedAt = now,
            ExpiresAt = now.AddDays(Days)
        };

        _db.Trials.Add(trial);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Два запуска одновременно: уникальный индекс оставил одну запись.
            _db.Entry(trial).State = EntityState.Detached;

            var created = await _db.Trials.FirstOrDefaultAsync(x => x.HardwareId == device, cancellationToken);
            if (created is null)
                throw;

            return new TrialResult(
                created.IsActive(now) ? TrialOutcome.Resumed : TrialOutcome.AlreadyUsed,
                created.StartedAt,
                created.ExpiresAt,
                Days);
        }

        _logger.LogInformation("Выдан пробный период до {ExpiresAt:u}.", trial.ExpiresAt);
        return new TrialResult(TrialOutcome.Started, trial.StartedAt, trial.ExpiresAt, Days);
    }
}
