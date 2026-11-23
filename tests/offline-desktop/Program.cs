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
    public static int Main()
    {
        try
        {
            Run();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Run()
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
            CheckHomeAppearance(window);
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

    private static void CheckHomeAppearance(MainWindow window)
    {
        var home = (HomeView)window.FindName("HomeScreen");
        var logo = (System.Windows.Controls.Image)home.FindName("BrandMark");
        Check(logo.Source is System.Windows.Media.Imaging.BitmapSource { PixelWidth: 468 }, "ресурс логотипа");
        var sort = (System.Windows.Controls.Button)home.FindName("SortDirectionButton");
        var filter = (System.Windows.Controls.ComboBox)home.FindName("DateFilter");
        foreach (var theme in new[] { "Light", "Dark" })
        {
            ThemeManager.SetPreference(theme);
            home.Measure(new Size(860, 560));
            home.Arrange(new Rect(0, 0, 860, 560));
            home.UpdateLayout();
            var start = sort.TranslatePoint(new Point(), home);
            var filterRight = filter.TranslatePoint(new Point(filter.ActualWidth, 0), home);
            Check(start.X - filterRight.X >= 10, "зазор перед сортировкой");
            Check(start.X + sort.ActualWidth < home.RenderSize.Width, "правая граница сортировки");
            var border = (System.Windows.Controls.Border)sort.Template.FindName("Bd", sort);
            Check(border.CornerRadius.TopRight == 9 && border.BorderThickness.Right == 1, "контур сортировки");
            var label = (System.Windows.Controls.TextBlock)home.FindName("SortText");
            var previous = label.Text;
            sort.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(label.Text != previous, "смена направления сортировки");
        }
    }

    private static void Check(bool value, string scenario)
    {
        if (!value) throw new InvalidOperationException(scenario);
    }
}
