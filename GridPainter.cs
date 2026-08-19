using System.Windows;
using System.Windows.Media;
using Whiteboard.Models;

namespace Whiteboard.Rendering;

/// <summary>Рисует фоновую сетку доски прямо в мировых координатах.</summary>
public static class GridPainter
{
    /// <summary>Шаг сетки в мировых координатах.</summary>
    public const double Cell = 20.0;

    public static string DisplayName(GridStyle style) => style switch
    {
        GridStyle.Solid => "Сплошная",
        GridStyle.Dots => "Точка",
        GridStyle.Square => "Квадрат",
        GridStyle.Graph => "График",
        GridStyle.Hybrid => "Гибридная",
        GridStyle.Rhombus => "Ромб",
        GridStyle.WideRuled => "Широкий фильтр",
        GridStyle.Triangle => "Треугольник",
        GridStyle.NarrowRuled => "Узкий фильтр",
        _ => style.ToString()
    };

    /// <summary>Цвет линий: контрастный к фону, но неброский.</summary>
    public static Color LineColor(Color background)
    {
        var luminance = 0.299 * background.R + 0.587 * background.G + 0.114 * background.B;
        return luminance < 128
            ? Color.FromArgb(70, 255, 255, 255)
            : Color.FromArgb(60, 0, 0, 0);
    }

    /// <summary>
    /// Отрисовка сетки в видимой области мира.
    /// Шаг увеличивается при сильном отдалении, чтобы линии не сливались.
    /// </summary>
    public static void Draw(DrawingContext dc, GridStyle style, Color background, Rect world, double zoom)
    {
        if (style == GridStyle.Solid)
            return;

        var step = Cell;
        while (step * zoom < 7)
            step *= 2;

        var pen = new Pen(new SolidColorBrush(LineColor(background)), 1.0 / zoom);
        pen.Freeze();

        var x0 = Math.Floor(world.Left / step) * step;
        var y0 = Math.Floor(world.Top / step) * step;

        switch (style)
        {
            case GridStyle.Dots:
            {
                var brush = new SolidColorBrush(LineColor(background));
                brush.Freeze();
                var r = 1.4 / zoom;
                for (var x = x0; x <= world.Right; x += step)
                for (var y = y0; y <= world.Bottom; y += step)
                    dc.DrawEllipse(brush, null, new Point(x, y), r, r);
                break;
            }

            case GridStyle.Square:
                VerticalLines(dc, pen, world, x0, step);
                HorizontalLines(dc, pen, world, y0, step);
                break;

            case GridStyle.Graph:
                // Мелкая клетка плюс усиленная линия каждые пять клеток.
                VerticalLines(dc, pen, world, x0, step);
                HorizontalLines(dc, pen, world, y0, step);
                var bold = new Pen(new SolidColorBrush(LineColor(background)), 2.0 / zoom);
                bold.Freeze();
                VerticalLines(dc, bold, world, Math.Floor(world.Left / (step * 5)) * step * 5, step * 5);
                HorizontalLines(dc, bold, world, Math.Floor(world.Top / (step * 5)) * step * 5, step * 5);
                break;

            case GridStyle.Hybrid:
                // Клетка плюс диагонали — «гибридная» разметка.
                VerticalLines(dc, pen, world, x0, step);
                HorizontalLines(dc, pen, world, y0, step);
                Diagonals(dc, pen, world, step, true);
                break;

            case GridStyle.Rhombus:
                Diagonals(dc, pen, world, step, true);
                Diagonals(dc, pen, world, step, false);
                break;

            case GridStyle.Triangle:
                HorizontalLines(dc, pen, world, y0, step);
                Diagonals(dc, pen, world, step, true);
                Diagonals(dc, pen, world, step, false);
                break;

            case GridStyle.WideRuled:
                HorizontalLines(dc, pen, world, Math.Floor(world.Top / (step * 2)) * step * 2, step * 2);
                break;

            case GridStyle.NarrowRuled:
                HorizontalLines(dc, pen, world, y0, step);
                break;
        }
    }

    private static void VerticalLines(DrawingContext dc, Pen pen, Rect world, double start, double step)
    {
        for (var x = start; x <= world.Right; x += step)
            dc.DrawLine(pen, new Point(x, world.Top), new Point(x, world.Bottom));
    }

    private static void HorizontalLines(DrawingContext dc, Pen pen, Rect world, double start, double step)
    {
        for (var y = start; y <= world.Bottom; y += step)
            dc.DrawLine(pen, new Point(world.Left, y), new Point(world.Right, y));
    }

    /// <summary>Диагональная штриховка под 45° в одну из сторон.</summary>
    private static void Diagonals(DrawingContext dc, Pen pen, Rect world, double step, bool down)
    {
        var span = world.Width + world.Height;
        var start = down
            ? world.Left - world.Height
            : world.Left;

        start = Math.Floor(start / step) * step;

        for (var offset = start; offset <= world.Right + world.Height; offset += step)
        {
            var p1 = new Point(offset, world.Top);
            var p2 = down
                ? new Point(offset + world.Height, world.Bottom)
                : new Point(offset - world.Height, world.Bottom);
            dc.DrawLine(pen, p1, p2);
        }
    }
}
