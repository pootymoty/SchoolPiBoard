using System.Threading.Tasks;
using System.Windows;

namespace SchoolPiBoard.Services;

/// <summary>Что делать на старте приложения.</summary>
public enum LicenseGateResult
{
    /// <summary>Есть действующий ключ или незакончившийся пробный период.</summary>
    Allowed,

    /// <summary>Оснований работать нет — нужен экран активации (требуется интернет).</summary>
    NeedsActivation,

    /// <summary>Пробный период закончился — дальше только по ключу.</summary>
    TrialExpired
}

/// <summary>
/// Вся логика лицензии в одном месте: состояние, активация, пробный период,
/// офлайн-период и фоновая проверка. Логику самой доски не трогает.
/// </summary>
public static class LicenseManager
{
    /// <summary>Длительность пробного периода (для текстов; срок назначает сервер).</summary>
    public const int TrialDays = LicenseOptions.TrialDays;

    private static string _serverUrl = LicenseOptions.DefaultServerUrl;

    public static LicenseState? State { get; private set; }

    public static string ServerUrl => _serverUrl;

    /// <summary>Идёт ли сейчас пробный период.</summary>
    public static bool IsTrial => State?.Mode == LicenseMode.Trial;

    /// <summary>Куплен ли ключ.</summary>
    public static bool IsLicensed => State?.Mode == LicenseMode.Licensed;

    /// <summary>Показывать ли кнопку «Попробовать бесплатно».</summary>
    public static bool TrialAvailable => State is null && !TrialGuard.WasUsed;

    /// <summary>Читает сохранённое состояние и адрес сервера. Вызывается один раз при старте.</summary>
    public static void Initialize(AppSettings settings)
    {
        // Переменная окружения удобна для отладки и приёмки: она перекрывает
        // и настройки, и значение по умолчанию.
        var fromEnvironment = Environment.GetEnvironmentVariable(LicenseOptions.ServerUrlEnvironmentVariable);

        _serverUrl = !string.IsNullOrWhiteSpace(fromEnvironment)
            ? fromEnvironment.Trim()
            : !string.IsNullOrWhiteSpace(settings.LicenseServerUrl)
                ? settings.LicenseServerUrl.Trim()
                : LicenseOptions.DefaultServerUrl;

        State = LicenseStorage.Load();

        if (State is null)
            State = RestoreTrialFromMark();
    }

    /// <summary>
    /// Файл лицензии удалили посреди пробного периода. Метка о выданных днях
    /// лежит отдельно, поэтому восстанавливаем остаток вместо новых трёх дней.
    /// </summary>
    private static LicenseState? RestoreTrialFromMark()
    {
        var expiry = TrialGuard.KnownExpiry;
        if (expiry is null || expiry <= DateTime.UtcNow)
            return null;

        var restored = new LicenseState
        {
            Mode = LicenseMode.Trial,
            HardwareId = HardwareId.Current,
            TrialStartedAt = expiry.Value.AddDays(-TrialDays),
            TrialExpiresAt = expiry.Value,
            LastValidatedAt = DateTime.UtcNow
        };

        LicenseStorage.Save(restored);
        return restored;
    }

    public static LicenseGateResult Evaluate()
    {
        var state = State;
        if (state is null)
            return LicenseGateResult.NeedsActivation;

        // Состояние от другого компьютера. Обычно оно сюда и не доедет
        // (файл не расшифруется), но проверка дешёвая.
        if (!string.IsNullOrEmpty(state.HardwareId) &&
            !string.Equals(state.HardwareId, HardwareId.Current, StringComparison.OrdinalIgnoreCase))
        {
            return LicenseGateResult.NeedsActivation;
        }

        var now = DateTime.UtcNow;

        if (state.Mode == LicenseMode.Trial)
        {
            // Часы перевели назад — самый дешёвый способ растянуть три дня.
            if (now < state.TrialStartedAt.AddDays(-1))
                return LicenseGateResult.TrialExpired;

            return now >= state.TrialExpiresAt
                ? LicenseGateResult.TrialExpired
                : LicenseGateResult.Allowed;
        }

        // Купленный ключ бессрочный, и приложение офлайновое: требовать
        // выхода в сеть «раз в N дней» не за что. Отозванный ключ
        // отлавливает фоновая проверка — но только тогда, когда связь
        // и так есть, и не мешая работать, когда её нет.
        return string.IsNullOrWhiteSpace(state.Key)
            ? LicenseGateResult.NeedsActivation
            : LicenseGateResult.Allowed;
    }

    /// <summary>Сколько дней пробного периода осталось.</summary>
    public static int TrialDaysLeft => IsTrial ? DaysLeft(State?.TrialExpiresAt) : 0;

    private static int DaysLeft(DateTime? until)
    {
        if (until is null)
            return 0;

        var left = until.Value - DateTime.UtcNow;
        return left <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(left.TotalDays);
    }

    /// <summary>
    /// Активация или повторная проверка ключа — с точки зрения сервера это одно
    /// и то же обращение: для уже занятого слота он просто обновит дату проверки.
    /// </summary>
    public static async Task<LicenseCallResult> ActivateAsync(string key)
    {
        var normalized = LicenseKeyFormat.Normalize(key);

        if (!LicenseKeyFormat.IsComplete(normalized))
        {
            return new LicenseCallResult
            {
                Status = LicenseCallStatus.InvalidKey,
                Message = "Ключ состоит из 16 символов: XXXX-XXXX-XXXX-XXXX. Проверьте, что он введён целиком."
            };
        }

        var client = new LicenseApiClient(_serverUrl);
        var result = await client.ActivateAsync(normalized, HardwareId.Current).ConfigureAwait(false);

        if (!result.IsOk)
            return result;

        var now = DateTime.UtcNow;
        State = new LicenseState
        {
            Mode = LicenseMode.Licensed,
            Key = normalized,
            HardwareId = HardwareId.Current,
            Token = result.Token ?? string.Empty,
            Email = result.Email ?? string.Empty,
            ActivatedAt = result.ActivatedAt ?? now,
            LastValidatedAt = now,
            DevicesUsed = result.DevicesUsed,
            DeviceLimit = result.DeviceLimit
        };
        LicenseStorage.Save(State);

        return result;
    }

    /// <summary>
    /// Запрашивает пробный период. Требует интернета: три дня выдаёт сервер,
    /// он же помнит, что этот компьютер их уже брал.
    /// </summary>
    public static async Task<LicenseCallResult> StartTrialAsync(string email)
    {
        var client = new LicenseApiClient(_serverUrl);
        var result = await client.StartTrialAsync(HardwareId.Current, email.Trim()).ConfigureAwait(false);

        if (result.IsOk && result.TrialExpiresAt is not null)
        {
            var startedAt = result.TrialStartedAt ?? DateTime.UtcNow;

            State = new LicenseState
            {
                Mode = LicenseMode.Trial,
                HardwareId = HardwareId.Current,
                Email = email.Trim(),
                TrialStartedAt = startedAt,
                TrialExpiresAt = result.TrialExpiresAt.Value,
                LastValidatedAt = DateTime.UtcNow
            };
            LicenseStorage.Save(State);
            TrialGuard.Remember(startedAt, result.TrialExpiresAt.Value);

            return result;
        }

        if (result.Status == LicenseCallStatus.TrialUsed)
        {
            // Сервер помнит выданный период, а локальная метка пропала —
            // возвращаем её на место, чтобы кнопка больше не предлагалась.
            TrialGuard.Remember(
                result.TrialStartedAt ?? DateTime.UtcNow,
                result.TrialExpiresAt ?? DateTime.UtcNow);
        }

        return result;
    }

    /// <summary>Освобождает слот устройства. Работает только онлайн.</summary>
    public static async Task<LicenseCallResult> DeactivateAsync()
    {
        var state = State;
        if (state is null || state.Mode != LicenseMode.Licensed)
        {
            return new LicenseCallResult
            {
                Status = LicenseCallStatus.InvalidKey,
                Message = "На этом компьютере нет активированной лицензии."
            };
        }

        var client = new LicenseApiClient(_serverUrl);
        var result = await client.DeactivateAsync(state.Key, HardwareId.Current).ConfigureAwait(false);

        if (result.IsOk)
            Clear();

        return result;
    }

    /// <summary>Забывает лицензию — при следующем запуске появится экран активации.</summary>
    public static void Clear()
    {
        State = null;
        LicenseStorage.Delete();
    }

    /// <summary>
    /// Фоновая проверка. Нужна ровно для одного: узнать, что ключ отозван —
    /// например, после возврата денег. Ничего не требует от пользователя:
    /// если связи нет, приложение просто продолжает работать, и так может
    /// продолжаться сколько угодно. Заблокировать может только внятный ответ
    /// сервера о том, что ключ больше не действует.
    /// </summary>
    public static void StartBackgroundCheck(Action onRevoked)
    {
        var state = State;

        // Пробному периоду проверять нечего: его срок уже записан локально.
        if (state is null || state.Mode != LicenseMode.Licensed)
            return;

        if (DateTime.UtcNow - state.LastValidatedAt < LicenseOptions.ValidationInterval)
            return;

        _ = Task.Run(async () =>
        {
            var client = new LicenseApiClient(_serverUrl);
            var result = await client.ValidateAsync(state.Key, HardwareId.Current).ConfigureAwait(false);

            if (result.IsOk)
            {
                state.LastValidatedAt = DateTime.UtcNow;
                if (result.DevicesUsed > 0)
                    state.DevicesUsed = result.DevicesUsed;
                LicenseStorage.Save(state);
                return;
            }

            if (result.Status != LicenseCallStatus.InvalidKey)
                return; // Нет сети или сервер не в духе — это не повод блокировать.

            Clear();
            Application.Current?.Dispatcher.Invoke(onRevoked);
        });
    }
}
