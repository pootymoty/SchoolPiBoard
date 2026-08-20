using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SchoolPiBoard.Views;

/// <summary>
/// Палитра цветов: готовые образцы плюс ползунки оттенка и яркости.
/// HEX-код показывается справочно, вводить его вручную не требуется.
/// </summary>
public class ColorPalette : UserControl
{
    public static readonly string[] Swatches =
    {
        "#FF000000", "#FF434343", "#FF7A7A85", "#FFB7B7C0", "#FFFFFFFF",
        "#FFC62828", "#FFE53935", "#FFFF6D01", "#FFFB8C00", "#FFFDD835",
        "#FF7CB342", "#FF43A047", "#FF00ACC1", "#FF29B6F6", "#FF1E88E5",
        "#FF3949AB", "#FF673AB7", "#FF8E24AA", "#FFD81B60", "#FFF06292",
        "#FF6D4C41", "#FF455A64", "#FF00838F", "#FF4DD0E1", "#FFA5D6A7"
    };

    private readonly Border _preview;
    private readonly Slider _hue;
    private readonly Slider _brightness;
    private readonly TextBlock _hexLabel;
    private bool _updating;

    public Color SelectedColor { get; private set; } = Colors.White;

    /// <summary>Разрешает вариант «без цвета» (прозрачная заливка или отсутствие границы).</summary>
    public bool AllowNone { get; }

    public bool IsNoneSelected { get; private set; }

    public event Action<Color>? ColorPicked;
    public event Action? NonePicked;

    public ColorPalette(Color initial, bool allowNone = false, string noneCaption = "Без цвета")
    {
        AllowNone = allowNone;
        SelectedColor = initial;

        var root = new StackPanel();

        var grid = new UniformGrid { Columns = 5 };
        foreach (var hex in Swatches)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
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
                ToolTip = hex
            };
            button.Click += (s, _) =>
            {
                if (s is Button { Tag: Color c })
                    Apply(c, notify: true);
            };
            grid.Children.Add(button);
        }
        root.Children.Add(grid);

        // --- тонкая настройка ---
        _hue = MakeSlider(0, 360);
        _brightness = MakeSlider(0, 100);

        root.Children.Add(LabeledRow("Оттенок", _hue));
        root.Children.Add(LabeledRow("Яркость", _brightness));

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0)
        };

        _preview = new Border
        {
            Width = 34,
            Height = 26,
            CornerRadius = new CornerRadius(6),
            BorderBrush = (Brush)Application.Current.Resources["BorderBrushColor"],
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(initial)
        };
        bottom.Children.Add(_preview);

        _hexLabel = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Text = initial.ToString()
        };
        bottom.Children.Add(_hexLabel);

        if (allowNone)
        {
            var noneButton = new Button
            {
                Content = noneCaption,
                Style = (Style)Application.Current.Resources["TextButton"],
                Margin = new Thickness(12, 0, 0, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            noneButton.Click += (_, _) =>
            {
                IsNoneSelected = true;
                NonePicked?.Invoke();
            };
            bottom.Children.Add(noneButton);
        }

        root.Children.Add(bottom);
        Content = root;

        Apply(initial, notify: false);

        _hue.ValueChanged += (_, _) => OnSlidersChanged();
        _brightness.ValueChanged += (_, _) => OnSlidersChanged();
    }

    private static Slider MakeSlider(double min, double max) => new()
    {
        Minimum = min,
        Maximum = max,
        Width = 128,
        VerticalAlignment = VerticalAlignment.Center
    };

    private FrameworkElement LabeledRow(string caption, Slider slider)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };
        row.Children.Add(new TextBlock
        {
            Text = caption,
            Width = 60,
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(slider);
        return row;
    }

    private void OnSlidersChanged()
    {
        if (_updating)
            return;

        var color = FromHsv(_hue.Value, 0.85, _brightness.Value / 100.0);
        Apply(color, notify: true, fromSliders: true);
    }

    private void Apply(Color color, bool notify, bool fromSliders = false)
    {
        SelectedColor = color;
        IsNoneSelected = false;

        _preview.Background = new SolidColorBrush(color);
        _hexLabel.Text = color.ToString();

        if (!fromSliders)
        {
            _updating = true;
            var (h, _, v) = ToHsv(color);
            _hue.Value = h;
            _brightness.Value = v * 100;
            _updating = false;
        }

        if (notify)
            ColorPicked?.Invoke(color);
    }

    // ---- преобразования HSV ----
    private static Color FromHsv(double hue, double saturation, double value)
    {
        hue = (hue % 360 + 360) % 360;
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - c;

        double r = 0, g = 0, b = 0;
        switch ((int)(hue / 60))
        {
            case 0: r = c; g = x; break;
            case 1: r = x; g = c; break;
            case 2: g = c; b = x; break;
            case 3: g = x; b = c; break;
            case 4: r = x; b = c; break;
            default: r = c; b = x; break;
        }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static (double H, double S, double V) ToHsv(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double hue = 0;
        if (delta > 1e-6)
        {
            if (Math.Abs(max - r) < 1e-6)
                hue = 60 * (((g - b) / delta) % 6);
            else if (Math.Abs(max - g) < 1e-6)
                hue = 60 * ((b - r) / delta + 2);
            else
                hue = 60 * ((r - g) / delta + 4);
        }

        if (hue < 0)
            hue += 360;

        var saturation = max < 1e-6 ? 0 : delta / max;
        return (hue, saturation, max);
    }
}
