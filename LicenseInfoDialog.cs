using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Whiteboard.Services;

namespace Whiteboard.Views;

/// <summary>
/// «О программе и лицензии»: состояние ключа, сколько устройств занято
/// и текст лицензионного соглашения. Отсюда же можно освободить слот
/// устройства перед переездом на другой компьютер.
/// </summary>
public class LicenseInfoDialog : Window
{
    private readonly StackPanel _details;
    private readonly Button _deactivateButton;
    private readonly TextBlock _status;

    public LicenseInfoDialog(Window owner)
    {
        Title = "О программе";
        Width = 560;
        MaxHeight = 760;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Owner = owner;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.Resources["AppBg"];
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 20) };

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        root.Children.Add(new TextBlock
        {
            Text = version is null ? "Whiteboard" : $"Whiteboard {version.ToString(3)}",
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 19,
            FontWeight = FontWeights.SemiBold
        });

        root.Children.Add(new TextBlock
        {
            Text = "Лицензия",
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 20, 0, 8)
        });

        _details = new StackPanel();
        root.Children.Add(new Border
        {
            Background = (Brush)Application.Current.Resources["AppBg2"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrushColor"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Child = _details
        });

        _status = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(_status);

        root.Children.Add(new TextBlock
        {
            Text = Eula.Title,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 20, 0, 8)
        });

        root.Children.Add(new ScrollViewer
        {
            MaxHeight = 190,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new TextBlock
            {
                Text = Eula.Text,
                Foreground = (Brush)Application.Current.Resources["TextSecondary"],
                FontSize = 12,
                LineHeight = 18,
                TextWrapping = TextWrapping.Wrap
            }
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };

        _deactivateButton = new Button
        {
            Content = "Отвязать этот компьютер",
            Style = (Style)Application.Current.Resources["TextButton"],
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinWidth = 200
        };
        _deactivateButton.Click += OnDeactivateClick;
        buttons.Children.Add(_deactivateButton);

        var close = new Button
        {
            Content = "Закрыть",
            Style = (Style)Application.Current.Resources["AccentButton"],
            MinWidth = 120,
            Margin = new Thickness(10, 0, 0, 0),
            IsDefault = true,
            IsCancel = true
        };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);

        root.Children.Add(buttons);

        Content = root;
        FillDetails();
    }

    private void FillDetails()
    {
        _details.Children.Clear();

        var state = LicenseManager.State;
        if (state is null)
        {
            _details.Children.Add(new TextBlock
            {
                Text = "Лицензия на этом компьютере не активирована.",
                Foreground = (Brush)Application.Current.Resources["TextPrimary"],
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            });
            _deactivateButton.IsEnabled = false;
            return;
        }

        AddRow("Ключ", state.Key);

        if (!string.IsNullOrWhiteSpace(state.Email))
            AddRow("Почта", state.Email);

        AddRow("Активирована", state.ActivatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
        AddRow("Последняя проверка", state.LastValidatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
        AddRow("Устройств", $"{Math.Max(state.DevicesUsed, 1)} из {state.DeviceLimit}");
        AddRow("Этот компьютер", state.HardwareId.Length > 8
            ? state.HardwareId[..8] + "…"
            : state.HardwareId);

        var daysLeft = LicenseManager.OfflineDaysLeft;
        AddRow("Работа без интернета", daysLeft > 0
            ? $"осталось {daysLeft} дн. до следующей проверки"
            : "требуется проверка при следующем запуске");
    }

    private void AddRow(string label, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var caption = new TextBlock
        {
            Text = label,
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 12
        };
        Grid.SetColumn(caption, 0);
        row.Children.Add(caption);

        var text = new TextBlock
        {
            Text = value,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        _details.Children.Add(row);
    }

    private async void OnDeactivateClick(object sender, RoutedEventArgs e)
    {
        if (LicenseManager.State is null)
            return;

        var confirmed = ConfirmDialog.Show(this,
            "Отвязать этот компьютер",
            "Ключ освободится, и его можно будет активировать на другом компьютере. " +
            "Доски и настройки останутся на месте, но Whiteboard закроется и при " +
            "следующем запуске снова попросит ключ.",
            "Отвязать", danger: true);

        if (!confirmed)
            return;

        _deactivateButton.IsEnabled = false;
        ShowStatus("Освобождаем слот устройства…");

        var result = await LicenseManager.DeactivateAsync();

        if (result.IsOk)
        {
            ConfirmDialog.Info(this,
                "Компьютер отвязан",
                "Ключ снова свободен. Whiteboard сейчас закроется.");
            Application.Current.Shutdown();
            return;
        }

        _deactivateButton.IsEnabled = true;
        ShowStatus(result.Status == LicenseCallStatus.Offline
            ? "Отвязать компьютер можно только с интернетом: сервер должен узнать, что слот освободился."
            : result.Message);
    }

    private void ShowStatus(string text)
    {
        _status.Text = text;
        _status.Visibility = Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(text))
            _status.Visibility = Visibility.Visible;
    }
}
