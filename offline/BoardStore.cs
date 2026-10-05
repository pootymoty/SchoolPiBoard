using System.IO;
using System.IO.Compression;
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

    public Board CreateBoard(string name, AppSettings? settings = null, bool darkTheme = true)
    {
        var board = new Board
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Новая доска" : name.Trim(),
            // Формат — как у последнего изменённого холста, а при первом запуске:
            // тёмная тема — тёмный холст, светлая — белый, оба без разлиновки.
            // Умолчания класса Board не трогаем: от них зависит чтение старых досок.
            BackgroundColor = settings?.NewBoardBackgroundColor ?? (darkTheme ? "#FF1B1B1F" : "#FFFFFFFF"),
            Grid = settings?.NewBoardGrid ?? GridStyle.Solid,
            GridColor = settings?.NewBoardGridColor ?? "",
            GridOpacity = settings?.NewBoardGridOpacity ?? 1.0
        };
        Boards.Add(board);
        Save();
        return board;
    }

    public void DeleteBoard(Board board)
    {
        Boards.Remove(board);
        Save();
        TryDelete(ArchivePath(board));
    }

    public void TouchModified(Board board)
    {
        board.Modified = DateTime.Now;
        board.Archived = false;
        board.Compressed = false;
    }

    // =====================================================================
    //  Сжатый архив
    // =====================================================================

    public string ArchiveFolder => Path.Combine(DataFolder, "archive");

    public string ArchivePath(Board board) => Path.Combine(ArchiveFolder, board.Id + ".json.gz");

    /// <summary>
    /// Записывает содержимое доски в сжатый файл и сразу читает его обратно:
    /// пока файл не проверен, из основного файла ничего не убирается.
    /// Статический, без обращения к состоянию хранилища — вызывается из фона.
    /// </summary>
    public static void WriteArchiveFile(string path, List<BoardItem> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";

        using (var file = File.Create(tmp))
        using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
            JsonSerializer.Serialize(gzip, items, JsonOptions);

        var check = ReadArchiveFile(tmp);
        if (check.Count != items.Count)
        {
            File.Delete(tmp);
            throw new InvalidDataException("Проверка архива не прошла.");
        }

        File.Move(tmp, path, overwrite: true);
    }

    public static List<BoardItem> ReadArchiveFile(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<List<BoardItem>>(gzip, JsonOptions) ?? new List<BoardItem>();
    }

    /// <summary>
    /// Возвращает содержимое сжатой доски в память перед открытием.
    /// Файл архива остаётся на диске до следующего запуска: пока основной
    /// файл ещё помнит доску сжатой, он нужен.
    /// </summary>
    public void EnsureLoaded(Board board)
    {
        if (!board.Compressed)
            return;

        board.Items = ReadArchiveFile(ArchivePath(board));
        board.Compressed = false;
    }

    /// <summary>
    /// Завершает сжатие: убирает содержимое из основного файла, если за время
    /// записи с доской ничего не случилось (её не открыли и не вернули из архива).
    /// </summary>
    public bool FinishCompression(Board board, List<BoardItem> written)
    {
        if (!board.Archived || board.Compressed || !ReferenceEquals(board.Items, written))
            return false;

        board.Items = new List<BoardItem>();
        board.Compressed = true;
        Save();
        return true;
    }

    /// <summary>
    /// Убирает файлы архива, которым больше не соответствует ни одна сжатая доска.
    /// Только файлы старше двух суток: резервная копия основного файла отстаёт
    /// на одно сохранение и может ещё ссылаться на недавний архив.
    /// </summary>
    public void CleanupArchiveFolder()
    {
        try
        {
            if (!Directory.Exists(ArchiveFolder))
                return;

            var keep = Boards.Where(b => b.Compressed).Select(b => b.Id + ".json.gz").ToHashSet();
            var limit = DateTime.UtcNow.AddDays(-2);

            foreach (var file in Directory.EnumerateFiles(ArchiveFolder))
            {
                var name = Path.GetFileName(file);
                if (keep.Contains(name) || File.GetLastWriteTimeUtc(file) > limit)
                    continue;
                TryDelete(file);
            }
        }
        catch
        {
            // Уборка не критична.
        }
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
        var oldArchive = ArchiveFolder;

        Directory.CreateDirectory(newFolder);

        if (moveExisting)
        {
            var target = Path.Combine(newFolder, FileName);
            if (File.Exists(oldFile))
                File.Copy(oldFile, target, overwrite: true);

            // Сжатые доски лежат в подпапке — без неё в новом месте они бы остались пустыми.
            if (Directory.Exists(oldArchive))
            {
                var newArchive = Path.Combine(newFolder, "archive");
                Directory.CreateDirectory(newArchive);
                foreach (var file in Directory.EnumerateFiles(oldArchive))
                    File.Copy(file, Path.Combine(newArchive, Path.GetFileName(file)), overwrite: true);
            }

            _settings.DataFolder = newFolder;
            _settings.Save();

            // Копию удаляем только после успешного переноса.
            TryDelete(oldFile);
            TryDelete(oldBackup);
            try
            {
                if (Directory.Exists(oldArchive))
                    Directory.Delete(oldArchive, recursive: true);
            }
            catch
            {
                // Не критично.
            }
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
