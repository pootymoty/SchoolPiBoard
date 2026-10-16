import Foundation

// Перечисления пишутся в JSON строками — как JsonStringEnumConverter
// в Windows-версии. Незнакомое значение (например, из более новой версии
// программы) не роняет чтение всей доски, а заменяется умолчанием.

public enum ItemKind: String, Codable, CaseIterable, Sendable {
    case stroke = "Stroke"
    case shape = "Shape"
    case text = "Text"
    case image = "Image"

    public init(from decoder: Decoder) throws {
        let raw = try decoder.singleValueContainer().decode(String.self)
        self = ItemKind(rawValue: raw) ?? .stroke
    }
}

public enum LineStyle: String, Codable, CaseIterable, Sendable {
    case solid = "Solid"
    case dash = "Dash"
    case dashDot = "DashDot"
    case dot = "Dot"

    public init(from decoder: Decoder) throws {
        let raw = try decoder.singleValueContainer().decode(String.self)
        self = LineStyle(rawValue: raw) ?? .solid
    }
}

public enum ShapeKind: String, Codable, CaseIterable, Sendable {
    case line = "Line"
    case arrow = "Arrow"
    case rectangle = "Rectangle"
    case ellipse = "Ellipse"
    case triangle = "Triangle"
    case trapezoid = "Trapezoid"
    case parallelogram = "Parallelogram"
    case rhombus = "Rhombus"
    case cube = "Cube"
    case cylinder = "Cylinder"
    case cone = "Cone"
    case sphere = "Sphere"
    case pyramid = "Pyramid"
    case tetrahedron = "Tetrahedron"

    public init(from decoder: Decoder) throws {
        let raw = try decoder.singleValueContainer().decode(String.self)
        self = ShapeKind(rawValue: raw) ?? .rectangle
    }

    /// Объёмные фигуры рисуются каркасом, задние рёбра — пунктиром.
    public var isVolume: Bool {
        switch self {
        case .cube, .cylinder, .cone, .sphere, .pyramid, .tetrahedron: return true
        default: return false
        }
    }

    public var isLine: Bool { self == .line || self == .arrow }
}

public enum GridStyle: String, Codable, CaseIterable, Sendable {
    case solid = "Solid"
    case dots = "Dots"
    case square = "Square"
    case graph = "Graph"
    case hybrid = "Hybrid"
    case rhombus = "Rhombus"
    case wideRuled = "WideRuled"
    case triangle = "Triangle"
    case narrowRuled = "NarrowRuled"

    public init(from decoder: Decoder) throws {
        let raw = try decoder.singleValueContainer().decode(String.self)
        self = GridStyle(rawValue: raw) ?? .square
    }
}
