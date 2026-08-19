using System.IO;
using System.Text.Json;

namespace Whiteboard.Services;

/// <summary>
/// Хранит состояние лицензии (или пробного периода) в
/// %APPDATA%\WhiteboardApp\license.dat. Файл зашифрован — см. <see cref="LocalCrypto"/>.
/// </summary>
public static class LicenseStorage
{
    private const string Purpose = "LicenseFile";

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WhiteboardApp");

    public static string FilePath => Path.Combine(Folder, "license.dat");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>Читает состояние. Любая проблема с файлом = «лицензии нет».</summary>
    public static LicenseState? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            var json = LocalCrypto.Unprotect(Purpose, File.ReadAllBytes(FilePath));
            if (json is null)
                return null;

            var state = JsonSerializer.Deserialize<LicenseState>(json);
            if (state is null || state.Mode == LicenseMode.None)
                return null;

            // Лицензия без ключа и пробный период без даты окончания
            // одинаково бессмысленны — считаем файл испорченным.
            if (state.Mode == LicenseMode.Licensed && string.IsNullOrWhiteSpace(state.Key))
                return null;

            if (state.Mode == LicenseMode.Trial && state.TrialExpiresAt == default)
                return null;

            return state;
        }
        catch
        {
            // Повреждённый или чужой файл равносилен отсутствию лицензии —
            // пользователь просто увидит экран активации.
            return null;
        }
    }

    public static void Save(LicenseState state)
    {
        try
        {
            Directory.CreateDirectory(Folder);

            var bytes = LocalCrypto.Protect(Purpose, JsonSerializer.Serialize(state, JsonOptions));

            var tmp = FilePath + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            // Не смогли сохранить — приложение продолжит работать в этой сессии,
            // а при следующем запуске просто попросит активацию заново.
        }
    }

    public static void Delete()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        catch
        {
            // Файл может быть занят — не критично.
        }
    }
}
