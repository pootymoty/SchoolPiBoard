using System.Security.Cryptography;
using System.Text;

namespace Whiteboard.Services;

/// <summary>
/// Лёгкое шифрование локальных файлов лицензии и пробного периода.
///
/// Ключ выводится из отпечатка компьютера, поэтому файл нельзя ни поправить
/// блокнотом, ни перенести на другую машину — там он просто не расшифруется.
/// Настоящей криптозащитой это не является и не задумано: ключ выводится тем
/// же кодом, что лежит рядом. Задача — поднять порог, а не остановить взлом.
/// </summary>
internal static class LocalCrypto
{
    private static readonly byte[] Magic = { (byte)'W', (byte)'B', (byte)'L', (byte)'1' };
    private const int IvLength = 16;

    public static byte[] Protect(string purpose, string plainText)
    {
        var plain = Encoding.UTF8.GetBytes(plainText);

        using var aes = CreateAes(purpose);
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var body = encryptor.TransformFinalBlock(plain, 0, plain.Length);

        var bytes = new byte[Magic.Length + IvLength + body.Length];
        Array.Copy(Magic, 0, bytes, 0, Magic.Length);
        Array.Copy(aes.IV, 0, bytes, Magic.Length, IvLength);
        Array.Copy(body, 0, bytes, Magic.Length + IvLength, body.Length);

        return bytes;
    }

    /// <summary>Расшифровывает или возвращает null, если данные чужие либо испорчены.</summary>
    public static string? Unprotect(string purpose, byte[]? data)
    {
        if (data is null || data.Length <= Magic.Length + IvLength)
            return null;

        for (var i = 0; i < Magic.Length; i++)
        {
            if (data[i] != Magic[i])
                return null;
        }

        try
        {
            using var aes = CreateAes(purpose);
            var iv = new byte[IvLength];
            Array.Copy(data, Magic.Length, iv, 0, IvLength);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var offset = Magic.Length + IvLength;
            var plain = decryptor.TransformFinalBlock(data, offset, data.Length - offset);

            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static Aes CreateAes(string purpose)
    {
        var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"Whiteboard.{purpose}.v1|{HardwareId.Current}"));
        return aes;
    }
}
