using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SchoolPiBoard.Services;

namespace SchoolPiBoard.Views;

/// <summary>Модальный ввод строки — в WPF нет штатного InputBox.</summary>
public static class PromptDialog
{
    public static string? Show(Window owner, string title, string prompt, string initialValue = "")
    {
        var window = new Window
        {
            Title = title,
            Owner = owner,
            Width = 430,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)Application.Current.Resources["AppBg"],
            ShowInTaskbar = false
        };
        window.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(window);

        var root = new StackPanel { Margin = new Thickness(22) };

        root.Children.Add(new TextBlock
        {
            Text = prompt,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var input = new TextBox
        {
            Text = initialValue,
            FontSize = 14,
            Padding = new Thickness(8, 6, 8, 6),
            Background = (Brush)Application.Current.Resources["AppBg2"],
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            BorderBrush = (Brush)Application.Current.Resources["Accent"],
            BorderThickness = new Thickness(1),
            CaretBrush = (Brush)Application.Current.Resources["TextPrimary"]
        };
        root.Children.Add(input);

        string? result = null;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };

        var cancel = new Button
        {
            Content = "Отмена",
            Style = (Style)Application.Current.Resources["TextButton"],
            MinWidth = 92,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        cancel.Click += (_, _) => window.Close();

        var ok = new Button
        {
            Content = "OK",
            Style = (Style)Application.Current.Resources["AccentButton"],
            MinWidth = 92,
            Margin = new Thickness(10, 0, 0, 0),
            IsDefault = true
        };
        ok.Click += (_, _) =>
        {
            result = input.Text;
            window.Close();
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        window.Content = root;
        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };

        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                result = input.Text;
                window.Close();
            }
            else if (e.Key == Key.Escape)
            {
                window.Close();
            }
        };

        window.ShowDialog();
        return result;
    }
}
