using System.Threading.Tasks;
using System.Windows;

namespace Whiteboard.Services;

/// <summary>Что делать с лицензией на старте приложения.</summary>
public enum LicenseGateResult
{
    /// <summary>Лицензия есть и проверка не просрочена — пускаем.</summary>
    Allowed,

    /// <summary>Лицензии нет — нужен экран активации (требуется интернет).</summary>
    NeedsActivation,

    /// <summary>Лицензия есть, но офлайн-период вышел — нужна проверка на сервере.</summary>
    NeedsRevalidation
}

/// <summary>
/// Вся логика лицензии в одном месте: состояние, активация, офлайн-период
/// и фоновая проверка. Логику самой доски не трогает — это слой поверх.
/// </summary>
public static class LicenseManager
{
    /// <summary>Сколько дней приложение работает без связи с сервером.</summary>
    public const int GraceDays = LicenseOptions.GraceDays;

    private static string _serverUrl = LicenseOptions.DefaultServerUrl;

    public static LicenseState? State { get; private set; }

    public static string ServerUrl => _serverUrl;

    /// <summary>Читает сохранённую лицензию и адрес сервера. Вызывается один раз при старте.</summary>
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
    }

    public static LicenseGateResult Evaluate()
    {
        var state = State;
        if (state is null || string.IsNullOrWhiteSpace(state.Key))
            return LicenseGateResult.NeedsActivation;

        // Файл лицензии от другого компьютера. Обычно он сюда и не доедет
        // (не расшифруется), но проверка дешёвая.
        if (!string.Equals(state.HardwareId, HardwareId.Current, StringComparison.OrdinalIgnoreCase))
            return LicenseGateResult.NeedsActivation;

        var now = DateTime.UtcNow;

        // Часы перевели назад — самый простой способ растянуть офлайн-период.
        // Сутки допуска на переезд между часовыми поясами и правку времени.
        if (now < state.LastValidatedAt.AddDays(-1))
            return LicenseGateResult.NeedsRevalidation;

        if (now - state.LastValidatedAt > TimeSpan.FromDays(GraceDays))
            return LicenseGateResult.NeedsRevalidation;

        return LicenseGateResult.Allowed;
    }

    /// <summary>Сколько дней офлайн-периода осталось (0, если он уже вышел).</summary>
    public static int OfflineDaysLeft
    {
        get
        {
            if (State is null)
                return 0;
            var left = State.LastValidatedAt.AddDays(GraceDays) - DateTime.UtcNow;
            return left <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(left.TotalDays);
        }
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

    /// <summary>Освобождает слот устройства. Работает только онлайн.</summary>
    public static async Task<LicenseCallResult> DeactivateAsync()
    {
        var state = State;
        if (state is null)
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
    /// Фоновая проверка раз в сутки. UI не блокирует и молчит при любых
    /// проблемах со связью: заблокировать приложение может только внятный
    /// ответ сервера о том, что ключ больше не действует.
    /// </summary>
    public static void StartBackgroundCheck(Action onRevoked)
    {
        var state = State;
        if (state is null)
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
