using Microsoft.EntityFrameworkCore;
using Whiteboard.LicenseServer.Configuration;
using Whiteboard.LicenseServer.Data;

namespace Whiteboard.LicenseServer.Services;

public enum AuthOutcome
{
    Ok,

    /// <summary>Почта уже занята.</summary>
    EmailTaken,

    /// <summary>Почта или пароль не подошли. Что именно — пользователю не сообщаем.</summary>
    InvalidCredentials,

    /// <summary>Почта или пароль не прошли простейшую проверку.</summary>
    BadRequest
}

public sealed record AuthResult(AuthOutcome Outcome, User? User, string? Token, string? Message = null);

/// <summary>Регистрация и вход в веб-версию.</summary>
public sealed class AuthService
{
    /// <summary>Меньше восьми символов не принимаем — это единственное требование к паролю.</summary>
    public const int MinPasswordLength = 8;

    private readonly LicenseDbContext _db;
    private readonly AuthTokenService _tokens;
    private readonly AuthOptions _options;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        LicenseDbContext db,
        AuthTokenService tokens,
        AuthOptions options,
        ILogger<AuthService> logger)
    {
        _db = db;
        _tokens = tokens;
        _options = options;
        _logger = logger;
    }

    public async Task<AuthResult> RegisterAsync(string? email, string? password, string? displayName, CancellationToken cancellationToken)
    {
        var address = EmailAddress.Normalize(email);
        if (address is null)
            return new AuthResult(AuthOutcome.BadRequest, null, null, "Проверьте адрес почты.");

        if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
        {
            return new AuthResult(AuthOutcome.BadRequest, null, null,
                $"Пароль должен быть не короче {MinPasswordLength} символов.");
        }

        if (await _db.Users.AnyAsync(x => x.Email == address, cancellationToken))
            return new AuthResult(AuthOutcome.EmailTaken, null, null, "Такая почта уже зарегистрирована.");

        var user = new User
        {
            Email = address,
            PasswordHash = PasswordHasher.Hash(password),
            DisplayName = (displayName ?? string.Empty).Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);

        // Пробная подписка на 7 дней заводится сразу: человек должен увидеть
        // продукт до того, как его попросят заплатить.
        _db.Subscriptions.Add(new Subscription
        {
            UserId = user.Id,
            Provider = "trial",
            Plan = Subscription.PlanMonthly,
            Status = Subscription.StatusTrialing,
            TrialEndsAt = DateTime.UtcNow.AddDays(_options.SubscriptionTrialDays),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Две регистрации с одной почтой одновременно: уникальный индекс
            // оставил одну, и это не наша.
            return new AuthResult(AuthOutcome.EmailTaken, null, null, "Такая почта уже зарегистрирована.");
        }

        _logger.LogInformation("Зарегистрирован пользователь {UserId}.", user.Id);
        return new AuthResult(AuthOutcome.Ok, user, _tokens.Issue(user));
    }

    public async Task<AuthResult> LoginAsync(string? email, string? password, CancellationToken cancellationToken)
    {
        var address = EmailAddress.Normalize(email);
        if (address is null || string.IsNullOrEmpty(password))
            return new AuthResult(AuthOutcome.InvalidCredentials, null, null, "Почта или пароль не подошли.");

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == address, cancellationToken);

        // Пароль проверяем даже для несуществующей почты: иначе по времени
        // ответа было бы видно, зарегистрирован адрес или нет.
        var valid = PasswordHasher.Verify(password, user?.PasswordHash);

        if (user is null || !valid)
            return new AuthResult(AuthOutcome.InvalidCredentials, null, null, "Почта или пароль не подошли.");

        return new AuthResult(AuthOutcome.Ok, user, _tokens.Issue(user));
    }

    public Task<User?> FindAsync(Guid userId, CancellationToken cancellationToken)
        => _db.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

    public Task<Subscription?> GetSubscriptionAsync(Guid userId, CancellationToken cancellationToken)
        => _db.Subscriptions
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
