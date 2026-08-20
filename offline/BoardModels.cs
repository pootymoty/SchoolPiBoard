using System.Text.Json.Serialization;
using System.Windows;

namespace SchoolPiBoard.Models;

public enum ItemKind
{
    Stroke,
    Shape,
    Text,
    Image
}

public enum ShapeKind
{
    Line,
    Arrow,
    Rectangle,
    Ellipse,
    Triangle,
    Trapezoid,
    Parallelogram,
    Rhombus
}

/// <summary>Стиль фоновой сетки доски (названия соответствуют панели «Форматирование фона»).</summary>
public enum GridStyle
{
    Solid,        // Сплошная
    Dots,         // Точка
    Square,       // Квадрат
    Graph,        // График
    Hybrid,       // Гибридная
    Rhombus,      // Ромб
    WideRuled,    // Широкий фильтр
    Triangle,     // Треугольник
    NarrowRuled   // Узкий фильтр
}

/// <summary>
/// Единый тип для всех объектов доски. Наследование намеренно не используется —
/// так сериализация в JSON остаётся простой и не требует полиморфных конвертеров.
/// </summary>
public class BoardItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ItemKind Kind { get; set; }
    public ShapeKind Shape { get; set; } = ShapeKind.Rectangle;

    /// <summary>Габариты объекта в мировых координатах (без учёта поворота).</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; } = 100;
    public double H { get; set; } = 100;

    /// <summary>Поворот в градусах вокруг центра габаритов.</summary>
    public double Rotation { get; set; }

    /// <summary>Порядок отрисовки: больше — выше.</summary>
    public int Z { get; set; }

    public string StrokeColor { get; set; } = "#FFE53935";

    /// <summary>Пустая строка — заливки нет.</summary>
    public string FillColor { get; set; } = "";

    public double Thickness { get; set; } = 3;
    public double Opacity { get; set; } = 1.0;

    /// <summary>Маркер рисуется полупрозрачным и с плоским пером.</summary>
    public bool Marker { get; set; }

    /// <summary>Точки рукописного штриха: плоский список x0,y0,x1,y1,…</summary>
    public List<double> Points { get; set; } = new();

    /// <summary>
    /// Области, вырезанные ластиком из геометрической фигуры.
    /// Формат: x0, y0, radius0, x1, y1, radius1, … в локальных координатах объекта.
    /// Это позволяет стирать линии и фигуры частично, не удаляя объект целиком.
    /// </summary>
    public List<double> ErasePoints { get; set; } = new();

    public string Text { get; set; } = "";
    public double FontSize { get; set; } = 20;
    public string TextColor { get; set; } = "#FFFFFFFF";

    public string ImageBase64 { get; set; } = "";

    [JsonIgnore]
    public Rect Bounds => new(X, Y, Math.Max(0.01, W), Math.Max(0.01, H));

    [JsonIgnore]
    public Point Center => new(X + W / 2, Y + H / 2);

    public BoardItem Clone()
    {
        var copy = (BoardItem)MemberwiseClone();
        copy.Points = new List<double>(Points);
        copy.ErasePoints = new List<double>(ErasePoints);
        return copy;
    }

    public IEnumerable<Point> EnumeratePoints()
    {
        for (var i = 0; i + 1 < Points.Count; i += 2)
            yield return new Point(Points[i], Points[i + 1]);
    }

    public void SetPoints(IEnumerable<Point> points)
    {
        Points.Clear();
        foreach (var p in points)
        {
            Points.Add(p.X);
            Points.Add(p.Y);
        }
        RecalculateBoundsFromPoints();
    }

    /// <summary>Пересчитывает габариты штриха по его точкам.</summary>
    public void RecalculateBoundsFromPoints()
    {
        if (Points.Count < 2)
            return;

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        for (var i = 0; i + 1 < Points.Count; i += 2)
        {
            minX = Math.Min(minX, Points[i]);
            maxX = Math.Max(maxX, Points[i]);
            minY = Math.Min(minY, Points[i + 1]);
            maxY = Math.Max(maxY, Points[i + 1]);
        }

        X = minX;
        Y = minY;
        W = Math.Max(0.01, maxX - minX);
        H = Math.Max(0.01, maxY - minY);
    }
}

public class Board
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Новая доска";

    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Modified { get; set; } = DateTime.Now;
    public bool Archived { get; set; }

    public GridStyle Grid { get; set; } = GridStyle.Square;
    public string BackgroundColor { get; set; } = "#FF1B1B1F";

    public List<BoardItem> Items { get; set; } = new();

    [JsonIgnore] public string CreatedText => Created.ToString("dd.MM.yyyy HH:mm");
    [JsonIgnore] public string ModifiedText => Modified.ToString("dd.MM.yyyy HH:mm");
}

public class BoardStoreFile
{
    public int Version { get; set; } = 2;
    public List<Board> Boards { get; set; } = new();
}
