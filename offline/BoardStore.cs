using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SchoolPiBoard.Models;

namespace SchoolPiBoard.Services;

public class BoardStore
{
    public const int ArchiveAfterDays = 30;
    public const string FileName = "boards.json";

    // Отступы в файле досок увеличивали его в разы, а вместе с размером —
    // и время сериализации. Файл машинный, читать его глазами не нужно.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppSettings _settings;

    public List<Board> Boards { get; private set; } = new();

    public string DataFolder => _settings.DataFolder;
    public string DataFile => Path.Combine(DataFolder, FileName);
    public string BackupFile => Path.Combine(DataFolder, "boards.backup.json");

    public BoardStore(AppSettings settings)
    {
        _settings = settings;
        Directory.CreateDirectory(DataFolder);
    }

    public void Load()
    {
        Boards = ReadFile(DataFile) ?? ReadFile(BackupFile) ?? new List<Board>();
    }

    private static List<Board>? ReadFile(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var store = JsonSerializer.Deserialize<BoardStoreFile>(File.ReadAllText(path), JsonOptions);
            return store?.Boards;
        }
        catch
        {
            return null;
        }
    }

    public void Save() => WriteSnapshot(CreateSnapshot(), DataFolder, DataFile, BackupFile);

    /// <summary>
    /// Снимок для сохранения. Делается в потоке интерфейса и стоит одного
    /// прохода по списку досок; всё дорогое — сериализация и запись —
    /// происходит потом в фоне и уже не может помешать рисованию.
    /// </summary>
    public BoardStoreFile CreateSnapshot() =>
        new() { Boards = Boards.Select(board => board.SnapshotCopy()).ToList() };

    /// <summary>
    /// Запись снимка. Статический метод без обращения к состоянию хранилища:
    /// его безопасно вызывать из фонового потока.
    /// </summary>
    public static void WriteSnapshot(BoardStoreFile snapshot, string folder, string dataFile, string backupFile)
    {
        Directory.CreateDirectory(folder);

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var tmp = dataFile + ".tmp";
        File.WriteAllText(tmp, json);

        if (File.Exists(dataFile))
        {
            try
            {
                File.Copy(dataFile, backupFile, overwrite: true);
            }
            catch
            {
                // Резервная копия необязательна.
            }
        }

        File.Move(tmp, dataFile, overwrite: true);
    }

    public Board CreateBoard(string name)
    {
        var board = new Board
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Новая доска" : name.Trim()
        };
        Boards.Add(board);
        Save();
        return board;
    }

    public void DeleteBoard(Board board)
    {
        Boards.Remove(board);
        Save();
    }

    public void TouchModified(Board board)
    {
        board.Modified = DateTime.Now;
        board.Archived = false;
    }

    public int AutoArchive()
    {
        var cutoff = DateTime.Now.AddDays(-ArchiveAfterDays);
        var count = 0;
        foreach (var b in Boards)
        {
            if (!b.Archived && b.Modified < cutoff)
            {
                b.Archived = true;
                count++;
            }
        }
        if (count > 0)
            Save();
        return count;
    }

    /// <summary>
    /// Меняет папку хранения. Если <paramref name="moveExisting"/> — файл досок
    /// переносится, иначе приложение просто начинает работать с содержимым
    /// новой папки (или с пустым списком, если её ещё нет).
    /// </summary>
    public void ChangeFolder(string newFolder, bool moveExisting)
    {
        var oldFile = DataFile;
        var oldBackup = BackupFile;

        Directory.CreateDirectory(newFolder);

        if (moveExisting)
        {
            var target = Path.Combine(newFolder, FileName);
            if (File.Exists(oldFile))
                File.Copy(oldFile, target, overwrite: true);

            _settings.DataFolder = newFolder;
            _settings.Save();

            // Копию удаляем только после успешного переноса.
            TryDelete(oldFile);
            TryDelete(oldBackup);
        }
        else
        {
            _settings.DataFolder = newFolder;
            _settings.Save();
        }

        Load();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Файл может быть занят — не критично.
        }
    }
}
