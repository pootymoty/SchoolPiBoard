using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SchoolPiBoard.Models;

namespace SchoolPiBoard.Services;

public class BoardStore
{
    public const int ArchiveAfterDays = 30;
    public const string FileName = "boards.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
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

    public void Save()
    {
        Directory.CreateDirectory(DataFolder);

        var json = JsonSerializer.Serialize(new BoardStoreFile { Boards = Boards }, JsonOptions);
        var tmp = DataFile + ".tmp";
        File.WriteAllText(tmp, json);

        if (File.Exists(DataFile))
        {
            try
            {
                File.Copy(DataFile, BackupFile, overwrite: true);
            }
            catch
            {
                // Резервная копия необязательна.
            }
        }

        File.Move(tmp, DataFile, overwrite: true);
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
