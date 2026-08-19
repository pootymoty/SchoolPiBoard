using Microsoft.EntityFrameworkCore;
using SchoolPiBoard.Online.Configuration;
using SchoolPiBoard.Online.Data;

namespace SchoolPiBoard.Online.Services;

public enum AccountOutcome
{
    Ok,
    BadRequest,
    EmailTaken,
    /// <summary>Регистрация есть, но почта ещё не подтверждена.</summary>
    NotConfirmed,
    InvalidCredentials,
    /// <summary>Ссылка из письма просрочена или уже использована.</summary>
    LinkExpired,
    NotFound
}

public sealed record AccountResult(AccountOutcome Outcome, User? User = null, string? Token = null, string? Message = null)
{
    public bool IsOk => Outcome == AccountOutcome.Ok;
}

/// <summary>
/// Регистрация, вход и всё, что связано с самой учётной записью.
///
/// Ключевое правило: учётная запись появляется только после подтверждения
/// почты. До этого данные лежат в отдельной таблице и через час пропадают.
/// </summary>
public sealed class AccountService
{
    public const int MinPasswordLength = 8;
    private const int MinAgeYears = 6;
    private const int MaxAgeYears = 120;

    private readonly AppDbContext _db;
    private readonly AuthTokenService _tokens;
    private readonly IEmailSender _email;
    private readonly OnlineOptions _options;
    private readonly ILogger<AccountService> _logger;

    public AccountService(
        AppDbContext db,
        AuthTokenService tokens,
        IEmailSender email,
        OnlineOptions options,
        ILogger<AccountService> logger)
    {
        _db = db;
        _tokens = tokens;
        _email = email;
        _options = options;
        _logger = logger;
    }

    public async Task<AccountResult> RegisterAsync(
        string? lastName,
        string? firstName,
        DateOnly? birthDate,
        string? email,
        string? password,
        string? passwordConfirm,
        CancellationToken cancellationToken)
    {
        var address = EmailAddress.Normalize(email);
        if (address is null)
            return Bad("Проверьте адрес почты.");

        var last = (lastName ?? string.Empty).Trim();
        var first = (firstName ?? string.Empty).Trim();

        if (last.Length is 0 or > 100 || first.Length is 0 or > 100)
            return Bad("Укажите фамилию и имя.");

        if (birthDate is null || !IsPlausibleBirthDate(birthDate.Value))
            return Bad("Проверьте дату рождения.");

        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            return Bad($"Пароль должен быть не короче {MinPasswordLength} символов.");

        if (password != passwordConfirm)
            return Bad("Пароли не совпадают.");

        if (await _db.Users.AnyAsync(x => x.Email == address, cancellationToken))
            return new AccountResult(AccountOutcome.EmailTaken, Message: "Такая почта уже зарегистрирована.");

        // Заодно подчищаем просроченные заявки — отдельная уборка не нужна.
        var now = DateTime.UtcNow;
        var stale = await _db.PendingRegistrations
            .Where(x => x.ExpiresAt < now)
            .ToListAsync(cancellationToken);

        if (stale.Count > 0)
            _db.PendingRegistrations.RemoveRange(stale);

        // Повторная регистрация на ту же почту заменяет прежнюю заявку:
        // человек мог не получить письмо или ошибиться в данных.
        var previous = await _db.PendingRegistrations
            .FirstOrDefaultAsync(x => x.Email == address, cancellationToken);

        if (previous is not null)
            _db.PendingRegistrations.Remove(previous);

        var token = SecurityTokens.Create();

        _db.PendingRegistrations.Add(new PendingRegistration
        {
            Email = address,
            PasswordHash = PasswordHasher.Hash(password),
            LastName = last,
            FirstName = first,
            BirthDate = birthDate.Value,
            TokenHash = SecurityTokens.HashOf(token),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_options.Auth.RegistrationTtlMinutes)
        });

        await _db.SaveChangesAsync(cancellationToken);

        var link = $"{_options.Site.BaseUrl}/confirm?token={Uri.EscapeDataString(token)}";
        var letter = EmailTemplates.ConfirmRegistration(link, _options.Auth.RegistrationTtlMinutes);

        if (!await _email.SendAsync(address, letter.Subject, letter.Html, letter.Text, cancellationToken))
        {
            _logger.LogError("Письмо с подтверждением регистрации не ушло.");
            return Bad("Не удалось отправить письмо. Попробуйте ещё раз или напишите в поддержку.");
        }

        return new AccountResult(AccountOutcome.Ok);
    }

    /// <summary>Подтверждение почты. Только здесь появляется настоящая учётная запись.</summary>
    public async Task<AccountResult> ConfirmRegistrationAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new AccountResult(AccountOutcome.LinkExpired, Message: "Ссылка недействительна.");

        var hash = SecurityTokens.HashOf(token);
        var now = DateTime.UtcNow;

        var pending = await _db.PendingRegistrations.FirstOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);

        if (pending is null || pending.ExpiresAt <= now)
        {
            if (pending is not null)
            {
                _db.PendingRegistrations.Remove(pending);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return new AccountResult(AccountOutcome.LinkExpired,
                Message: "Ссылка устарела. Пройдите регистрацию заново — на подтверждение даётся час.");
        }

        // За этот час кто-то мог зарегистрировать ту же почту и подтвердить её раньше.
        if (await _db.Users.AnyAsync(x => x.Email == pending.Email, cancellationToken))
        {
            _db.PendingRegistrations.Remove(pending);
            await _db.SaveChangesAsync(cancellationToken);
            return new AccountResult(AccountOutcome.EmailTaken, Message: "Такая почта уже зарегистрирована.");
        }

        var user = new User
        {
            Email = pending.Email,
            PasswordHash = pending.PasswordHash,
            LastName = pending.LastName,
            FirstName = pending.FirstName,
            BirthDate = pending.BirthDate,
            CreatedAt = now
        };

        _db.Users.Add(user);
        _db.PendingRegistrations.Remove(pending);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Подтверждена регистрация {UserId}.", user.Id);
        return new AccountResult(AccountOutcome.Ok, user);
    }

    public async Task<AccountResult> LoginAsync(string? email, string? password, CancellationToken cancellationToken)
    {
        var address = EmailAddress.Normalize(email);
        if (address is null || string.IsNullOrEmpty(password))
            return Invalid();

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == address, cancellationToken);

        // Хеш проверяем всегда: иначе по времени ответа было бы видно,
        // зарегистрирована почта или нет.
        var valid = PasswordHasher.Verify(password, user?.PasswordHash);

        if (user is null || !valid)
        {
            // Подсказываем только про неподтверждённую регистрацию — это
            // частая причина «пароль не подходит».
            if (user is null && await _db.PendingRegistrations.AnyAsync(x => x.Email == address, cancellationToken))
            {
                return new AccountResult(AccountOutcome.NotConfirmed,
                    Message: "Регистрация не завершена: подтвердите почту по ссылке из письма.");
            }

            return Invalid();
        }

        return new AccountResult(AccountOutcome.Ok, user, _tokens.Issue(user));
    }

    public Task<User?> FindAsync(Guid userId, CancellationToken cancellationToken)
        => _db.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

    public async Task<AccountResult> ChangeNameAsync(Guid userId, string? lastName, string? firstName, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
            return new AccountResult(AccountOutcome.NotFound, Message: "Учётная запись не найдена.");

        var last = (lastName ?? string.Empty).Trim();
        var first = (firstName ?? string.Empty).Trim();

        if (last.Length is 0 or > 100 || first.Length is 0 or > 100)
            return Bad("Укажите фамилию и имя.");

        user.LastName = last;
        user.FirstName = first;
        await _db.SaveChangesAsync(cancellationToken);

        return new AccountResult(AccountOutcome.Ok, user);
    }

    public async Task<AccountResult> ChangePasswordAsync(
        Guid userId, string? currentPassword, string? newPassword, string? confirmPassword, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
            return new AccountResult(AccountOutcome.NotFound, Message: "Учётная запись не найдена.");

        if (!PasswordHasher.Verify(currentPassword ?? string.Empty, user.PasswordHash))
            return new AccountResult(AccountOutcome.InvalidCredentials, Message: "Текущий пароль не подошёл.");

        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < MinPasswordLength)
            return Bad($"Новый пароль должен быть не короче {MinPasswordLength} символов.");

        if (newPassword != confirmPassword)
            return Bad("Новые пароли не совпадают.");

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await _db.SaveChangesAsync(cancellationToken);

        // Письмо не подтверждает смену, а сообщает о ней: если пароль сменил
        // не хозяин, он об этом узнает.
        var letter = EmailTemplates.PasswordChanged();
        await _email.SendAsync(user.Email, letter.Subject, letter.Html, letter.Text, cancellationToken);

        return new AccountResult(AccountOutcome.Ok, user);
    }

    /// <summary>Запрос на удаление: ссылка уходит на почту, сразу ничего не удаляется.</summary>
    public async Task<AccountResult> RequestDeletionAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
            return new AccountResult(AccountOutcome.NotFound, Message: "Учётная запись не найдена.");

        var token = SecurityTokens.Create();
        var now = DateTime.UtcNow;

        _db.EmailActions.Add(new EmailAction
        {
            UserId = user.Id,
            Kind = EmailAction.KindDeleteAccount,
            TokenHash = SecurityTokens.HashOf(token),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_options.Auth.RegistrationTtlMinutes)
        });

        await _db.SaveChangesAsync(cancellationToken);

        var link = $"{_options.Site.BaseUrl}/profile/delete?token={Uri.EscapeDataString(token)}";
        var letter = EmailTemplates.ConfirmAccountDeletion(link, _options.Auth.RegistrationTtlMinutes);

        if (!await _email.SendAsync(user.Email, letter.Subject, letter.Html, letter.Text, cancellationToken))
            return Bad("Не удалось отправить письмо. Попробуйте ещё раз.");

        return new AccountResult(AccountOutcome.Ok, user);
    }

    /// <summary>
    /// Подтверждение удаления. Доски, участие в чужих досках, подписка
    /// и платежи уходят вместе с учётной записью — это делает база
    /// каскадным удалением.
    /// </summary>
    public async Task<AccountResult> ConfirmDeletionAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new AccountResult(AccountOutcome.LinkExpired, Message: "Ссылка недействительна.");

        var hash = SecurityTokens.HashOf(token);
        var now = DateTime.UtcNow;

        var action = await _db.EmailActions.FirstOrDefaultAsync(
            x => x.TokenHash == hash && x.Kind == EmailAction.KindDeleteAccount, cancellationToken);

        if (action is null || action.UsedAt is not null || action.ExpiresAt <= now)
        {
            return new AccountResult(AccountOutcome.LinkExpired,
                Message: "Ссылка устарела. Запросите удаление ещё раз.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == action.UserId, cancellationToken);
        if (user is null)
            return new AccountResult(AccountOutcome.NotFound, Message: "Учётная запись уже удалена.");

        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
        if (subscription is not null)
        {
            // Автопродление снимаем явно: удалить пользователя мало,
            // повторное списание должно быть невозможно.
            subscription.AutoRenew = false;
            subscription.Status = Subscription.StatusCanceled;
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Удалена учётная запись {UserId}.", action.UserId);
        return new AccountResult(AccountOutcome.Ok);
    }

    private static bool IsPlausibleBirthDate(DateOnly birthDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return birthDate <= today.AddYears(-MinAgeYears) && birthDate >= today.AddYears(-MaxAgeYears);
    }

    private static AccountResult Bad(string message) => new(AccountOutcome.BadRequest, Message: message);

    private static AccountResult Invalid() =>
        new(AccountOutcome.InvalidCredentials, Message: "Почта или пароль не подошли.");
}
