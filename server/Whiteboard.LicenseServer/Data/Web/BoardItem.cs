namespace Whiteboard.LicenseServer.Data;

/// <summary>
/// Объект доски: штрих, фигура, текст или изображение. Хранится отдельной
/// строкой, чтобы правку одного объекта можно было применить, не переписывая
/// доску целиком.
/// </summary>
public class BoardItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BoardId { get; set; }

    /// <summary>stroke | rect | ellipse | line | arrow | text | image …</summary>
    public string Kind { get; set; } = string.Empty;

    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public double Rotation { get; set; }
    public int ZIndex { get; set; }

    public string? StrokeColor { get; set; }
    public string? FillColor { get; set; }
    public double? Thickness { get; set; }
    public double? Opacity { get; set; }

    /// <summary>Точки штриха, массивом JSON — для остальных объектов null.</summary>
    public string? Points { get; set; }

    public string? Text { get; set; }
    public double? FontSize { get; set; }

    /// <summary>Ключ файла в объектном хранилище; сами картинки в базе не лежат.</summary>
    public string? ImageRef { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
