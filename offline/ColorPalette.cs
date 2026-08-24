using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SchoolPiBoard.Models;

namespace SchoolPiBoard.Views;

/// <summary>
/// Компактная палитра: 9 фиксированных цветов + десятый пользовательский цвет.
/// Пользовательский цвет настраивается отдельным обычным окном выбора цвета.
/// </summary>
public class ColorPalette : UserControl
{
    public static readonly string[] FixedSwatches =
    {
        "#FF000000", // чёрный
        "#FFFFFFFF", // белый
        "#FF1E88E5", // синий
        "#FFE53935", // красный
        "#FF43A047", // зелёный
        "#FFFB8C00", // оранжевый
        "#FFFDD835", // жёлтый
        "#FF7E57C2", // фиолетовый
        "#FF6D6D78", // серый
        "#FF00ACC1"  // бирюзовый
    };

    public Color SelectedColor { get; private set; } = Colors.White;
    public bool AllowNone { get; }
    public bool IsNoneSelected { get; private set; }

    public event Action<Color>? ColorPicked;
    public event Action? NonePicked;

    // customInitial оставлен только для совместимости со старыми вызовами.
    public ColorPalette(Color initial, bool allowNone = false, string noneCaption = "Без цвета", Color? customInitial = null)
    {
        AllowNone = allowNone;
        SelectedColor = initial;

        var root = new StackPanel();
        var grid = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 2) };

        foreach (var hex in FixedSwatches)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            grid.Children.Add(CreateSwatch(color, hex));
        }

        root.Children.Add(grid);

        if (allowNone)
        {
            var noneButton = new Button
            {
                Content = noneCaption,
                Style = (Style)Application.Current.Resources["TextButton"],
                Margin = new Thickness(3, 6, 3, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            noneButton.Click += (_, _) =>
            {
                IsNoneSelected = true;
                NonePicked?.Invoke();
            };
            root.Children.Add(noneButton);
        }

        Content = root;
        Apply(initial, false);
    }

    private Button CreateSwatch(Color color, string tooltip)
    {
        var button = new Button
        {
            Width = 30,
            Height = 26,
            Margin = new Thickness(3),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = new SolidColorBrush(color),
            BorderBrush = (Brush)Application.Current.Resources["BorderBrushColor"],
            BorderThickness = new Thickness(1),
            Tag = color,
            ToolTip = tooltip
        };
        button.Click += (_, _) =>
        {
            if (button.Tag is Color c)
            {
                SelectedColor = c;
                IsNoneSelected = false;
                ColorPicked?.Invoke(c);
            }
        };
        return button;
    }

    private void Apply(Color color, bool notify)
    {
        SelectedColor = color;
        IsNoneSelected = false;
        if (notify)
            ColorPicked?.Invoke(color);
    }
}
