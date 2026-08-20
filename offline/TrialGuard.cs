using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace SchoolPiBoard.Services;

/// <summary>
/// Локальные следы пробного периода: реестр пользователя и папка ProgramData.
///
/// Решает, давать ли пробный период, всё равно сервер — эти метки нужны для
/// двух вещей. Во-первых, чтобы приложение знало ответ без интернета. Во-вторых,
/// чтобы удаление license.dat не превращалось в новые три дня: метки лежат
/// в других местах и переустановку приложения переживают.
///
/// Полностью защититься от чистки реестра и переустановки Windows нельзя —
/// именно поэтому последнее слово за записью в базе на сервере.
/// </summary>
public static class TrialGuard
{
    private const string Purpose = "TrialMark";

    // Названия нарочно неприметные: искать «trial» в реестре будут в первую очередь.
    private const string RegistryPath = @"Software\SchoolPiBoard";
    private const string RegistryValue = "AppState";

    private static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SchoolPiBoard");

    private static string DataFile => Path.Combine(DataFolder, "state.bin");

    /// <summary>Брали ли на этом компьютере пробный период.</summary>
    public static bool WasUsed => ReadMark() is not null;

    /// <summary>Когда заканчивается взятый пробный период (если метка сохранилась).</summary>
    public static DateTime? KnownExpiry => ReadMark()?.ExpiresAt;

    /// <summary>Запоминает выданный пробный период. Стереть его приложение не умеет — намеренно.</summary>
    public static void Remember(DateTime startedAt, DateTime expiresAt)
    {
        var mark = new TrialMark
        {
            Device = HardwareId.Current,
            StartedAt = startedAt,
            ExpiresAt = expiresAt
        };

        var payload = Convert.ToBase64String(
            LocalCrypto.Protect(Purpose, JsonSerializer.Serialize(mark)));

        WriteToRegistry(payload);
        WriteToDisk(payload);
    }

    private static TrialMark? ReadMark()
    {
        return Parse(ReadFromRegistry()) ?? Parse(ReadFromDisk());
    }

    private static TrialMark? Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            var json = LocalCrypto.Unprotect(Purpose, Convert.FromBase64String(payload));
            if (json is null)
                return null;

            var mark = JsonSerializer.Deserialize<TrialMark>(json);

            // Метка от другого компьютера (например, скопировали профиль) не считается.
            if (mark is null || !string.Equals(mark.Device, HardwareId.Current, StringComparison.OrdinalIgnoreCase))
                return null;

            return mark;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteToRegistry(string payload)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key?.SetValue(RegistryValue, payload, RegistryValueKind.String);
        }
        catch
        {
            // Останется метка на диске.
        }
    }

    private static string? ReadFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            return key?.GetValue(RegistryValue) as string;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteToDisk(string payload)
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(DataFile, payload);
        }
        catch
        {
            // ProgramData может быть закрыт политиками — останется метка в реестре.
        }
    }

    private static string? ReadFromDisk()
    {
        try
        {
            return File.Exists(DataFile) ? File.ReadAllText(DataFile) : null;
        }
        catch
        {
            return null;
        }
    }

    private sealed class TrialMark
    {
        public string Device { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
