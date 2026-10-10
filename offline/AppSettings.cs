using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SchoolPiBoard.Models;

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

    private static readonly string DefaultExportFolderPath =
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    /// <summary>Папка, в которой лежит файл с досками.</summary>
    public string DataFolder { get; set; } = ConfigDirectory;

    /// <summary>
    /// Папка, которую диалог «Экспорт в PNG» открывает по умолчанию.
    /// Без своей настройки Windows сама помнит последнюю выбранную папку
    /// между запусками диалога, но не между переустановками и не всегда
    /// предсказуемо — своя настройка снимает эту неопределённость.
    /// </summary>
    public string ExportFolder { get; set; } = DefaultExportFolderPath;

    /// <summary>
    /// Адрес сервера лицензий. Обычно менять не нужно; поле существует,
    /// чтобы адрес можно было переключить без пересборки приложения.
    /// </summary>
    public string LicenseServerUrl { get; set; } = LicenseOptions.DefaultServerUrl;

    /// <summary>Имя подпапки внутри <see cref="ExportFolder"/>, куда падают все экспортированные картинки.</summary>
    public const string ExportSubfolderName = "Доска Пи";

    /// <summary>
    /// Куда реально сохраняются картинки: подпапка внутри выбранной папки.
    /// Только в ней программа что-то создаёт, поэтому при удалении
    /// программы её можно снести целиком, не задев чужие файлы в «Изображениях».
    /// </summary>
    [JsonIgnore]
    public string ExportDirectory => Path.Combine(
        string.IsNullOrWhiteSpace(ExportFolder) ? DefaultExportFolderPath : ExportFolder,
        ExportSubfolderName);

    // Формат холста для новых досок: запоминается при любом изменении фона,
    // цвета/вида разлиновки на любой доске. Пока null — берётся умолчание
    // по теме (тёмная тема — тёмный холст, светлая — белый, без сетки).
    /// <summary>
    /// «Умный фон»: новые доски повторяют последний изменённый формат фона
    /// (до первого изменения — по теме), а белое/чёрное перо и текст
    /// подстраиваются под фон открытой доски. Выключено — всегда белый лист
    /// без разлиновки и ничего не подстраивается.
    /// </summary>
    public bool SmartBoardFormat { get; set; } = true;

    public string? NewBoardBackgroundColor { get; set; }
    public GridStyle? NewBoardGrid { get; set; }
    public string? NewBoardGridColor { get; set; }
    public double? NewBoardGridOpacity { get; set; }

    /// <summary>Тема оформления: "System" (как в Windows), "Light" или "Dark".</summary>
    public string Theme { get; set; } = "System";

    public static AppSettings Load()
    {
        var settings = LoadCore();
        settings.PublishPathsForUninstaller();
        return settings;
    }

    /// <summary>
    /// Деинсталлятор читает адреса папок досок и картинок из реестра:
    /// settings.json в UTF-8, а Inno Setup читает его как ANSI, и пути с
    /// русскими буквами исказились бы. Ключ только для чтения установщиком.
    /// </summary>
    private void PublishPathsForUninstaller()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\DoskaPi\Paths");
            key?.SetValue("DataFolder", DataFolder);
            key?.SetValue("ExportDirectory", ExportDirectory);
        }
        catch
        {
            // Не критично: деинсталлятор возьмёт стандартные папки.
        }
    }

    private static AppSettings LoadCore()
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
            PublishPathsForUninstaller();
        }
        catch
        {
            // Не критично: приложение продолжит работать с текущими настройками.
        }
    }

    public static string DefaultFolder => ConfigDirectory;
    public static string DefaultExportFolder => DefaultExportFolderPath;

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
