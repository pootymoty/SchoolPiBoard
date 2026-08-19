using System.Security.Cryptography;
using System.Text;

namespace Whiteboard.LicenseServer.Services;

/// <summary>
/// Генерация и нормализация ключей. Алфавит без символов, которые
/// путают при диктовке и наборе: нет I, L, O, а также 0 и 1.
/// </summary>
public static class LicenseKey
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public const int GroupSize = 4;
    public const int GroupCount = 4;
    public const int Length = GroupSize * GroupCount;

    /// <summary>
    /// Новый ключ. Символы берутся из криптографического генератора:
    /// последовательные или предсказуемые ключи подбирались бы перебором.
    /// </summary>
    public static string Create()
    {
        var symbols = new char[Length];
        for (var i = 0; i < symbols.Length; i++)
            symbols[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];

        return Group(symbols);
    }

    /// <summary>Приводит пользовательский ввод к каноническому XXXX-XXXX-XXXX-XXXX.</summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var symbols = new List<char>(Length);
        foreach (var symbol in input)
        {
            if (symbols.Count == Length)
                break;
            if (char.IsLetterOrDigit(symbol))
                symbols.Add(char.ToUpperInvariant(symbol));
        }

        return Group(symbols.ToArray());
    }

    /// <summary>Полный ли это ключ — 16 символов из нашего алфавита.</summary>
    public static bool IsWellFormed(string? normalized)
    {
        if (string.IsNullOrEmpty(normalized))
            return false;

        var symbols = 0;
        foreach (var symbol in normalized)
        {
            if (symbol == '-')
                continue;
            if (!Alphabet.Contains(symbol))
                return false;
            symbols++;
        }

        return symbols == Length;
    }

    private static string Group(char[] symbols)
    {
        var result = new StringBuilder(Length + GroupCount - 1);
        for (var i = 0; i < symbols.Length; i++)
        {
            if (i > 0 && i % GroupSize == 0)
                result.Append('-');
            result.Append(symbols[i]);
        }

        return result.ToString();
    }
}
