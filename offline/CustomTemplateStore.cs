using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SchoolPiBoard.Models;

namespace SchoolPiBoard.Services;

/// <summary>Заготовка, которую сохранил сам пользователь из объектов на доске.</summary>
public sealed class CustomTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;

    /// <summary>Исходная геометрия — как есть, без параметров пересборки.</summary>
    public List<BoardItem> Items { get; set; } = new();
}

internal sealed class CustomTemplateFile
{
    public int Version { get; set; } = 1;
    public List<CustomTemplate> Templates { get; set; } = new();
}

/// <summary>
/// Библиотека собственных заготовок пользователя — отдельно от каталога
/// встроенных (<see cref="BoardTemplateLibrary"/>): встроенные пересчитывают
/// геометрию по параметрам, собственные хранят её как есть, ровно в том
/// виде, в котором она была на доске в момент сохранения.
///
/// Файл один на всё приложение, лежит в профиле рядом с настройками —
/// как список досок к конкретной доске не привязан и не переезжает вместе
/// с папкой хранения досок при её смене (см. <c>SettingsDialog</c>).
/// </summary>
public static class CustomTemplateStore
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DoskaPi");

    private static readonly string FilePath = Path.Combine(ConfigDirectory, "custom_templates.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private static List<CustomTemplate>? _cache;

    /// <summary>Список изменился — вставлена, сохранена или удалена заготовка.</summary>
    public static event Action? Changed;

    public static IReadOnlyList<CustomTemplate> Templates
    {
        get
        {
            EnsureLoaded();
            return _cache!;
        }
    }

    private static void EnsureLoaded()
    {
        if (_cache is not null)
            return;

        _cache = new List<CustomTemplate>();
        try
        {
            if (File.Exists(FilePath))
            {
                var file = JsonSerializer.Deserialize<CustomTemplateFile>(File.ReadAllText(FilePath), JsonOptions);
                if (file?.Templates is not null)
                    _cache = file.Templates;
            }
        }
        catch
        {
            // Повреждённый файл не должен мешать работе — начинаем с пустой библиотеки,
            // старый файл остаётся на диске нетронутым до следующего успешного сохранения.
        }
    }

    /// <summary>
    /// Сохраняет копию переданных объектов как новую заготовку. Копия — чтобы
    /// дальнейшее редактирование объектов на доске (или их удаление) никак
    /// не затрагивало то, что уже легло в библиотеку.
    /// </summary>
    public static CustomTemplate Save(string title, IEnumerable<BoardItem> items)
    {
        EnsureLoaded();

        var template = new CustomTemplate
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Без названия" : title.Trim(),
            Items = items.Select(i => i.Clone()).ToList()
        };

        _cache!.Add(template);
        Persist();
        return template;
    }

    public static void Delete(string id)
    {
        EnsureLoaded();
        if (_cache!.RemoveAll(t => t.Id == id) > 0)
            Persist();
    }

    private static void Persist()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            var json = JsonSerializer.Serialize(new CustomTemplateFile { Templates = _cache! }, JsonOptions);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            // Не критично: заготовка остаётся в памяти до следующей удачной попытки
            // записи (например, при сохранении или удалении следующей заготовки).
        }

        Changed?.Invoke();
    }
}
