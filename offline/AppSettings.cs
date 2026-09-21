using System.IO;
using System.Text.Json;

namespace SchoolPiBoard.Services;

/// <summary>
/// Настройки приложения. Хранятся отдельно от досок и всегда в профиле
/// пользователя — иначе, сменив папку досок, приложение не нашло бы,
/// куда именно оно её перенесло.
/// </summary>
public class AppSettings
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DoskaPi");

    private static readonly string ConfigFile = Path.Combine(ConfigDirectory, "settings.json");

    /// <summary>Папка, в которой лежит файл с досками.</summary>
    public string DataFolder { get; set; } = ConfigDirectory;

    /// <summary>
    /// Адрес сервера лицензий. Обычно менять не нужно; поле существует,
    /// чтобы адрес можно было переключить без пересборки приложения.
    /// </summary>
    public string LicenseServerUrl { get; set; } = LicenseOptions.DefaultServerUrl;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigFile));
                if (settings is not null && !string.IsNullOrWhiteSpace(settings.DataFolder))
                    return settings;
            }
        }
        catch
        {
            // Повреждённые настройки не должны мешать запуску — берём значения по умолчанию.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(ConfigFile,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Не критично: приложение продолжит работать с текущими настройками.
        }
    }

    public static string DefaultFolder => ConfigDirectory;

    /// <summary>
    /// Разовый перенос всей папки данных из прежнего названия продукта
    /// (SchoolPiBoard) в новое (Доска Пи, технически — DoskaPi): доски,
    /// license.dat, custom_templates.json и settings.json лежат в одной
    /// папке, поэтому перенос самой папки переносит их все разом. Если
    /// новая папка уже существует — перенос уже случился или это чистая
    /// установка, трогать нечего.
    /// </summary>
    public static void MigrateFromLegacyFolder()
    {
        if (Directory.Exists(ConfigDirectory))
            return;

        var legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SchoolPiBoard");

        if (!Directory.Exists(legacy))
            return;

        try
        {
            Directory.Move(legacy, ConfigDirectory);
        }
        catch
        {
            // Не критично: старая папка остаётся на месте нетронутой, просто
            // перенос не случился в этот раз — можно попробовать в следующий
            // запуск (например, если файл был занят антивирусом).
        }
    }
}
