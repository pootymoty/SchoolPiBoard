using System.Windows;
using SchoolPiBoard.Models;
using SchoolPiBoard.Rendering;

namespace SchoolPiBoard.Services;

public enum KnobKind { Number, Toggle }

/// <summary>Одна настраиваемая величина заготовки: ползунок числа или переключатель.</summary>
public sealed record TemplateKnob(
    string Key, string Label, KnobKind Kind, double Min, double Max, string? Suffix = null);

/// <summary>
/// Куда и чем вставлять заготовку. Считает холст в момент вставки — только
/// он знает, куда сейчас смотрит пользователь, в каком масштабе, и какими
/// цветом, толщиной и кеглем сейчас рисуют инструменты.
/// </summary>
public sealed record TemplateFrame(double Cx, double Cy, double Size, string Color, double Width, double FontSize);

/// <summary>
/// Заготовка — не отдельный тип объекта, а рецепт, который один раз выдаёт
/// набор обычных объектов доски. После вставки они ничем не отличаются от
/// нарисованного вручную: двигаются, стираются, поворачиваются и
/// редактируются любым обычным инструментом.
/// </summary>
public sealed class BoardTemplate
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Group { get; init; }
    public required string Hint { get; init; }
    public required TemplateKnob[] Knobs { get; init; }
    public required Dictionary<string, double> Defaults { get; init; }
    public required Func<TemplateFrame, Dictionary<string, double>, List<BoardItem>> Build { get; init; }
}

/// <summary>
/// Каталог заготовок. Пока только группа «Координаты» — числовая прямая
/// и координатная плоскость: обе строятся из линий, стрелок и надписей,
/// новых видов фигур не требуют.
///
/// Группа «Объёмные фигуры» (куб, шар, конус и т. п.) сюда намеренно
/// не перенесена: ей нужен новый вид фигуры — половина эллипса — которого
/// в ShapeKind ещё нет, и это отдельная задача.
/// </summary>
public static class BoardTemplateLibrary
{
    public const string GroupCoordinates = "coordinates";

    public static readonly BoardTemplate NumberLine = new()
    {
        Id = "number-line",
        Title = "Числовая прямая",
        Group = GroupCoordinates,
        Hint = "Горизонтальная ось с делениями и подписями от −n до n.",
        Knobs = new[]
        {
            new TemplateKnob("divisions", "Делений в каждую сторону", KnobKind.Number, 3, 12),
            new TemplateKnob("labels", "Подписывать числа", KnobKind.Toggle, 0, 1)
        },
        Defaults = new Dictionary<string, double> { ["divisions"] = 6, ["labels"] = 1 },
        Build = BuildNumberLine
    };

    public static readonly BoardTemplate Plane = new()
    {
        Id = "plane",
        Title = "Координатная плоскость",
        Group = GroupCoordinates,
        Hint = "Две перпендикулярные оси с делениями, подписями и стрелками.",
        Knobs = new[]
        {
            new TemplateKnob("divisions", "Делений по каждой оси", KnobKind.Number, 3, 10),
            new TemplateKnob("labels", "Подписывать числа", KnobKind.Toggle, 0, 1)
        },
        Defaults = new Dictionary<string, double> { ["divisions"] = 5, ["labels"] = 1 },
        Build = BuildPlane
    };

    public static readonly BoardTemplate[] All = { NumberLine, Plane };

    /// <summary>Числовая прямая. Перенесена дословно из онлайн-версии.</summary>
    private static List<BoardItem> BuildNumberLine(TemplateFrame f, Dictionary<string, double> p)
    {
        var n = (int)Math.Round(p["divisions"]);

        // Шаг деления держим кратным клетке — иначе подписи разойдутся
        // с фоновой сеткой, если её потом включат.
        var raw = f.Size / (2 * n + 1.6);
        var unit = Math.Max(GridPainter.Cell, Math.Round(raw / GridPainter.Cell) * GridPainter.Cell);

        var ax = Math.Round(f.Cx / GridPainter.Cell) * GridPainter.Cell;
        var ay = Math.Round(f.Cy / GridPainter.Cell) * GridPainter.Cell;
        var reach = n * unit + unit * 0.7;
        var tick = Math.Max(5, unit * 0.2);
        var small = Math.Max(11, f.FontSize * 0.8);
        var labels = p["labels"] > 0;

        var items = new List<BoardItem>
        {
            Arrow(f, ax - reach, ay, ax + reach, ay),
            Line(f, ax, ay - tick, ax, ay + tick),
            Caption(f, ax, ay + tick + small * 0.3, "0", small)
        };

        for (var i = 1; i <= n; i++)
            foreach (var sign in new[] { 1, -1 })
            {
                var x = ax + sign * i * unit;
                items.Add(Line(f, x, ay - tick, x, ay + tick));
                if (labels)
                    items.Add(Caption(f, x, ay + tick + small * 0.3, (sign * i).ToString(), small));
            }

        return items;
    }

    /// <summary>
    /// Координатная плоскость: та же логика, что у числовой прямой,
    /// применённая к двум перпендикулярным осям сразу.
    /// </summary>
    private static List<BoardItem> BuildPlane(TemplateFrame f, Dictionary<string, double> p)
    {
        var n = (int)Math.Round(p["divisions"]);

        var raw = f.Size / (2 * n + 1.6);
        var unit = Math.Max(GridPainter.Cell, Math.Round(raw / GridPainter.Cell) * GridPainter.Cell);

        var cx = Math.Round(f.Cx / GridPainter.Cell) * GridPainter.Cell;
        var cy = Math.Round(f.Cy / GridPainter.Cell) * GridPainter.Cell;
        var reach = n * unit + unit * 0.7;
        var tick = Math.Max(5, unit * 0.2);
        var small = Math.Max(11, f.FontSize * 0.8);
        var labels = p["labels"] > 0;

        var items = new List<BoardItem>
        {
            Arrow(f, cx - reach, cy, cx + reach, cy),
            Arrow(f, cx, cy + reach, cx, cy - reach),
            Caption(f, cx + reach + small * 0.9, cy - small * 0.6, "x", small),
            Caption(f, cx + small * 0.9, cy - reach - small * 1.4, "y", small)
        };

        for (var i = 1; i <= n; i++)
            foreach (var sign in new[] { 1, -1 })
            {
                var x = cx + sign * i * unit;
                items.Add(Line(f, x, cy - tick, x, cy + tick));
                if (labels)
                    items.Add(Caption(f, x, cy + tick + small * 0.3, (sign * i).ToString(), small));

                // Экранные Y растут вниз — положительные деления оси Y идут вверх.
                var y = cy - sign * i * unit;
                items.Add(Line(f, cx - tick, y, cx + tick, y));
                if (labels)
                    items.Add(Caption(f, cx + tick + small * 0.3, y - small * 0.65, (sign * i).ToString(), small));
            }

        return items;
    }

    // =====================================================================
    //  Тонкие обёртки — создают один BoardItem нужного вида, окрашенный
    //  и заданной толщины из текущей рамки вставки.
    // =====================================================================

    private static BoardItem Line(TemplateFrame f, double x1, double y1, double x2, double y2) =>
        LineLike(f, ShapeKind.Line, x1, y1, x2, y2);

    private static BoardItem Arrow(TemplateFrame f, double x1, double y1, double x2, double y2) =>
        LineLike(f, ShapeKind.Arrow, x1, y1, x2, y2);

    private static BoardItem LineLike(TemplateFrame f, ShapeKind kind, double x1, double y1, double x2, double y2)
    {
        var item = new BoardItem
        {
            Kind = ItemKind.Shape,
            Shape = kind,
            StrokeColor = f.Color,
            FillColor = "",
            Thickness = f.Width,
            LineStyle = LineStyle.Solid,
            Opacity = 1.0
        };
        item.SetPoints(new[] { new Point(x1, y1), new Point(x2, y2) });
        return item;
    }

    /// <summary>Надпись, отцентрованная по горизонтали в точке (cx, topY).</summary>
    private static BoardItem Caption(TemplateFrame f, double cx, double topY, string text, double fontSize)
    {
        // pixelsPerDip не меняет ширину букв (только пиксельный хинтинг),
        // поэтому здесь безопасно взять 1.0, не таская сюда FrameworkElement.
        var size = ItemRenderer.MeasureText(text, fontSize, 1.0, 400);

        return new BoardItem
        {
            Kind = ItemKind.Text,
            X = cx - size.Width / 2,
            Y = topY,
            W = size.Width,
            H = size.Height,
            Text = text,
            FontSize = fontSize,
            StrokeColor = f.Color
        };
    }
}

/// <summary>
/// Знаки и формулы — не заготовки, а просто вставка готового текста
/// надписью на холст. К геометрии отношения не имеют.
/// </summary>
public static class QuickInsertLibrary
{
    // Черновой набор — предложен по смыслу задачи, не перенесён из
    // онлайн-версии дословно (там список не документирован). Стоит свериться
    // и дополнить или сократить по вкусу.
    public static readonly string[] Signs =
    {
        "∑", "∏", "√", "∞", "≈", "≠", "≤", "≥", "±", "÷", "×",
        "π", "α", "β", "γ", "θ", "Δ", "°", "∠", "⊥", "∥"
    };

    public static readonly string[] Formulas =
    {
        "a² + b² = c²",
        "S = π·r²",
        "V = (4/3)·π·r³",
        "S = ½·a·h",
        "sin(α+β) = sinα·cosβ + cosα·sinβ",
        "cos(α+β) = cosα·cosβ − sinα·sinβ",
        "V = a·b·c",
        "S = a·b·sin(γ)"
    };
}
