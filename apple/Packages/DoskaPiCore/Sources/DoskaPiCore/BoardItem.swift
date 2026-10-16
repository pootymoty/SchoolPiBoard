import Foundation

/// Объект доски. Один тип на всё — как `BoardItem` в Windows-версии
/// (offline/BoardModels.cs): имена полей в JSON совпадают один в один,
/// поэтому доска с Windows открывается здесь и обратно.
///
/// Каждое поле читается с умолчанием: старые доски, где части полей ещё
/// не было, и доски из более новых версий читаются без ошибок.
public struct BoardItem: Codable, Equatable, Identifiable, Sendable {
    public var id: String = BoardItem.newId()
    public var kind: ItemKind = .stroke
    public var shape: ShapeKind = .rectangle
    public var lineStyle: LineStyle = .solid

    /// Габариты в мировых координатах (без учёта поворота).
    public var x: Double = 0
    public var y: Double = 0
    public var w: Double = 100
    public var h: Double = 100

    /// Поворот в градусах вокруг центра габаритов, по часовой.
    public var rotation: Double = 0
    public var z: Int = 0

    public var strokeColor: String = "#FFE53935"
    /// Пустая строка — заливки нет.
    public var fillColor: String = ""
    public var thickness: Double = 3
    public var opacity: Double = 1

    public var marker: Bool = false
    public var strokeSource: String = "Pen"
    public var isStraightStroke: Bool = false

    /// Точки штриха: x0,y0,x1,y1,… (старый формат — один сегмент).
    public var points: [Double] = []
    /// Несколько сегментов одного штриха.
    public var strokeSegments: [[Double]] = []
    /// Следы ластика на прямой: x, y, радиус, …
    public var erasePoints: [Double] = []

    /// Нажим пера по точкам (0…1), параллельно `strokeSegments` / `points`.
    /// Есть только у досок, нарисованных на iPad/Mac. Windows-версия этого
    /// поля не знает: она его пропускает и рисует штрих ровной толщиной.
    public var pointPressures: [[Double]] = []

    public var text: String = ""
    public var fontSize: Double = 20
    public var textColor: String = "#FFFFFFFF"

    public var imageBase64: String = ""
    public var attachedToId: String = ""

    public var groupId: String = ""
    public var innerGroupIds: [String] = []
    public var flipX: Bool = false
    public var flipY: Bool = false

    public init() {}

    public static func newId() -> String {
        UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
    }

    enum CodingKeys: String, CodingKey {
        case id = "Id", kind = "Kind", shape = "Shape", lineStyle = "LineStyle"
        case x = "X", y = "Y", w = "W", h = "H"
        case rotation = "Rotation", z = "Z"
        case strokeColor = "StrokeColor", fillColor = "FillColor"
        case thickness = "Thickness", opacity = "Opacity"
        case marker = "Marker", strokeSource = "StrokeSource", isStraightStroke = "IsStraightStroke"
        case points = "Points", strokeSegments = "StrokeSegments", erasePoints = "ErasePoints"
        case pointPressures = "PointPressures"
        case text = "Text", fontSize = "FontSize", textColor = "TextColor"
        case imageBase64 = "ImageBase64", attachedToId = "AttachedToId"
        case groupId = "GroupId", innerGroupIds = "InnerGroupIds"
        case flipX = "FlipX", flipY = "FlipY"
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = BoardItem()
        id = try c.decodeIfPresent(String.self, forKey: .id) ?? d.id
        kind = try c.decodeIfPresent(ItemKind.self, forKey: .kind) ?? d.kind
        shape = try c.decodeIfPresent(ShapeKind.self, forKey: .shape) ?? d.shape
        lineStyle = try c.decodeIfPresent(LineStyle.self, forKey: .lineStyle) ?? d.lineStyle
        x = try c.decodeIfPresent(Double.self, forKey: .x) ?? d.x
        y = try c.decodeIfPresent(Double.self, forKey: .y) ?? d.y
        w = try c.decodeIfPresent(Double.self, forKey: .w) ?? d.w
        h = try c.decodeIfPresent(Double.self, forKey: .h) ?? d.h
        rotation = try c.decodeIfPresent(Double.self, forKey: .rotation) ?? d.rotation
        z = try c.decodeIfPresent(Int.self, forKey: .z) ?? d.z
        strokeColor = try c.decodeIfPresent(String.self, forKey: .strokeColor) ?? d.strokeColor
        fillColor = try c.decodeIfPresent(String.self, forKey: .fillColor) ?? d.fillColor
        thickness = try c.decodeIfPresent(Double.self, forKey: .thickness) ?? d.thickness
        opacity = try c.decodeIfPresent(Double.self, forKey: .opacity) ?? d.opacity
        marker = try c.decodeIfPresent(Bool.self, forKey: .marker) ?? d.marker
        strokeSource = try c.decodeIfPresent(String.self, forKey: .strokeSource) ?? d.strokeSource
        isStraightStroke = try c.decodeIfPresent(Bool.self, forKey: .isStraightStroke) ?? d.isStraightStroke
        points = try c.decodeIfPresent([Double].self, forKey: .points) ?? d.points
        strokeSegments = try c.decodeIfPresent([[Double]].self, forKey: .strokeSegments) ?? d.strokeSegments
        erasePoints = try c.decodeIfPresent([Double].self, forKey: .erasePoints) ?? d.erasePoints
        pointPressures = try c.decodeIfPresent([[Double]].self, forKey: .pointPressures) ?? d.pointPressures
        text = try c.decodeIfPresent(String.self, forKey: .text) ?? d.text
        fontSize = try c.decodeIfPresent(Double.self, forKey: .fontSize) ?? d.fontSize
        textColor = try c.decodeIfPresent(String.self, forKey: .textColor) ?? d.textColor
        imageBase64 = try c.decodeIfPresent(String.self, forKey: .imageBase64) ?? d.imageBase64
        attachedToId = try c.decodeIfPresent(String.self, forKey: .attachedToId) ?? d.attachedToId
        groupId = try c.decodeIfPresent(String.self, forKey: .groupId) ?? d.groupId
        innerGroupIds = try c.decodeIfPresent([String].self, forKey: .innerGroupIds) ?? d.innerGroupIds
        flipX = try c.decodeIfPresent(Bool.self, forKey: .flipX) ?? d.flipX
        flipY = try c.decodeIfPresent(Bool.self, forKey: .flipY) ?? d.flipY
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(id, forKey: .id)
        try c.encode(kind, forKey: .kind)
        try c.encode(shape, forKey: .shape)
        try c.encode(lineStyle, forKey: .lineStyle)
        try c.encode(x, forKey: .x)
        try c.encode(y, forKey: .y)
        try c.encode(w, forKey: .w)
        try c.encode(h, forKey: .h)
        try c.encode(rotation, forKey: .rotation)
        try c.encode(z, forKey: .z)
        try c.encode(strokeColor, forKey: .strokeColor)
        try c.encode(fillColor, forKey: .fillColor)
        try c.encode(thickness, forKey: .thickness)
        try c.encode(opacity, forKey: .opacity)
        try c.encode(marker, forKey: .marker)
        try c.encode(strokeSource, forKey: .strokeSource)
        try c.encode(isStraightStroke, forKey: .isStraightStroke)
        try c.encode(points, forKey: .points)
        try c.encode(strokeSegments, forKey: .strokeSegments)
        try c.encode(erasePoints, forKey: .erasePoints)
        // Поле только для Apple-версий: пишем, лишь когда нажим есть.
        if !pointPressures.isEmpty {
            try c.encode(pointPressures, forKey: .pointPressures)
        }
        try c.encode(text, forKey: .text)
        try c.encode(fontSize, forKey: .fontSize)
        try c.encode(textColor, forKey: .textColor)
        try c.encode(imageBase64, forKey: .imageBase64)
        try c.encode(attachedToId, forKey: .attachedToId)
        try c.encode(groupId, forKey: .groupId)
        try c.encode(innerGroupIds, forKey: .innerGroupIds)
        try c.encode(flipX, forKey: .flipX)
        try c.encode(flipY, forKey: .flipY)
    }
}
