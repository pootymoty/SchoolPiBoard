using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using SchoolPiBoard;
using SchoolPiBoard.Services;
using SchoolPiBoard.Views;

internal static class Regression
{
    [STAThread]
    public static void Main()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var settings = new AppSettings { DataFolder = folder };
            var store = new BoardStore(settings);
            var board = store.CreateBoard("первый");
            var stale = store.PrepareSave();
            board.Name = "последний";
            store.Save();
            stale();
            store.Load();
            Check(store.Boards.Single().Name == "последний", "устаревший снимок");

            var backup = File.ReadAllBytes(store.BackupFile);
            File.WriteAllText(store.DataFile, "{");
            store.Load();
            Check(store.Boards.Single().Name == "первый", "загрузка резерва");

            // Блокируем замену уже после записи временного файла.
            using (File.Open(store.DataFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { store.Save(); throw new Exception("ожидалась ошибка записи"); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            Check(File.ReadAllBytes(store.BackupFile).SequenceEqual(backup), "резерв после ошибки");
            store.Save();
            Check(File.ReadAllBytes(store.BackupFile).SequenceEqual(backup), "резерв после восстановления");
            store.Load();
            Check(store.Boards.Single().Name == "первый", "восстановленный файл");

            var app = new App();
            app.InitializeComponent();
            var window = new MainWindow();
            var editor = (EditorView)window.FindName("EditorScreen");
            var field = typeof(EditorView).GetField("_pendingWrite", BindingFlags.NonPublic | BindingFlags.Instance)!;
            foreach (var close in new[] { false, true })
            {
                var pending = Task.Run(async () => await Task.Delay(150));
                field.SetValue(editor, pending);
                if (close)
                    typeof(MainWindow).GetMethod("OnClosing", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .Invoke(window, new object[] { new CancelEventArgs() });
                else
                    window.ShowHome();
                Check(pending.IsCompletedSuccessfully && field.GetValue(editor) == null,
                    close ? "закрытие до конца записи" : "выход до конца записи");
            }
            Console.WriteLine("Desktop save regressions passed.");
        }
        finally { Directory.Delete(folder, true); }
    }

    private static void Check(bool value, string scenario)
    {
        if (!value) throw new InvalidOperationException(scenario);
    }
}
