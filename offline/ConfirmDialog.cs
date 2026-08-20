using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SchoolPiBoard.Services;

namespace SchoolPiBoard.Views;

/// <summary>
/// Подтверждение действия в оформлении приложения — системный MessageBox
/// выбивается из общего стиля.
/// </summary>
public static class ConfirmDialog
{
    public static bool Show(Window owner, string title, string message,
                             string confirmText = "Подтвердить", bool danger = false)
    {
        var window = new Window
        {
            Title = title,
            Owner = owner,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current.Resources["AppBg"]
        };
        window.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(window);

        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 20) };

        root.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        root.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            LineHeight = 19
        });

        var result = false;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };

        var cancel = new Button
        {
            Content = "Отмена",
            Style = (Style)Application.Current.Resources["TextButton"],
            MinWidth = 104,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsCancel = true
        };
        cancel.Click += (_, _) => window.Close();
        buttons.Children.Add(cancel);

        var confirm = new Button
        {
            Content = confirmText,
            MinWidth = 130,
            Margin = new Thickness(10, 0, 0, 0),
            IsDefault = true,
            Style = (Style)Application.Current.Resources[
                danger ? "DangerButton" : "AccentButton"]
        };

        confirm.Click += (_, _) =>
        {
            result = true;
            window.Close();
        };
        buttons.Children.Add(confirm);

        root.Children.Add(buttons);
        window.Content = root;
        window.ShowDialog();

        return result;
    }

    /// <summary>
    /// Диалог с тремя вариантами. Возвращает true для первого действия,
    /// false для второго и null, если пользователь отменил.
    /// </summary>
    public static bool? ShowThreeWay(Window owner, string title, string message,
                                      string firstText, string secondText)
    {
        var window = new Window
        {
            Title = title,
            Owner = owner,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current.Resources["AppBg"]
        };
        window.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(window);

        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 20) };

        root.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        root.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            LineHeight = 19
        });

        bool? result = null;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };

        var cancel = new Button
        {
            Content = "Отмена",
            Style = (Style)Application.Current.Resources["TextButton"],
            MinWidth = 96,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsCancel = true
        };
        cancel.Click += (_, _) => window.Close();
        buttons.Children.Add(cancel);

        var second = new Button
        {
            Content = secondText,
            Style = (Style)Application.Current.Resources["TextButton"],
            MinWidth = 150,
            Margin = new Thickness(8, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        second.Click += (_, _) => { result = false; window.Close(); };
        buttons.Children.Add(second);

        var first = new Button
        {
            Content = firstText,
            Style = (Style)Application.Current.Resources["AccentButton"],
            MinWidth = 150,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = true
        };
        first.Click += (_, _) => { result = true; window.Close(); };
        buttons.Children.Add(first);

        root.Children.Add(buttons);
        window.Content = root;
        window.ShowDialog();

        return result;
    }

    /// <summary>Информационное сообщение в том же оформлении.</summary>
    public static void Info(Window owner, string title, string message)
    {
        var window = new Window
        {
            Title = title,
            Owner = owner,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current.Resources["AppBg"]
        };
        window.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(window);

        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 20) };

        root.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        root.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            LineHeight = 19
        });

        var ok = new Button
        {
            Content = "Хорошо",
            Style = (Style)Application.Current.Resources["AccentButton"],
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0),
            IsDefault = true,
            IsCancel = true
        };
        ok.Click += (_, _) => window.Close();
        root.Children.Add(ok);

        window.Content = root;
        window.ShowDialog();
    }
}
