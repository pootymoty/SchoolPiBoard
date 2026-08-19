using System.IO;
using System.Text;
using System.Windows;
using Whiteboard.Services;
using Whiteboard.Views;

namespace Whiteboard;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WhiteboardApp", "crash.log");

    public App()
    {
        // Обработчики ставим до разбора XAML: иначе ранняя ошибка
        // закрывает приложение без единого сообщения.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Report(e.ExceptionObject as Exception, "AppDomain");

        DispatcherUnhandledException += (_, e) =>
        {
            Report(e.Exception, "Dispatcher");
            e.Handled = true;
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report(e.Exception, "Task");
            e.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            ThemeManager.Initialize();

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            Report(ex, "Startup");
            Shutdown(1);
        }
    }

    private static void Report(Exception? exception, string source)
    {
        if (exception is null)
            return;

        var chain = new StringBuilder();
        var current = exception;
        var level = 0;

        while (current is not null && level < 10)
        {
            chain.AppendLine($"[{level}] {current.GetType().FullName}")
                 .AppendLine($"     {current.Message}");

            if (current is System.Windows.Markup.XamlParseException xaml)
            {
                chain.AppendLine($"     Файл:   {xaml.BaseUri}")
                     .AppendLine($"     Строка: {xaml.LineNumber}, позиция: {xaml.LinePosition}");
            }

            current = current.InnerException;
            level++;
        }

        var root = exception;
        while (root.InnerException is not null)
            root = root.InnerException;

        var text = new StringBuilder()
            .AppendLine("=======================================")
            .AppendLine($"Время:    {DateTime.Now:dd.MM.yyyy HH:mm:ss}")
            .AppendLine($"Источник: {source}")
            .AppendLine()
            .AppendLine("--- Цепочка исключений ---")
            .Append(chain)
            .AppendLine("--- Полный стек ---")
            .AppendLine(exception.ToString())
            .AppendLine()
            .ToString();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, text, Encoding.UTF8);
        }
        catch
        {
            // Остаётся только диалог.
        }

        try
        {
            MessageBox.Show(
                $"Ошибка при работе приложения.\n\n" +
                $"ПЕРВОПРИЧИНА:\n{root.GetType().Name}\n{root.Message}\n\n" +
                $"Подробности: {LogPath}",
                "Whiteboard — ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Данные уже в логе.
        }
    }
}
