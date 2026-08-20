using System.Text;

namespace Whiteboard.Services;

/// <summary>На каком основании приложение работает.</summary>
public enum LicenseMode
{
    /// <summary>Оснований нет — нужен ключ или пробный период.</summary>
    None = 0,

    /// <summary>Куплен бессрочный ключ.</summary>
    Licensed = 1,

    /// <summary>Идёт пробный период.</summary>
    Trial = 2
}

/// <summary>
/// То, что приложение помнит о лицензии между запусками.
/// Сохраняется в %APPDATA%\WhiteboardApp\license.dat (см. <see cref="LicenseStorage"/>).
/// </summary>
public class LicenseState
{
    /// <summary>
    /// Ключ или пробный период. Значение по умолчанию — «ключ»: так файлы,
    /// записанные до появления пробного периода, читаются без миграции.
    /// </summary>
    public LicenseMode Mode { get; set; } = LicenseMode.Licensed;

    /// <summary>Ключ в каноническом виде XXXX-XXXX-XXXX-XXXX.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Компьютер, на котором ключ активирован.</summary>
    public string HardwareId { get; set; } = string.Empty;

    /// <summary>Подписанный сервером токен активации. Клиент его не разбирает.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Почта покупателя — показывается в разделе «О программе».</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>UTC-время первой активации на этом компьютере.</summary>
    public DateTime ActivatedAt { get; set; }

    /// <summary>UTC-время последней успешной проверки. От него считается офлайн-период.</summary>
    public DateTime LastValidatedAt { get; set; }

    /// <summary>Сколько устройств занято ключом на момент последней проверки.</summary>
    public int DevicesUsed { get; set; }

    /// <summary>Сколько устройств разрешено — приходит с сервера, по умолчанию 2.</summary>
    public int DeviceLimit { get; set; } = LicenseOptions.DefaultDeviceLimit;

    /// <summary>UTC-время начала пробного периода.</summary>
    public DateTime TrialStartedAt { get; set; }

    /// <summary>UTC-время окончания пробного периода.</summary>
    public DateTime TrialExpiresAt { get; set; }

    public bool IsTrial => Mode == LicenseMode.Trial;
}

/// <summary>Настройки лицензирования на стороне клиента.</summary>
public static class LicenseOptions
{
    /// <summary>
    /// Адрес сервера ключей. Значение зашивается в exe при сборке, поэтому
    /// менять его нужно до выпуска установщика. Для отладки и для переезда
    /// на другой адрес без пересборки есть переменная окружения
    /// WHITEBOARD_LICENSE_URL и поле LicenseServerUrl в settings.json.
    /// </summary>
    public const string DefaultServerUrl = "https://keys.school-pi.online";

    public const string ServerUrlEnvironmentVariable = "WHITEBOARD_LICENSE_URL";

    /// <summary>Максимум устройств на один ключ (окончательное слово всё равно за сервером).</summary>
    public const int DefaultDeviceLimit = 2;

    /// <summary>
    /// Не чаще одного раза в сутки приложение молча спрашивает сервер,
    /// не отозван ли ключ. Ничего не требует и ничего не блокирует при
    /// отсутствии связи — см. <see cref="LicenseManager.StartBackgroundCheck"/>.
    /// </summary>
    public static readonly TimeSpan ValidationInterval = TimeSpan.FromDays(1);

    /// <summary>
    /// Длительность пробного периода. Значение справочное — настоящий срок
    /// назначает сервер, здесь оно нужно только для текстов на экране.
    /// </summary>
    public const int TrialDays = 3;

}

/// <summary>
/// Приведение ключа к каноническому виду. Алфавит ключа не содержит
/// символов, которые легко перепутать (I, L, O, 0, 1), поэтому достаточно
/// убрать разделители и поднять регистр.
/// </summary>
public static class LicenseKeyFormat
{
    public const int GroupCount = 4;
    public const int GroupSize = 4;
    public const int Length = GroupCount * GroupSize;

    /// <summary>«abcd efgh…» → «ABCD-EFGH-…». Лишние символы отбрасываются.</summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var letters = new StringBuilder(Length);
        foreach (var symbol in input)
        {
            if (letters.Length == Length)
                break;
            if (char.IsLetterOrDigit(symbol))
                letters.Append(char.ToUpperInvariant(symbol));
        }

        var result = new StringBuilder(Length + GroupCount - 1);
        for (var i = 0; i < letters.Length; i++)
        {
            if (i > 0 && i % GroupSize == 0)
                result.Append('-');
            result.Append(letters[i]);
        }

        return result.ToString();
    }

    /// <summary>Похоже ли введённое на полный ключ (16 символов).</summary>
    public static bool IsComplete(string? input)
    {
        var normalized = Normalize(input);
        return normalized.Length == Length + GroupCount - 1;
    }
}
