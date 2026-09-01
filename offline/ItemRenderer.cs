using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SchoolPiBoard.Models;

namespace SchoolPiBoard.Rendering;

/// <summary>
/// Построение геометрии, отрисовка и проверка попадания для объектов доски.
/// Вся отрисовка идёт напрямую в DrawingContext — это даёт полный контроль
/// над видом выделения и позволяет показывать изменения объекта вживую.
/// </summary>
public static class ItemRenderer
{
    private static readonly Dictionary<string, BitmapSource> ImageCache = new();

    public static Brush ParseBrush(string value, Brush fallback)
    {
        var color = ParseColor(value);
        return color is null ? fallback : SolidBrush(color.Value);
    }

    public static Color? ParseColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        try
        {
            return (Color)ColorConverter.ConvertFromString(value)!;
        }
        catch
        {
            return null;
        }
    }

    public static BitmapSource? GetImage(BoardItem item)
    {
        if (string.IsNullOrEmpty(item.ImageBase64))
            return null;

        if (ImageCache.TryGetValue(item.Id, out var cached))
            return cached;

        try
        {
            var bytes = Convert.FromBase64String(item.ImageBase64);
            var bitmap = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();

            ImageCache[item.Id] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static void DropImageCache(string id) => ImageCache.Remove(id);

    // =====================================================================
    //  Геометрия фигур
    // =====================================================================
    public static Geometry BuildShapeGeometry(ShapeKind kind, Rect r)
    {
        var w = Math.Max(0.01, r.Width);
        var h = Math.Max(0.01, r.Height);
        var x = r.X;
        var y = r.Y;

        switch (kind)
        {
            case ShapeKind.Rectangle:
                return new RectangleGeometry(new Rect(x, y, w, h));

            case ShapeKind.Ellipse:
                return new EllipseGeometry(new Rect(x, y, w, h));

            case ShapeKind.Line:
                return new LineGeometry(new Point(x, y), new Point(x + w, y + h));

            case ShapeKind.Arrow:
                return BuildArrow(new Point(x, y), new Point(x + w, y + h));

            case ShapeKind.Triangle:
                return Polygon(new Point(x + w / 2, y), new Point(x, y + h), new Point(x + w, y + h));

            case ShapeKind.Trapezoid:
                return Polygon(new Point(x + w * 0.22, y), new Point(x + w * 0.78, y),
                                new Point(x + w, y + h), new Point(x, y + h));

            case ShapeKind.Parallelogram:
                return Polygon(new Point(x + w * 0.25, y), new Point(x + w, y),
                                new Point(x + w * 0.75, y + h), new Point(x, y + h));

            case ShapeKind.Rhombus:
                return Polygon(new Point(x + w / 2, y), new Point(x + w, y + h / 2),
                                new Point(x + w / 2, y + h), new Point(x, y + h / 2));

            default:
                return new RectangleGeometry(new Rect(x, y, w, h));
        }
    }

    private static Geometry Polygon(params Point[] points)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
        for (var i = 1; i < points.Length; i++)
            figure.Segments.Add(new LineSegment(points[i], true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Geometry BuildArrow(Point start, Point end)
    {
        var group = new GeometryGroup();
        group.Children.Add(new LineGeometry(start, end));

        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        var length = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
        var head = Math.Max(10.0, Math.Min(30.0, length * 0.2));
        const double spread = 26 * Math.PI / 180;

        group.Children.Add(new LineGeometry(end, new Point(
            end.X - head * Math.Cos(angle - spread),
            end.Y - head * Math.Sin(angle - spread))));
        group.Children.Add(new LineGeometry(end, new Point(
            end.X - head * Math.Cos(angle + spread),
            end.Y - head * Math.Sin(angle + spread))));

        return group;
    }

    /// <summary>Строит сглаженную кривую по точкам рукописного штриха.</summary>
    // =====================================================================
    //  Кэш кистей и перьев
    // =====================================================================
    // Каждый кадр раньше создавал по кисти и перу на каждый объект. На доске
    // с тысячами штрихов это тысячи объектов на кадр, и все — незамороженные,
    // то есть WPF на каждый вешал отслеживание изменений. Цветов и толщин
    // в доске всего десятки, поэтому кэш маленький и попадает почти всегда.
    private static readonly Dictionary<uint, SolidColorBrush> BrushCache = new();
    private static readonly Dictionary<(uint Color, double Thickness, bool Flat, LineStyle Dash), Pen> PenCache = new();

    public static SolidColorBrush SolidBrush(Color color)
    {
        var key = (uint)((color.A << 24) | (color.R << 16) | (color.G << 8) | color.B);

        if (BrushCache.TryGetValue(key, out var cached))
            return cached;

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        BrushCache[key] = brush;
        return brush;
    }

    /// <param name="flat">Плоское перо маркера вместо круглого.</param>
    public static Pen StrokePen(Color color, double thickness, bool flat, LineStyle dash)
    {
        var key = ((uint)((color.A << 24) | (color.R << 16) | (color.G << 8) | color.B),
                   thickness, flat, dash);

        if (PenCache.TryGetValue(key, out var cached))
            return cached;

        var pen = new Pen(SolidBrush(color), Math.Max(0.1, thickness))
        {
            StartLineCap = flat ? PenLineCap.Square : PenLineCap.Round,
            EndLineCap = flat ? PenLineCap.Square : PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
            DashStyle = GetDashStyle(dash)
        };
        pen.Freeze();

        PenCache[key] = pen;
        return pen;
    }

    public static Geometry BuildStrokeGeometry(IList<Point> points)
    {
        if (points.Count == 0)
            return Geometry.Empty;

        if (points.Count == 1)
            return new EllipseGeometry(points[0], 0.4, 0.4);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], false, false);

            if (points.Count == 2)
            {
                ctx.LineTo(points[1], true, false);
            }
            else
            {
                // Квадратичное сглаживание: узлы — середины отрезков,
                // сами точки становятся контрольными. Даёт мягкую линию
                // без «углов» на каждом замере мыши.
                for (var i = 1; i < points.Count - 1; i++)
                {
                    var mid = new Point((points[i].X + points[i + 1].X) / 2,
                                        (points[i].Y + points[i + 1].Y) / 2);
                    ctx.QuadraticBezierTo(points[i], mid, true, false);
                }
                ctx.LineTo(points[^1], true, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    // =====================================================================
    //  Отрисовка
    // =====================================================================
    public static void Draw(DrawingContext dc, BoardItem item, double pixelsPerDip)
    {
        var rotated = Math.Abs(item.Rotation) > 0.01;
        if (rotated)
            dc.PushTransform(new RotateTransform(item.Rotation, item.Center.X, item.Center.Y));

        if (item.Opacity < 0.999)
            dc.PushOpacity(item.Opacity);

        switch (item.Kind)
        {
            case ItemKind.Stroke:
                DrawStroke(dc, item);
                break;
            case ItemKind.Shape:
                DrawShape(dc, item, pixelsPerDip);
                break;
            case ItemKind.Text:
                DrawText(dc, item, pixelsPerDip);
                break;
            case ItemKind.Image:
                DrawImage(dc, item);
                break;
        }

        if (item.Opacity < 0.999)
            dc.Pop();

        if (rotated)
            dc.Pop();
    }

    private static readonly DashStyle DashPattern = Frozen(new DashStyle(new[] { 4.0, 3.0 }, 0));
    private static readonly DashStyle DashDotPattern = Frozen(new DashStyle(new[] { 4.0, 2.0, 1.0, 2.0 }, 0));
    private static readonly DashStyle DotPattern = Frozen(new DashStyle(new[] { 1.0, 2.5 }, 0));

    private static DashStyle Frozen(DashStyle style)
    {
        style.Freeze();
        return style;
    }

    private static DashStyle GetDashStyle(LineStyle style)
    {
        return style switch
        {
            LineStyle.Dash => DashPattern,
            LineStyle.DashDot => DashDotPattern,
            LineStyle.Dot => DotPattern,
            _ => DashStyles.Solid
        };
    }

    private static void DrawStroke(DrawingContext dc, BoardItem item)
    {
        var color = ParseColor(item.StrokeColor) ?? Colors.Red;
        var pen = StrokePen(color, item.Thickness, item.Marker,
                            item.IsStraightStroke ? item.LineStyle : LineStyle.Solid);

        // Составной штрих остаётся одним BoardItem. Каждый внутренний сегмент
        // рисуется отдельно, поэтому отрыв пера между буквами не превращается
        // в соединительную линию.
        if (item.StrokeSegments.Count > 0)
        {
            foreach (var segment in item.EnumerateStrokeSegments())
            {
                if (segment.Count >= 2)
                    dc.DrawGeometry(null, pen, BuildStrokeGeometry(segment));
            }
            return;
        }

        var points = item.EnumeratePoints().ToList();
        if (points.Count == 0)
            return;

        // Выпрямленный пером/маркером штрих остаётся Stroke, но визуально
        // рисуется именно как прямая. Это позволяет одновременно сохранить
        // частичное стирание и применять тип линии.
        if (item.IsStraightStroke && points.Count >= 2)
        {
            dc.DrawLine(pen, points[0], points[^1]);
            return;
        }

        dc.DrawGeometry(null, pen, BuildStrokeGeometry(points));
    }

    private static void DrawShape(DrawingContext dc, BoardItem item, double pixelsPerDip)
    {
        Geometry geometry;

        // У линии и стрелки направление задают сохранённые концы.
        // Раньше они строились по диагонали габаритов и всегда смотрели
        // из левого верхнего угла в правый нижний.
        if (item.Shape is ShapeKind.Line or ShapeKind.Arrow && item.Points.Count >= 4)
        {
            var a = new Point(item.Points[0], item.Points[1]);
            var b = new Point(item.Points[2], item.Points[3]);
            geometry = item.Shape == ShapeKind.Arrow
                ? BuildArrow(a, b)
                : new LineGeometry(a, b);
        }
        else
        {
            geometry = BuildShapeGeometry(item.Shape, item.Bounds);
        }

        var fill = string.IsNullOrEmpty(item.FillColor)
            ? null
            : ParseBrush(item.FillColor, Brushes.Transparent);

        Pen? pen = null;
        if (item.Thickness > 0.01)
        {
            var color = ParseColor(item.StrokeColor) ?? Colors.Red;
            pen = StrokePen(color, item.Thickness, flat: false, item.LineStyle);
        }

        // Для прямой/стрелки используем разбиение центрального отрезка на
        // неповреждённые фрагменты. Это надёжнее, чем клип-маска для LineGeometry:
        // каждый оставшийся участок действительно остаётся отдельным штрихом.
        if (item.Shape == ShapeKind.Line && item.Points.Count >= 4 && item.ErasePoints.Count >= 3)
        {
            if (pen is not null)
                DrawLineShapeWithErase(dc, item, pen);
            return;
        }

        // Фигуры и стрелки теперь не имеют частичного стирания: при касании
        // ластиком они удаляются целиком. Это существенно дешевле клип-масок.
        dc.DrawGeometry(fill, pen, geometry);

        if (!string.IsNullOrEmpty(item.Text))
            DrawTextInside(dc, item, pixelsPerDip);
    }


    private static void DrawLineShapeWithErase(DrawingContext dc, BoardItem item, Pen pen)
    {
        var a = new Point(item.Points[0], item.Points[1]);
        var b = new Point(item.Points[2], item.Points[3]);
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len2 = dx * dx + dy * dy;
        if (len2 < 1e-9)
            return;

        var len = Math.Sqrt(len2);
        var eraseRadiusExtra = 0.0;
        var intervals = new List<(double Start, double End)>();

        for (var i = 0; i + 2 < item.ErasePoints.Count; i += 3)
        {
            var center = new Point(item.ErasePoints[i], item.ErasePoints[i + 1]);
            var radius = Math.Max(0.1, item.ErasePoints[i + 2]) + eraseRadiusExtra;

            var t = ((center.X - a.X) * dx + (center.Y - a.Y) * dy) / len2;
            var tc = Math.Clamp(t, 0, 1);
            var px = a.X + tc * dx;
            var py = a.Y + tc * dy;
            var dist = Math.Sqrt((center.X - px) * (center.X - px) +
                                  (center.Y - py) * (center.Y - py));
            if (dist > radius)
                continue;

            var dt = Math.Sqrt(Math.Max(0, radius * radius - dist * dist)) / len;
            intervals.Add((Math.Max(0, t - dt), Math.Min(1, t + dt)));
        }

        intervals.Sort((x, y) => x.Start.CompareTo(y.Start));
        var merged = new List<(double Start, double End)>();
        foreach (var interval in intervals)
        {
            if (merged.Count == 0 || interval.Start > merged[^1].End + 1e-9)
                merged.Add(interval);
            else
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, interval.End));
        }

        var cursor = 0.0;
        foreach (var erased in merged)
        {
            DrawLineFragment(dc, pen, a, b, cursor, erased.Start);
            cursor = Math.Max(cursor, erased.End);
        }
        DrawLineFragment(dc, pen, a, b, cursor, 1.0);

    }

    private static void DrawArrowHead(DrawingContext dc, Pen pen, Point start, Point end)
    {
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        var length = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
        var head = Math.Max(10.0, Math.Min(30.0, length * 0.2));
        const double spread = 26 * Math.PI / 180;

        dc.DrawLine(pen, end, new Point(
            end.X - head * Math.Cos(angle - spread),
            end.Y - head * Math.Sin(angle - spread)));
        dc.DrawLine(pen, end, new Point(
            end.X - head * Math.Cos(angle + spread),
            end.Y - head * Math.Sin(angle + spread)));
    }

    private static void DrawLineFragment(DrawingContext dc, Pen pen, Point a, Point b, double start, double end)
    {
        if (end - start < 1e-6)
            return;

        var p1 = new Point(a.X + (b.X - a.X) * start, a.Y + (b.Y - a.Y) * start);
        var p2 = new Point(a.X + (b.X - a.X) * end, a.Y + (b.Y - a.Y) * end);
        dc.DrawLine(pen, p1, p2);
    }

    private static void DrawTextInside(DrawingContext dc, BoardItem item, double pixelsPerDip)
    {
        var padding = Math.Min(item.W, item.H) * 0.12;
        var maxWidth = Math.Max(8, item.W - padding * 2);

        var formatted = MakeText(item.Text, item.FontSize, item.TextColor, pixelsPerDip, maxWidth);

        var origin = new Point(
            item.X + (item.W - formatted.Width) / 2,
            item.Y + (item.H - formatted.Height) / 2);

        dc.PushClip(new RectangleGeometry(item.Bounds));
        dc.DrawText(formatted, origin);
        dc.Pop();
    }

    private static void DrawText(DrawingContext dc, BoardItem item, double pixelsPerDip)
    {
        var formatted = MakeText(item.Text, item.FontSize, item.StrokeColor, pixelsPerDip,
                                  Math.Max(10, item.W));
        dc.DrawText(formatted, new Point(item.X, item.Y));
    }

    private static FormattedText MakeText(string text, double size, string color,
                                           double pixelsPerDip, double maxWidth)
    {
        var brush = ParseBrush(color, Brushes.White);
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
                          FontWeights.Normal, FontStretches.Normal),
            Math.Max(1, size),
            brush,
            pixelsPerDip)
        {
            MaxTextWidth = Math.Max(10, maxWidth),
            TextAlignment = TextAlignment.Center
        };
        return formatted;
    }

    /// <summary>Измеряет текст для автоподбора размеров текстового объекта.</summary>
    public static Size MeasureText(string text, double size, double pixelsPerDip, double maxWidth)
    {
        var formatted = MakeText(text, size, "#FFFFFFFF", pixelsPerDip, maxWidth);
        return new Size(Math.Max(20, formatted.Width), Math.Max(size, formatted.Height));
    }

    private static void DrawImage(DrawingContext dc, BoardItem item)
    {
        var bitmap = GetImage(item);
        if (bitmap is not null)
            dc.DrawImage(bitmap, item.Bounds);
    }

    // =====================================================================
    //  Проверка попадания
    // =====================================================================
    /// <summary>Переводит мировую точку в систему координат объекта без поворота.</summary>
    public static Point ToLocal(BoardItem item, Point world)
    {
        if (Math.Abs(item.Rotation) < 0.01)
            return world;

        var center = item.Center;
        var angle = -item.Rotation * Math.PI / 180;
        var dx = world.X - center.X;
        var dy = world.Y - center.Y;

        return new Point(
            dx * Math.Cos(angle) - dy * Math.Sin(angle) + center.X,
            dx * Math.Sin(angle) + dy * Math.Cos(angle) + center.Y);
    }

    public static bool HitTest(BoardItem item, Point world, double tolerance)
    {
        var local = ToLocal(item, world);

        switch (item.Kind)
        {
            case ItemKind.Stroke:
            {
                var limit = tolerance + item.Thickness / 2;
                foreach (var segment in item.EnumerateStrokeSegments())
                {
                    if (segment.Count == 1 && Distance(segment[0], local) <= limit)
                        return true;

                    for (var i = 0; i + 1 < segment.Count; i++)
                    {
                        if (DistanceToSegment(local, segment[i], segment[i + 1]) <= limit)
                            return true;
                    }
                }
                return false;
            }

            case ItemKind.Image:
            case ItemKind.Text:
                return item.Bounds.Contains(local);

            case ItemKind.Shape:
            {
                // Линия и стрелка: проверяем близость к самому отрезку,
                // а не к прямоугольнику габаритов.
                if (item.Shape is ShapeKind.Line or ShapeKind.Arrow && item.Points.Count >= 4)
                {
                    var a = new Point(item.Points[0], item.Points[1]);
                    var b = new Point(item.Points[2], item.Points[3]);
                    return DistanceToSegment(local, a, b) <= tolerance + Math.Max(1, item.Thickness) / 2;
                }

                var geometry = BuildShapeGeometry(item.Shape, item.Bounds);

                if (!string.IsNullOrEmpty(item.FillColor) && geometry.FillContains(local))
                    return true;

                // По тексту внутри фигуры тоже нужно попадать.
                if (!string.IsNullOrEmpty(item.Text) && item.Bounds.Contains(local))
                    return true;

                var pen = new Pen(Brushes.Black, Math.Max(1, item.Thickness) + tolerance * 2);
                return geometry.StrokeContains(pen, local);
            }

            default:
                return false;
        }
    }

    /// <summary>Габариты с учётом поворота — нужны для вписывания содержимого в экран.</summary>
    public static Rect RotatedBounds(BoardItem item)
    {
        if (Math.Abs(item.Rotation) < 0.01)
            return item.Bounds;

        var center = item.Center;
        var angle = item.Rotation * Math.PI / 180;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);

        var corners = new[]
        {
            new Point(item.X, item.Y),
            new Point(item.X + item.W, item.Y),
            new Point(item.X + item.W, item.Y + item.H),
            new Point(item.X, item.Y + item.H)
        };

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var c in corners)
        {
            var dx = c.X - center.X;
            var dy = c.Y - center.Y;
            var px = dx * cos - dy * sin + center.X;
            var py = dx * sin + dy * cos + center.Y;

            minX = Math.Min(minX, px);
            maxX = Math.Max(maxX, px);
            minY = Math.Min(minY, py);
            maxY = Math.Max(maxY, py);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    public static double Distance(Point a, Point b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    public static double DistanceToSegment(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;

        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
            return Distance(p, a);

        var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
        t = Math.Clamp(t, 0, 1);

        return Distance(p, new Point(a.X + t * dx, a.Y + t * dy));
    }
}
