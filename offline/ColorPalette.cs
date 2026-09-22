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

    private readonly List<(Button Button, Color Color)> _swatches = new();
    private Button? _noneButton;

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
            var swatch = CreateSwatch(color, hex);
            _swatches.Add((swatch, color));
            grid.Children.Add(swatch);
        }

        root.Children.Add(grid);

        if (allowNone)
        {
            _noneButton = new Button
            {
                Content = noneCaption,
                Style = (Style)Application.Current.Resources["TextButton"],
                Margin = new Thickness(3, 6, 3, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            _noneButton.Click += (_, _) =>
            {
                IsNoneSelected = true;
                UpdateSelectionVisuals();
                NonePicked?.Invoke();
            };
            root.Children.Add(_noneButton);
        }

        Content = root;
        UpdateSelectionVisuals();
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
            Tag = color,
            ToolTip = tooltip
        };
        button.Click += (_, _) =>
        {
            if (button.Tag is Color c)
            {
                SelectedColor = c;
                IsNoneSelected = false;
                UpdateSelectionVisuals();
                ColorPicked?.Invoke(c);
            }
        };
        return button;
    }

    /// <summary>
    /// Обычная тонкая рамка одного цвета не видна на образце того же (или
    /// близкого) цвета — чёрный на чёрном, серый на сером. Поэтому у
    /// выбранного цвета рамка акцентная и заметно толще, а не просто
    /// другого оттенка.
    /// </summary>
    private void UpdateSelectionVisuals()
    {
        var accent = (Brush)Application.Current.Resources["Accent"];
        var normal = (Brush)Application.Current.Resources["BorderBrushColor"];

        foreach (var (button, color) in _swatches)
        {
            var selected = !IsNoneSelected && ColorsEqual(color, SelectedColor);
            button.BorderBrush = selected ? accent : normal;
            button.BorderThickness = new Thickness(selected ? 3 : 1);
        }

        if (_noneButton is not null)
        {
            _noneButton.Foreground = IsNoneSelected
                ? accent
                : (Brush)Application.Current.Resources["TextPrimary"];
            _noneButton.FontWeight = IsNoneSelected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private static bool ColorsEqual(Color a, Color b) =>
        a.A == b.A && a.R == b.R && a.G == b.G && a.B == b.B;
}
