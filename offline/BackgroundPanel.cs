using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using SchoolPiBoard.Models;
using SchoolPiBoard.Rendering;

namespace SchoolPiBoard.Views;

/// <summary>
/// Всплывающая панель «Форматирование фона»: цвет доски и стиль сетки,
/// оба показаны образцами — как в референсе.
/// </summary>
public class BackgroundPanel : UserControl
{
    private static readonly string[] Colors =
    {
        "#FFFDF3C6", "#FFFBE0D2", "#FFF9D5DC", "#FFE5D9F2", "#FFD3E9F7",
        "#FFDCEFD3", "#FFFFFFFF", "#FFF1F1F1", "#FFCFCFCF", "#FF17171B",
        "#FF1B1B1F", "#FF0B2545", "#FF14342B", "#FF2A1B3D", "#FF3A2A1B"
    };

    private readonly Board _board;
    private readonly Action _changed;
    private readonly List<Border> _colorTiles = new();
    private readonly List<Border> _gridTiles = new();

    public BackgroundPanel(Board board, Action changed)
    {
        _board = board;
        _changed = changed;

        var root = new StackPanel { Width = 380 };

        root.Children.Add(new TextBlock
        {
            Text = "Форматирование фона",
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });

        root.Children.Add(Caption("Цвет"));
        root.Children.Add(BuildColorGrid());

        root.Children.Add(Caption("Сетка"));
        root.Children.Add(BuildGridChooser());

        Content = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 560,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["GroupCaption"]
    };

    private UIElement BuildColorGrid()
    {
        var grid = new UniformGrid { Columns = 5 };

        foreach (var hex in Colors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;

            var tile = new Border
            {
                Width = 62,
                Height = 54,
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(8),
                Background = (Brush)Application.Current.Resources["AppBg3"],
                BorderThickness = new Thickness(2),
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = hex,
                Child = new Ellipse
                {
                    Width = 34,
                    Height = 34,
                    Fill = new SolidColorBrush(color),
                    Stroke = (Brush)Application.Current.Resources["BorderBrushColor"],
                    StrokeThickness = 1
                }
            };

            tile.MouseLeftButtonUp += (s, _) =>
            {
                if (s is Border { Tag: string picked })
                {
                    _board.BackgroundColor = picked;
                    HighlightColor();
                    _changed();
                }
            };

            _colorTiles.Add(tile);
            grid.Children.Add(tile);
        }

        HighlightColor();
        return grid;
    }

    private void HighlightColor()
    {
        foreach (var tile in _colorTiles)
        {
            var isCurrent = tile.Tag as string == _board.BackgroundColor;
            tile.BorderBrush = isCurrent
                ? (Brush)Application.Current.Resources["Accent"]
                : System.Windows.Media.Brushes.Transparent;
        }
    }

    private UIElement BuildGridChooser()
    {
        var grid = new UniformGrid { Columns = 5 };

        foreach (GridStyle style in Enum.GetValues<GridStyle>())
        {
            var panel = new StackPanel { Margin = new Thickness(3) };

            var preview = new Border
            {
                Width = 62,
                Height = 54,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                Background = new VisualBrush(BuildPreviewVisual(style))
                {
                    Stretch = Stretch.None
                },
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = style
            };

            preview.MouseLeftButtonUp += (s, _) =>
            {
                if (s is Border { Tag: GridStyle picked })
                {
                    _board.Grid = picked;
                    HighlightGrid();
                    _changed();
                }
            };

            _gridTiles.Add(preview);
            panel.Children.Add(preview);

            panel.Children.Add(new TextBlock
            {
                Text = GridPainter.DisplayName(style),
                Foreground = (Brush)Application.Current.Resources["TextSecondary"],
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Width = 62,
                Margin = new Thickness(0, 3, 0, 0)
            });

            grid.Children.Add(panel);
        }

        HighlightGrid();
        return grid;
    }

    /// <summary>Маленький образец сетки для кнопки выбора.</summary>
    private static DrawingVisual BuildPreviewVisual(GridStyle style)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();

        var background = Color.FromRgb(0xF4, 0xF4, 0xF7);
        dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, 62, 54));
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, 62, 54)));

        // Рисуем сетку с мелким шагом, чтобы узор был узнаваем на образце.
        DrawPreviewGrid(dc, style, background, new Rect(0, 0, 62, 54));

        dc.Pop();
        return visual;
    }

    private static void DrawPreviewGrid(DrawingContext dc, GridStyle style, Color background, Rect area)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(150, 60, 60, 70)), 0.8);
        const double step = 9;

        switch (style)
        {
            case GridStyle.Solid:
                break;

            case GridStyle.Dots:
                var brush = new SolidColorBrush(Color.FromArgb(170, 60, 60, 70));
                for (var x = step; x < area.Width; x += step)
                for (var y = step; y < area.Height; y += step)
                    dc.DrawEllipse(brush, null, new Point(x, y), 1, 1);
                break;

            case GridStyle.Square:
                for (var x = step; x < area.Width; x += step)
                    dc.DrawLine(pen, new Point(x, 0), new Point(x, area.Height));
                for (var y = step; y < area.Height; y += step)
                    dc.DrawLine(pen, new Point(0, y), new Point(area.Width, y));
                break;

            case GridStyle.Graph:
                for (var x = step / 2; x < area.Width; x += step / 2)
                    dc.DrawLine(pen, new Point(x, 0), new Point(x, area.Height));
                for (var y = step / 2; y < area.Height; y += step / 2)
                    dc.DrawLine(pen, new Point(0, y), new Point(area.Width, y));
                break;

            case GridStyle.Hybrid:
                for (var x = step; x < area.Width; x += step)
                    dc.DrawLine(pen, new Point(x, 0), new Point(x, area.Height));
                for (var y = step; y < area.Height; y += step)
                    dc.DrawLine(pen, new Point(0, y), new Point(area.Width, y));
                for (var d = -area.Height; d < area.Width; d += step)
                    dc.DrawLine(pen, new Point(d, 0), new Point(d + area.Height, area.Height));
                break;

            case GridStyle.Rhombus:
                for (var d = -area.Height; d < area.Width; d += step)
                {
                    dc.DrawLine(pen, new Point(d, 0), new Point(d + area.Height, area.Height));
                    dc.DrawLine(pen, new Point(d, 0), new Point(d - area.Height, area.Height));
                }
                break;

            case GridStyle.Triangle:
                for (var y = step; y < area.Height; y += step)
                    dc.DrawLine(pen, new Point(0, y), new Point(area.Width, y));
                for (var d = -area.Height; d < area.Width; d += step)
                {
                    dc.DrawLine(pen, new Point(d, 0), new Point(d + area.Height, area.Height));
                    dc.DrawLine(pen, new Point(d, 0), new Point(d - area.Height, area.Height));
                }
                break;

            case GridStyle.WideRuled:
                for (var y = step * 1.6; y < area.Height; y += step * 1.6)
                    dc.DrawLine(pen, new Point(0, y), new Point(area.Width, y));
                break;

            case GridStyle.NarrowRuled:
                for (var y = step * 0.7; y < area.Height; y += step * 0.7)
                    dc.DrawLine(pen, new Point(0, y), new Point(area.Width, y));
                break;
        }
    }

    private void HighlightGrid()
    {
        foreach (var tile in _gridTiles)
        {
            var isCurrent = tile.Tag is GridStyle style && style == _board.Grid;
            tile.BorderBrush = isCurrent
                ? (Brush)Application.Current.Resources["Accent"]
                : System.Windows.Media.Brushes.Transparent;
        }
    }
}
