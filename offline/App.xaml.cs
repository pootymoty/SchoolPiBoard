using System.IO;
using System.Text;
using System.Windows;
using SchoolPiBoard.Services;
using SchoolPiBoard.Views;

namespace SchoolPiBoard;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SchoolPiBoard", "crash.log");

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

            // Пока идёт активация, ни одного окна приложения ещё нет: при режиме
            // по умолчанию WPF закрыл бы приложение сразу после закрытия окна
            // активации, не дав открыть список досок.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            LicenseManager.Initialize(AppSettings.Load());

            // Установщик на последней странице предлагает ввести ключ сразу —
            // тогда приложение запускается с этим ключом командной строки.
            var forceKeyEntry = e.Args.Any(argument =>
                string.Equals(argument, "--activate", StringComparison.OrdinalIgnoreCase));

            if (!PassLicenseGate(forceKeyEntry))
            {
                Shutdown(0);
                return;
            }

            var window = new MainWindow();
            MainWindow = window;
            window.Show();

            ShutdownMode = ShutdownMode.OnLastWindowClose;

            // Фоновая проверка: раз в сутки, без блокировки интерфейса.
            LicenseManager.StartBackgroundCheck(OnLicenseRevoked);
        }
        catch (Exception ex)
        {
            Report(ex, "Startup");
            Shutdown(1);
        }
    }

    /// <summary>
    /// Пускает приложение дальше, если лицензия в порядке. Иначе показывает
    /// экран активации и возвращает его результат.
    /// </summary>
    private static bool PassLicenseGate(bool forceKeyEntry)
    {
        var verdict = LicenseManager.Evaluate();

        if (verdict == LicenseGateResult.Allowed)
        {
            // Работать уже можно; ключ спрашиваем только потому, что об этом
            // попросили явно. Отказ от ввода ничего не ломает.
            if (forceKeyEntry)
                new ActivationWindow(ActivationMode.ManualEntry).ShowDialog();

            return true;
        }

        var mode = verdict == LicenseGateResult.TrialExpired
            ? ActivationMode.TrialExpired
            : ActivationMode.FirstRun;

        var activation = new ActivationWindow(mode);
        activation.ShowDialog();
        return activation.Activated;
    }

    /// <summary>Сервер подтвердил, что ключ отозван — дальше работать нельзя.</summary>
    private static void OnLicenseRevoked()
    {
        const string title = "Лицензия больше не действует";
        const string message =
            "Сервер сообщил, что этот ключ отозван, поэтому SchoolPiBoard закроется.\n\n" +
            "Доски и настройки остаются на компьютере — они снова откроются, " +
            "как только будет введён действующий ключ.";

        var owner = Current?.MainWindow;
        if (owner is not null)
            ConfirmDialog.Info(owner, title, message);
        else
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

        Current?.Shutdown();
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
                "SchoolPiBoard — ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Данные уже в логе.
        }
    }
}
