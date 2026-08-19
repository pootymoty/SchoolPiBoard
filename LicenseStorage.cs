using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Whiteboard.Services;

/// <summary>
/// Хранит состояние лицензии в %APPDATA%\WhiteboardApp\license.dat.
///
/// Файл шифруется AES на ключе, выведенном из отпечатка компьютера. Это не
/// защита от целевого взлома (ключ выводится тем же кодом, что и в приложении),
/// а две практические вещи: файл нельзя отредактировать блокнотом и нельзя
/// просто скопировать на другой компьютер — там он не расшифруется.
/// </summary>
public static class LicenseStorage
{
    private static readonly byte[] Magic = { (byte)'W', (byte)'B', (byte)'L', (byte)'1' };
    private const int IvLength = 16;

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

            var raw = File.ReadAllBytes(FilePath);
            if (raw.Length <= Magic.Length + IvLength)
                return null;

            for (var i = 0; i < Magic.Length; i++)
            {
                if (raw[i] != Magic[i])
                    return null;
            }

            using var aes = CreateAes();
            var iv = new byte[IvLength];
            Array.Copy(raw, Magic.Length, iv, 0, IvLength);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var offset = Magic.Length + IvLength;
            var plain = decryptor.TransformFinalBlock(raw, offset, raw.Length - offset);

            var state = JsonSerializer.Deserialize<LicenseState>(Encoding.UTF8.GetString(plain));
            if (state is null || string.IsNullOrWhiteSpace(state.Key))
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

            var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state, JsonOptions));

            using var aes = CreateAes();
            aes.GenerateIV();
            using var encryptor = aes.CreateEncryptor();
            var body = encryptor.TransformFinalBlock(plain, 0, plain.Length);

            var bytes = new byte[Magic.Length + IvLength + body.Length];
            Array.Copy(Magic, 0, bytes, 0, Magic.Length);
            Array.Copy(aes.IV, 0, bytes, Magic.Length, IvLength);
            Array.Copy(body, 0, bytes, Magic.Length + IvLength, body.Length);

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

    private static Aes CreateAes()
    {
        var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(
            "Whiteboard.LicenseFile.v1|" + HardwareId.Current));
        return aes;
    }
}
