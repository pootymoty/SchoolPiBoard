using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SchoolPiBoard.Services;

namespace SchoolPiBoard.Views;

/// <summary>Зачем открыт экран активации.</summary>
public enum ActivationMode
{
    /// <summary>Первый запуск: ключа нет.</summary>
    FirstRun,

    /// <summary>Пробный период истёк.</summary>
    TrialExpired,

    /// <summary>Пользователь сам открыл ввод ключа (настройки или ярлык с --activate).</summary>
    ManualEntry
}

/// <summary>
/// Экран активации. Показывается до списка досок: без ключа или действующего
/// пробного периода приложение дальше не идёт.
///
/// Внутри две панели — ввод ключа и запрос пробного периода. Разделять их
/// на два окна незачем: пользователь выбирает между ними в одном месте.
/// </summary>
public class ActivationWindow : Window
{
    private readonly ActivationMode _mode;

    private readonly TextBlock _title;
    private readonly TextBlock _description;
    private readonly StackPanel _keyPane;
    private readonly StackPanel _trialPane;
    private readonly TextBox _keyBox;
    private readonly TextBox _emailBox;
    private readonly TextBlock _status;
    private readonly Button _primaryButton;
    private readonly Button _trialButton;
    private readonly Button _exitButton;

    private bool _formatting;
    private bool _busy;
    private bool _trialVisible;

    /// <summary>Можно ли пускать пользователя в приложение.</summary>
    public bool Activated { get; private set; }

    public ActivationWindow(ActivationMode mode)
    {
        _mode = mode;

        Title = "SchoolPiBoard — активация";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = mode != ActivationMode.ManualEntry;
        Background = (Brush)Application.Current.Resources["AppBg"];
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        try
        {
            Icon = new BitmapImage(new Uri("pack://application:,,,/schoolpiboard.ico"));
        }
        catch
        {
            // Без иконки окно всё равно работает.
        }

        var root = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };

        _title = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(_title);

        _description = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 13,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 18)
        };
        root.Children.Add(_description);

        // ---------- панель ввода ключа ----------

        _keyPane = new StackPanel();

        _keyBox = new TextBox
        {
            Text = LicenseManager.State?.Key ?? string.Empty,
            FontSize = 20,
            FontFamily = new FontFamily("Consolas, Courier New"),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(10, 10, 10, 10),
            MaxLength = LicenseKeyFormat.Length + LicenseKeyFormat.GroupCount - 1,
            Background = (Brush)Application.Current.Resources["AppBg2"],
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            BorderBrush = (Brush)Application.Current.Resources["Accent"],
            BorderThickness = new Thickness(1),
            CaretBrush = (Brush)Application.Current.Resources["TextPrimary"]
        };
        _keyBox.TextChanged += OnKeyTextChanged;
        _keyPane.Children.Add(_keyBox);

        _keyPane.Children.Add(new TextBlock
        {
            Text = "Формат ключа: XXXX-XXXX-XXXX-XXXX",
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        });

        root.Children.Add(_keyPane);

        // ---------- панель пробного периода ----------

        _trialPane = new StackPanel { Visibility = Visibility.Collapsed };

        _emailBox = new TextBox
        {
            Text = LicenseManager.State?.Email ?? string.Empty,
            FontSize = 16,
            Padding = new Thickness(10, 9, 10, 9),
            MaxLength = 254,
            Background = (Brush)Application.Current.Resources["AppBg2"],
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            BorderBrush = (Brush)Application.Current.Resources["Accent"],
            BorderThickness = new Thickness(1),
            CaretBrush = (Brush)Application.Current.Resources["TextPrimary"]
        };
        _trialPane.Children.Add(_emailBox);

        _trialPane.Children.Add(new TextBlock
        {
            Text = "Почта нужна, чтобы пробный период выдавался один раз. " +
                   "Она же подставится при покупке ключа.",
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        root.Children.Add(_trialPane);

        // ---------- сообщение и кнопки ----------

        _status = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 13,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 16, 0, 0),
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(_status);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };

        _exitButton = new Button
        {
            Style = (Style)Application.Current.Resources["TextButton"],
            MinWidth = 104,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        _exitButton.Click += (_, _) => GoBackOrClose();
        buttons.Children.Add(_exitButton);

        _trialButton = new Button
        {
            Content = $"Попробовать {LicenseManager.TrialDays} дня",
            Style = (Style)Application.Current.Resources["TextButton"],
            MinWidth = 168,
            Margin = new Thickness(10, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        _trialButton.Click += (_, _) => ShowTrialPane();
        buttons.Children.Add(_trialButton);

        _primaryButton = new Button
        {
            Style = (Style)Application.Current.Resources["AccentButton"],
            MinWidth = 150,
            Margin = new Thickness(10, 0, 0, 0),
            IsDefault = true
        };
        _primaryButton.Click += (_, _) => _ = SubmitAsync();
        buttons.Children.Add(_primaryButton);

        root.Children.Add(buttons);

        Content = root;
        PreviewKeyDown += OnPreviewKeyDown;

        ShowKeyPane();

        Loaded += (_, _) =>
        {
            _keyBox.Focus();
            _keyBox.CaretIndex = _keyBox.Text.Length;
        };
    }

    // ---------- переключение панелей ----------

    private void ShowKeyPane()
    {
        _trialVisible = false;
        _keyPane.Visibility = Visibility.Visible;
        _trialPane.Visibility = Visibility.Collapsed;

        _title.Text = _mode switch
        {
            ActivationMode.TrialExpired => "Пробный период закончился",
            ActivationMode.ManualEntry => "Ввод ключа",
            _ => "Активация SchoolPiBoard"
        };

        _description.Text = _mode switch
        {
            ActivationMode.TrialExpired =>
                $"Бесплатные {LicenseManager.TrialDays} дня истекли. Доски и настройки никуда не делись — " +
                "они откроются сразу, как только вы введёте купленный ключ.",

            ActivationMode.ManualEntry =>
                "Введите ключ из письма. Если сейчас идёт пробный период, он сменится на постоянную лицензию.",

            _ => "Введите ключ из письма, которое пришло после оплаты. " +
                 "Первая активация требует интернета, дальше приложение работает офлайн."
        };

        _primaryButton.Content = "Активировать";
        _exitButton.Content = _mode == ActivationMode.ManualEntry ? "Отмена" : "Выйти";

        // Пробный период предлагаем только тому, кто его ещё не брал.
        _trialButton.Visibility = LicenseManager.TrialAvailable
            ? Visibility.Visible
            : Visibility.Collapsed;

        HideStatus();
        _keyBox.Focus();
    }

    private void ShowTrialPane()
    {
        _trialVisible = true;
        _keyPane.Visibility = Visibility.Collapsed;
        _trialPane.Visibility = Visibility.Visible;
        _trialButton.Visibility = Visibility.Collapsed;

        _title.Text = $"{LicenseManager.TrialDays} дня бесплатно";
        _description.Text =
            $"Полная версия на {LicenseManager.TrialDays} дня, без ограничений по возможностям. " +
            "Пробный период выдаётся один раз на компьютер, для его начала нужен интернет.";

        _primaryButton.Content = "Начать";
        _exitButton.Content = "Назад";

        HideStatus();
        _emailBox.Focus();
        _emailBox.CaretIndex = _emailBox.Text.Length;
    }

    private void GoBackOrClose()
    {
        if (_busy)
            return;

        if (_trialVisible)
            ShowKeyPane();
        else
            Close();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        GoBackOrClose();
    }

    /// <summary>Приводит ввод к виду XXXX-XXXX-XXXX-XXXX прямо во время набора.</summary>
    private void OnKeyTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formatting)
            return;

        var formatted = LicenseKeyFormat.Normalize(_keyBox.Text);
        if (formatted == _keyBox.Text)
            return;

        var caretWasAtEnd = _keyBox.CaretIndex >= _keyBox.Text.Length;
        var caret = _keyBox.CaretIndex;

        _formatting = true;
        _keyBox.Text = formatted;
        _keyBox.CaretIndex = caretWasAtEnd ? formatted.Length : Math.Min(caret, formatted.Length);
        _formatting = false;
    }

    // ---------- обращение к серверу ----------

    private async Task SubmitAsync()
    {
        if (_busy)
            return;

        if (_trialVisible)
            await StartTrialAsync();
        else
            await ActivateAsync();
    }

    private async Task ActivateAsync()
    {
        SetBusy(true);
        ShowStatus("Проверяем ключ…", danger: false);

        var result = await LicenseManager.ActivateAsync(_keyBox.Text);

        SetBusy(false);

        if (result.IsOk)
        {
            Activated = true;
            Close();
            return;
        }

        ShowStatus(DescribeKeyError(result), danger: true);
        _keyBox.Focus();
        _keyBox.SelectAll();
    }

    private async Task StartTrialAsync()
    {
        var email = _emailBox.Text.Trim();

        // Отсекаем очевидный мусор, не гоняя запрос на сервер.
        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1 || !email[(at + 1)..].Contains('.'))
        {
            ShowStatus("Проверьте адрес почты — он записан с ошибкой.", danger: true);
            _emailBox.Focus();
            return;
        }

        SetBusy(true);
        ShowStatus("Запрашиваем пробный период…", danger: false);

        var result = await LicenseManager.StartTrialAsync(email);

        SetBusy(false);

        if (result.IsOk)
        {
            Activated = true;
            Close();
            return;
        }

        var message = DescribeTrialError(result);

        // Пробный период уже был — возвращаем к вводу ключа, там кнопка исчезнет.
        if (result.Status == LicenseCallStatus.TrialUsed)
            ShowKeyPane();

        ShowStatus(message, danger: true);
    }

    /// <summary>Объяснение на человеческом языке — что именно пошло не так с ключом.</summary>
    private string DescribeKeyError(LicenseCallResult result) => result.Status switch
    {
        LicenseCallStatus.InvalidKey =>
            string.IsNullOrWhiteSpace(result.Message)
                ? "Такой ключ не найден или он отозван. Проверьте, что ключ введён целиком, как в письме."
                : result.Message,

        LicenseCallStatus.DeviceLimit =>
            $"Этот ключ уже используется на {result.DeviceLimit} компьютерах — это максимум для одной лицензии.\n" +
            "Освободите место: на ненужном компьютере откройте Настройки → «О программе и лицензии» → " +
            "«Отвязать этот компьютер». После этого ключ снова можно активировать здесь.",

        LicenseCallStatus.Offline =>
            "Нет связи с сервером. Ключ проверяется один раз, при активации, — " +
            "подключитесь к интернету и попробуйте снова. Дальше интернет приложению не нужен.",

        _ => string.IsNullOrWhiteSpace(result.Message)
            ? "Сервер лицензий сейчас недоступен. Попробуйте позже."
            : result.Message
    };

    private string DescribeTrialError(LicenseCallResult result) => result.Status switch
    {
        LicenseCallStatus.TrialUsed =>
            string.IsNullOrWhiteSpace(result.Message)
                ? $"Пробный период уже использован — {LicenseManager.TrialDays} дня даются один раз. " +
                  "Чтобы продолжить работу, введите купленный ключ."
                : result.Message,

        LicenseCallStatus.Offline =>
            "Нет связи с сервером. Пробный период начинается только с интернетом — " +
            "дальше приложение будет работать офлайн.",

        LicenseCallStatus.InvalidKey =>
            string.IsNullOrWhiteSpace(result.Message)
                ? "Проверьте адрес почты — он записан с ошибкой."
                : result.Message,

        _ => string.IsNullOrWhiteSpace(result.Message)
            ? "Сервер сейчас недоступен. Попробуйте позже."
            : result.Message
    };

    private void ShowStatus(string text, bool danger)
    {
        _status.Text = text;
        _status.Foreground = (Brush)Application.Current.Resources[danger ? "DangerText" : "TextSecondary"];
        _status.Visibility = Visibility.Visible;
    }

    private void HideStatus()
    {
        _status.Text = string.Empty;
        _status.Visibility = Visibility.Collapsed;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _keyBox.IsEnabled = !busy;
        _emailBox.IsEnabled = !busy;
        _primaryButton.IsEnabled = !busy;
        _trialButton.IsEnabled = !busy;
        _exitButton.IsEnabled = !busy;
        Cursor = busy ? Cursors.Wait : Cursors.Arrow;
    }
}
