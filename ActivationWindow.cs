using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Whiteboard.Services;

namespace Whiteboard.Views;

/// <summary>
/// Экран активации. Показывается до списка досок: без действующей лицензии
/// приложение дальше не идёт.
///
/// Один и тот же экран обслуживает два случая — первую активацию и повторную
/// проверку после того, как кончился офлайн-период. Обращение к серверу в обоих
/// случаях одинаковое, отличается только текст.
/// </summary>
public class ActivationWindow : Window
{
    private readonly bool _revalidation;

    private readonly TextBox _keyBox;
    private readonly TextBlock _status;
    private readonly Button _activateButton;
    private readonly Button _exitButton;

    private bool _formatting;
    private bool _busy;

    /// <summary>Пускать ли приложение дальше.</summary>
    public bool Activated { get; private set; }

    public ActivationWindow(bool revalidation)
    {
        _revalidation = revalidation;

        Title = "Whiteboard — активация";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true;
        Background = (Brush)Application.Current.Resources["AppBg"];
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        try
        {
            Icon = new BitmapImage(new Uri("pack://application:,,,/whiteboard.ico"));
        }
        catch
        {
            // Без иконки окно всё равно работает.
        }

        var root = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };

        root.Children.Add(new TextBlock
        {
            Text = _revalidation ? "Нужно проверить лицензию" : "Активация Whiteboard",
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        root.Children.Add(new TextBlock
        {
            Text = _revalidation
                ? $"Приложение больше {LicenseManager.GraceDays} дней работало без связи с сервером. " +
                  "Подключитесь к интернету и нажмите «Проверить» — после этого можно снова работать офлайн."
                : "Введите ключ из письма, которое пришло после оплаты. " +
                  "Первая активация требует интернета, дальше приложение работает офлайн.",
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 13,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 18)
        });

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
        _keyBox.KeyDown += OnKeyBoxKeyDown;
        root.Children.Add(_keyBox);

        root.Children.Add(new TextBlock
        {
            Text = "Формат ключа: XXXX-XXXX-XXXX-XXXX",
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        });

        _status = new TextBlock
        {
            Text = string.Empty,
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
            Content = "Выйти",
            Style = (Style)Application.Current.Resources["TextButton"],
            MinWidth = 104,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsCancel = true
        };
        _exitButton.Click += (_, _) => Close();
        buttons.Children.Add(_exitButton);

        _activateButton = new Button
        {
            Content = _revalidation ? "Проверить" : "Активировать",
            Style = (Style)Application.Current.Resources["AccentButton"],
            MinWidth = 150,
            Margin = new Thickness(10, 0, 0, 0),
            IsDefault = true
        };
        _activateButton.Click += OnActivateClick;
        buttons.Children.Add(_activateButton);

        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) =>
        {
            _keyBox.Focus();
            _keyBox.CaretIndex = _keyBox.Text.Length;
        };
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

    private void OnKeyBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_busy)
        {
            e.Handled = true;
            _ = SubmitAsync();
        }
    }

    private void OnActivateClick(object sender, RoutedEventArgs e) => _ = SubmitAsync();

    private async System.Threading.Tasks.Task SubmitAsync()
    {
        if (_busy)
            return;

        SetBusy(true);
        ShowStatus(_revalidation ? "Проверяем лицензию…" : "Проверяем ключ…", danger: false);

        var result = await LicenseManager.ActivateAsync(_keyBox.Text);

        SetBusy(false);

        if (result.IsOk)
        {
            Activated = true;
            Close();
            return;
        }

        ShowStatus(DescribeError(result), danger: true);
        _keyBox.Focus();
        _keyBox.SelectAll();
    }

    /// <summary>Объяснение на человеческом языке — что именно пошло не так.</summary>
    private string DescribeError(LicenseCallResult result) => result.Status switch
    {
        LicenseCallStatus.InvalidKey =>
            string.IsNullOrWhiteSpace(result.Message)
                ? "Такой ключ не найден или он отозван. Проверьте, что ключ введён целиком, как в письме."
                : result.Message,

        LicenseCallStatus.DeviceLimit =>
            $"Этот ключ уже используется на {result.DeviceLimit} компьютерах — это максимум для одной лицензии.\n" +
            "Освободите место: на ненужном компьютере откройте Настройки → «О программе и лицензии» → " +
            "«Отвязать этот компьютер». После этого ключ снова можно активировать здесь.",

        LicenseCallStatus.Offline => _revalidation
            ? $"Связи с сервером нет, а офлайн-период ({LicenseManager.GraceDays} дней) уже закончился. " +
              "Подключитесь к интернету и нажмите «Проверить» — доски и настройки при этом никуда не денутся."
            : "Нет связи с сервером. Первая активация возможна только с интернетом — " +
              "подключитесь и попробуйте снова.",

        _ => string.IsNullOrWhiteSpace(result.Message)
            ? "Сервер лицензий сейчас недоступен. Попробуйте позже."
            : result.Message
    };

    private void ShowStatus(string text, bool danger)
    {
        _status.Text = text;
        _status.Foreground = (Brush)Application.Current.Resources[danger ? "DangerText" : "TextSecondary"];
        _status.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _keyBox.IsEnabled = !busy;
        _activateButton.IsEnabled = !busy;
        _exitButton.IsEnabled = !busy;
        Cursor = busy ? Cursors.Wait : Cursors.Arrow;
    }
}
