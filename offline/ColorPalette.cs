using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace SchoolPiBoard.Views;

/// <summary>
/// Палитра: 9 фиксированных цветов кружками + радужный кружок «свой цвет»,
/// который раскрывает под палитрой выбор из спектра. Выбранный цвет
/// отмечен бежевым кольцом (такого цвета в палитре нет — не сливается)
/// и галочкой в центре. Свой цвет, если он выбран, показывается отдельным
/// кружком после радужного.
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
        "#FF6D6D78"  // серый
    };

    /// <summary>Цвет кольца выбранного кружка — бежевый, его нет среди цветов палитры.</summary>
    private static readonly Color RingColor = Color.FromRgb(0xE6, 0xC7, 0x9C);

    private const double SwatchSize = 28;

    public Color SelectedColor { get; private set; } = Colors.White;
    public bool AllowNone { get; }
    public bool IsNoneSelected { get; private set; }

    public event Action<Color>? ColorPicked;
    public event Action? NonePicked;

    private readonly List<(Grid Cell, Color Color)> _swatches = new();
    private readonly UniformGrid _grid;
    private Grid? _customCell;
    private Color? _customColor;
    private readonly SpectrumPicker _picker;
    private Button? _noneButton;

    // customInitial оставлен только для совместимости со старыми вызовами.
    public ColorPalette(Color initial, bool allowNone = false, string noneCaption = "Без цвета", Color? customInitial = null)
    {
        AllowNone = allowNone;
        SelectedColor = initial;

        var root = new StackPanel();
        _grid = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 2) };

        foreach (var hex in FixedSwatches)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            var cell = CreateSwatch(new SolidColorBrush(color), () => Pick(color));
            _swatches.Add((cell, color));
            _grid.Children.Add(cell);
        }

        var rainbow = CreateSwatch(RainbowBrush(), TogglePicker);
        rainbow.ToolTip = "Свой цвет";
        _grid.Children.Add(rainbow);

        if (!IsFixed(initial))
            SetCustom(initial);

        root.Children.Add(_grid);

        _picker = new SpectrumPicker { Visibility = Visibility.Collapsed, Margin = new Thickness(3, 8, 3, 2) };
        _picker.ColorCommitted += color =>
        {
            SetCustom(color);
            Pick(color);
        };
        root.Children.Add(_picker);

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

    private void Pick(Color color)
    {
        SelectedColor = color;
        IsNoneSelected = false;
        UpdateSelectionVisuals();
        ColorPicked?.Invoke(color);
    }

    private void TogglePicker()
    {
        var show = _picker.Visibility != Visibility.Visible;
        if (show)
            _picker.SetColor(IsNoneSelected ? Colors.Red : SelectedColor);
        _picker.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool IsFixed(Color color) =>
        FixedSwatches.Any(hex => ColorsEqual((Color)ColorConverter.ConvertFromString(hex)!, color));

    /// <summary>Свой цвет — отдельный кружок сразу после радужного.</summary>
    private void SetCustom(Color color)
    {
        if (IsFixed(color))
            return;

        _customColor = color;
        if (_customCell is null)
        {
            _customCell = CreateSwatch(new SolidColorBrush(color), () =>
            {
                if (_customColor is { } c)
                    Pick(c);
            });
            _customCell.ToolTip = "Свой цвет";
            _grid.Children.Add(_customCell);
        }
        else
        {
            ((Ellipse)_customCell.Children[1]).Fill = new SolidColorBrush(color);
        }
    }

    /// <summary>
    /// Кружок цвета: внешнее кольцо выделения, сам цвет с тонкой обводкой
    /// (иначе белый пропал бы на светлом фоне) и галочка поверх.
    /// </summary>
    private static Grid CreateSwatch(Brush fill, Action click)
    {
        var cell = new Grid
        {
            Width = SwatchSize + 8,
            Height = SwatchSize + 8,
            Margin = new Thickness(1),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand
        };

        var ring = new Ellipse
        {
            Width = SwatchSize + 8,
            Height = SwatchSize + 8,
            Stroke = new SolidColorBrush(RingColor),
            StrokeThickness = 2.5,
            Visibility = Visibility.Hidden
        };

        var dot = new Ellipse
        {
            Width = SwatchSize,
            Height = SwatchSize,
            Fill = fill,
            StrokeThickness = 1
        };
        dot.SetResourceReference(Shape.StrokeProperty, "BorderBrushColor");

        var check = new Path
        {
            Data = Geometry.Parse("M 0,5 L 4,9 L 11,1"),
            StrokeThickness = 2.4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Hidden
        };

        cell.Children.Add(ring);
        cell.Children.Add(dot);
        cell.Children.Add(check);
        cell.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return cell;
    }

    private void UpdateSelectionVisuals()
    {
        foreach (var (cell, color) in _swatches)
            Mark(cell, color, !IsNoneSelected && ColorsEqual(color, SelectedColor));

        if (_customCell is not null && _customColor is { } custom)
            Mark(_customCell, custom, !IsNoneSelected && ColorsEqual(custom, SelectedColor));

        if (_noneButton is not null)
        {
            _noneButton.Foreground = IsNoneSelected
                ? new SolidColorBrush(RingColor)
                : (Brush)Application.Current.Resources["TextPrimary"];
            _noneButton.FontWeight = IsNoneSelected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private static void Mark(Grid cell, Color color, bool selected)
    {
        cell.Children[0].Visibility = selected ? Visibility.Visible : Visibility.Hidden;

        var check = (Path)cell.Children[2];
        check.Visibility = selected ? Visibility.Visible : Visibility.Hidden;

        // Галочка контрастная: тёмная на светлых цветах, белая на тёмных.
        var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        check.Stroke = luminance > 0.6 ? new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)) : Brushes.White;
    }

    /// <summary>Радужный круг (оттенки по кругу) — значок «любой цвет».</summary>
    private static Brush RainbowBrush()
    {
        const int size = 56;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var angle = Math.Atan2(y - size / 2.0, x - size / 2.0) * 180 / Math.PI;
            var c = SpectrumPicker.FromHsv((angle + 360) % 360, 0.85, 1);
            var i = (y * size + x) * 4;
            pixels[i] = c.B;
            pixels[i + 1] = c.G;
            pixels[i + 2] = c.R;
            pixels[i + 3] = 255;
        }

        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        var brush = new ImageBrush(bitmap);
        brush.Freeze();
        return brush;
    }

    private static bool ColorsEqual(Color a, Color b) =>
        a.A == b.A && a.R == b.R && a.G == b.G && a.B == b.B;
}

/// <summary>
/// Выбор цвета из спектра: квадрат насыщенности/яркости, полоса оттенка,
/// образец и поля R, G, B. Цвет применяется, когда отпущена мышь или
/// введено значение, а не на каждое движение — чтобы не плодить шаги отмены.
/// </summary>
public class SpectrumPicker : UserControl
{
    private const double AreaWidth = 200;
    private const double AreaHeight = 130;

    public event Action<Color>? ColorCommitted;

    private double _hue;
    private double _saturation = 1;
    private double _value = 1;

    private readonly Border _area;
    private readonly Ellipse _areaMarker;
    private readonly Canvas _hueCanvas;
    private readonly Ellipse _hueMarker;
    private readonly Ellipse _preview;
    private readonly TextBox _r, _g, _b;
    private bool _updatingText;

    public SpectrumPicker()
    {
        var root = new StackPanel { Width = AreaWidth };

        // Квадрат: слева направо — насыщенность, сверху вниз — яркость.
        _area = new Border { CornerRadius = new CornerRadius(6) };
        _areaMarker = new Ellipse
        {
            Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2,
            IsHitTestVisible = false
        };
        var areaCanvas = new Canvas { Background = Brushes.Transparent };
        areaCanvas.Children.Add(_areaMarker);

        var areaGrid = new Grid { Width = AreaWidth, Height = AreaHeight, Cursor = Cursors.Cross, ClipToBounds = true };
        areaGrid.Children.Add(_area);
        areaGrid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0)
        });
        areaGrid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90)
        });
        areaGrid.Children.Add(areaCanvas);
        HookDrag(areaGrid, p =>
        {
            _saturation = Math.Clamp(p.X / AreaWidth, 0, 1);
            _value = 1 - Math.Clamp(p.Y / AreaHeight, 0, 1);
            Refresh();
        });
        root.Children.Add(areaGrid);

        // Образец и полоса оттенка.
        var hueRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        hueRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hueRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _preview = new Ellipse { Width = 26, Height = 26, StrokeThickness = 1, Margin = new Thickness(0, 0, 10, 0) };
        _preview.SetResourceReference(Shape.StrokeProperty, "BorderBrushColor");
        hueRow.Children.Add(_preview);

        var hueBrush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (var i = 0; i <= 6; i++)
            hueBrush.GradientStops.Add(new GradientStop(FromHsv(i * 60 % 360, 1, 1), i / 6.0));

        _hueMarker = new Ellipse { Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
        _hueCanvas = new Canvas { Background = Brushes.Transparent };
        _hueCanvas.Children.Add(_hueMarker);

        var hueGrid = new Grid { Height = 14, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
        hueGrid.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = hueBrush, Margin = new Thickness(0, 2, 0, 2) });
        hueGrid.Children.Add(_hueCanvas);
        HookDrag(hueGrid, p =>
        {
            var width = Math.Max(1, hueGrid.ActualWidth);
            _hue = Math.Clamp(p.X / width, 0, 0.9999) * 360;
            Refresh();
        });
        hueGrid.SizeChanged += (_, _) => Refresh();
        Grid.SetColumn(hueGrid, 1);
        hueRow.Children.Add(hueGrid);
        root.Children.Add(hueRow);

        // Поля R, G, B.
        var rgb = new UniformGrid { Columns = 3, Margin = new Thickness(0, 10, 0, 0) };
        _r = RgbBox(rgb, "R");
        _g = RgbBox(rgb, "G");
        _b = RgbBox(rgb, "B");
        root.Children.Add(rgb);

        Content = root;
        Refresh();
    }

    public void SetColor(Color color)
    {
        (_hue, _saturation, _value) = ToHsv(color);
        Refresh();
    }

    private Color Current => FromHsv(_hue, _saturation, _value);

    private void Refresh()
    {
        _area.Background = new SolidColorBrush(FromHsv(_hue, 1, 1));
        Canvas.SetLeft(_areaMarker, _saturation * AreaWidth - 6);
        Canvas.SetTop(_areaMarker, (1 - _value) * AreaHeight - 6);

        var width = _hueCanvas.ActualWidth > 0 ? _hueCanvas.ActualWidth : AreaWidth - 36;
        Canvas.SetLeft(_hueMarker, _hue / 360 * width - 7);
        _hueMarker.Fill = new SolidColorBrush(FromHsv(_hue, 1, 1));

        var color = Current;
        _preview.Fill = new SolidColorBrush(color);

        _updatingText = true;
        _r.Text = color.R.ToString();
        _g.Text = color.G.ToString();
        _b.Text = color.B.ToString();
        _updatingText = false;
    }

    /// <summary>Перетаскивание мышью: положение меняется живьём, цвет применяется при отпускании.</summary>
    private void HookDrag(FrameworkElement target, Action<Point> move)
    {
        target.MouseLeftButtonDown += (_, e) =>
        {
            target.CaptureMouse();
            move(e.GetPosition(target));
            e.Handled = true;
        };
        target.MouseMove += (_, e) =>
        {
            if (target.IsMouseCaptured)
                move(e.GetPosition(target));
        };
        target.MouseLeftButtonUp += (_, e) =>
        {
            if (!target.IsMouseCaptured)
                return;
            target.ReleaseMouseCapture();
            ColorCommitted?.Invoke(Current);
            e.Handled = true;
        };
    }

    private TextBox RgbBox(Panel host, string caption)
    {
        var panel = new StackPanel { Margin = new Thickness(2, 0, 2, 0) };
        var box = new TextBox
        {
            Height = 30,
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            MaxLength = 3
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ApplyText();
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) => ApplyText();
        panel.Children.Add(box);

        var label = new TextBlock
        {
            Text = caption,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0)
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        panel.Children.Add(label);

        host.Children.Add(panel);
        return box;
    }

    private void ApplyText()
    {
        if (_updatingText)
            return;

        static byte Parse(string s) =>
            int.TryParse(s, out var v) ? (byte)Math.Clamp(v, 0, 255) : (byte)0;

        var color = Color.FromRgb(Parse(_r.Text), Parse(_g.Text), Parse(_b.Text));
        if (color == Current)
            return;

        SetColor(color);
        ColorCommitted?.Invoke(color);
    }

    public static Color FromHsv(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - c;

        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    public static (double Hue, double Saturation, double Value) ToHsv(Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double hue = 0;
        if (delta > 1e-9)
        {
            if (max == r) hue = 60 * ((g - b) / delta % 6);
            else if (max == g) hue = 60 * ((b - r) / delta + 2);
            else hue = 60 * ((r - g) / delta + 4);
        }
        if (hue < 0) hue += 360;

        var saturation = max < 1e-9 ? 0 : delta / max;
        return (hue, saturation, max);
    }
}
