using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SchoolPiBoard.Views;

/// <summary>
/// Обычное окно выбора цвета с цветовым кругом и квадратом насыщенности/яркости.
/// Не зависит от темы приложения, чтобы выглядеть как самостоятельный системный
/// инструмент выбора цвета.
/// </summary>
public sealed class ColorPickerWindow : Window
{
    private readonly Image _hueImage;
    private readonly Image _svImage;
    private readonly Canvas _hueCanvas;
    private readonly Canvas _svCanvas;
    private readonly TextBox _hex;
    private readonly Border _preview;

    private double _hue;
    private double _saturation;
    private double _value;
    private bool _updating;

    public Color SelectedColor { get; private set; }

    public ColorPickerWindow(Color initial)
    {
        SelectedColor = initial;
        (_hue, _saturation, _value) = ToHsv(initial);

        Title = "Выбор цвета";
        Width = 390;
        Height = 470;
        MinWidth = 390;
        MinHeight = 470;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = SystemColors.WindowBrush;

        var root = new StackPanel { Margin = new Thickness(18) };

        root.Children.Add(new TextBlock
        {
            Text = "Выберите цвет",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var pickerArea = new Grid { Height = 255 };
        pickerArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        pickerArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _svCanvas = new Canvas { Width = 220, Height = 220 };
        _svImage = new Image { Width = 220, Height = 220, Stretch = Stretch.None };
        _svCanvas.Children.Add(_svImage);
        _svCanvas.MouseLeftButtonDown += SvMouse;
        _svCanvas.MouseMove += SvMouseMove;
        Grid.SetColumn(_svCanvas, 0);
        pickerArea.Children.Add(_svCanvas);

        _hueCanvas = new Canvas { Width = 220, Height = 220, Margin = new Thickness(0, 0, 0, 0) };
        _hueImage = new Image { Width = 220, Height = 220, Stretch = Stretch.None };
        _hueCanvas.Children.Add(_hueImage);
        _hueCanvas.MouseLeftButtonDown += HueMouse;
        _hueCanvas.MouseMove += HueMouseMove;
        Grid.SetColumn(_hueCanvas, 0);
        pickerArea.Children.Add(_hueCanvas);

        // Цветовой круг располагается поверх SV-квадрата только как переключатель
        // оттенка; SV-квадрат используется после выбора оттенка.
        // Для одновременного отображения делаем круг в правой части.
        Grid.SetColumn(_hueCanvas, 1);
        _hueCanvas.Margin = new Thickness(5, 0, 0, 0);
        pickerArea.Children.Remove(_hueCanvas);
        pickerArea.Children.Add(_hueCanvas);
        root.Children.Add(pickerArea);

        var hint = new TextBlock
        {
            Text = "Оттенок — круг, насыщенность и яркость — квадрат",
            FontSize = 11,
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(0, 4, 0, 10)
        };
        root.Children.Add(hint);

        var bottom = new Grid();
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });

        _preview = new Border
        {
            Width = 42,
            Height = 30,
            BorderBrush = SystemColors.ControlDarkBrush,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(initial),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        bottom.Children.Add(_preview);

        _hex = new TextBox
        {
            Text = initial.ToString(),
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 8, 0)
        };
        _hex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && TrySetHex())
                e.Handled = true;
        };
        Grid.SetColumn(_hex, 1);
        bottom.Children.Add(_hex);

        var ok = new Button
        {
            Content = "Выбрать",
            Height = 30,
            IsDefault = true,
            Padding = new Thickness(10, 0, 10, 0)
        };
        ok.Click += (_, _) =>
        {
            TrySetHex();
            DialogResult = true;
        };
        Grid.SetColumn(ok, 2);
        bottom.Children.Add(ok);
        root.Children.Add(bottom);

        var cancel = new Button
        {
            Content = "Отмена",
            Width = 85,
            Height = 30,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsCancel = true
        };
        root.Children.Add(cancel);

        Content = root;
        RenderPickers();
    }

    private void HueMouse(object sender, MouseButtonEventArgs e)
    {
        _hueCanvas.CaptureMouse();
        SetHueFromPoint(e.GetPosition(_hueCanvas));
    }

    private void HueMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            SetHueFromPoint(e.GetPosition(_hueCanvas));
    }

    private void SetHueFromPoint(Point p)
    {
        const double cx = 110, cy = 110;
        var dx = p.X - cx;
        var dy = p.Y - cy;
        var r = Math.Sqrt(dx * dx + dy * dy);
        if (r < 72 || r > 110)
            return;
        _hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        RenderPickers();
    }

    private void SvMouse(object sender, MouseButtonEventArgs e)
    {
        _svCanvas.CaptureMouse();
        SetSvFromPoint(e.GetPosition(_svCanvas));
    }

    private void SvMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            SetSvFromPoint(e.GetPosition(_svCanvas));
    }

    private void SetSvFromPoint(Point p)
    {
        _saturation = Math.Clamp(p.X / 220.0, 0, 1);
        _value = Math.Clamp(1.0 - p.Y / 220.0, 0, 1);
        RenderPickers();
    }

    private void RenderPickers()
    {
        if (_updating) return;
        _updating = true;

        _hueImage.Source = BuildHueWheel(220, 220);
        _svImage.Source = BuildSvSquare(220, 220, _hue);

        // Маркер выбранного оттенка на круге.
        const double cx = 110, cy = 110, r = 91;
        var rad = _hue * Math.PI / 180.0;
        SetMarker(_hueCanvas, "HueMarker", cx + Math.Cos(rad) * r - 5, cy + Math.Sin(rad) * r - 5, 10);

        // Маркер выбранного цвета в SV-квадрате.
        SetMarker(_svCanvas, "SvMarker", _saturation * 220 - 5, (1 - _value) * 220 - 5, 10);

        SelectedColor = FromHsv(_hue, _saturation, _value);
        _preview.Background = new SolidColorBrush(SelectedColor);
        _hex.Text = SelectedColor.ToString();
        _updating = false;
    }

    private static void SetMarker(Canvas canvas, string name, double left, double top, double size)
    {
        foreach (var old in canvas.Children.OfType<System.Windows.Shapes.Ellipse>().Where(e => Equals(e.Tag, name)).ToList())
            canvas.Children.Remove(old);
        var marker = new System.Windows.Shapes.Ellipse
        {
            Width = size,
            Height = size,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            Tag = name
        };
        canvas.Children.Add(marker);
        Canvas.SetLeft(marker, left);
        Canvas.SetTop(marker, top);
    }

    private bool TrySetHex()
    {
        var text = _hex.Text.Trim();
        try
        {
            if (!text.StartsWith("#", StringComparison.Ordinal))
                text = "#" + text;
            var color = (Color)ColorConverter.ConvertFromString(text)!;
            (_hue, _saturation, _value) = ToHsv(color);
            SelectedColor = color;
            _preview.Background = new SolidColorBrush(color);
            RenderPickers();
            return true;
        }
        catch
        {
            _hex.Text = SelectedColor.ToString();
            return false;
        }
    }

    private static BitmapSource BuildHueWheel(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        const double cx = 110, cy = 110, inner = 72, outer = 110;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var dx = x - cx;
            var dy = y - cy;
            var r = Math.Sqrt(dx * dx + dy * dy);
            if (r < inner || r > outer) continue;
            var hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
            var c = FromHsv(hue, 1, 1);
            var i = (y * width + x) * 4;
            pixels[i] = c.B;
            pixels[i + 1] = c.G;
            pixels[i + 2] = c.R;
            pixels[i + 3] = 255;
        }
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    }

    private static BitmapSource BuildSvSquare(int width, int height, double hue)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var s = x / (double)(width - 1);
            var v = 1 - y / (double)(height - 1);
            var c = FromHsv(hue, s, v);
            var i = (y * width + x) * 4;
            pixels[i] = c.B;
            pixels[i + 1] = c.G;
            pixels[i + 2] = c.R;
            pixels[i + 3] = 255;
        }
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    }

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
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
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
            if (Math.Abs(max - r) < 1e-6) hue = 60 * (((g - b) / delta) % 6);
            else if (Math.Abs(max - g) < 1e-6) hue = 60 * ((b - r) / delta + 2);
            else hue = 60 * ((r - g) / delta + 4);
        }
        if (hue < 0) hue += 360;
        var saturation = max < 1e-6 ? 0 : delta / max;
        return (hue, saturation, max);
    }
}
