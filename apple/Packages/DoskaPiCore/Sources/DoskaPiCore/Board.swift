import Foundation

/// Доска — как `Board` в Windows-версии.
public struct Board: Codable, Equatable, Identifiable, Sendable {
    public var id: String = BoardItem.newId()
    public var name: String = "Новая доска"
    public var created: Date = Date()
    public var modified: Date = Date()
    public var archived: Bool = false
    /// Содержимое лежит в сжатом архиве рядом с файлом досок (Windows-архив).
    public var compressed: Bool = false
    public var grid: GridStyle = .square
    public var backgroundColor: String = "#FF1B1B1F"
    public var gridColor: String = ""
    public var gridOpacity: Double = 1
    public var items: [BoardItem] = []

    public init() {}

    enum CodingKeys: String, CodingKey {
        case id = "Id", name = "Name", created = "Created", modified = "Modified"
        case archived = "Archived", compressed = "Compressed", grid = "Grid"
        case backgroundColor = "BackgroundColor", gridColor = "GridColor"
        case gridOpacity = "GridOpacity", items = "Items"
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = Board()
        id = try c.decodeIfPresent(String.self, forKey: .id) ?? d.id
        name = try c.decodeIfPresent(String.self, forKey: .name) ?? d.name
        created = try c.decodeIfPresent(Date.self, forKey: .created) ?? d.created
        modified = try c.decodeIfPresent(Date.self, forKey: .modified) ?? d.modified
        archived = try c.decodeIfPresent(Bool.self, forKey: .archived) ?? d.archived
        compressed = try c.decodeIfPresent(Bool.self, forKey: .compressed) ?? d.compressed
        grid = try c.decodeIfPresent(GridStyle.self, forKey: .grid) ?? d.grid
        backgroundColor = try c.decodeIfPresent(String.self, forKey: .backgroundColor) ?? d.backgroundColor
        gridColor = try c.decodeIfPresent(String.self, forKey: .gridColor) ?? d.gridColor
        gridOpacity = try c.decodeIfPresent(Double.self, forKey: .gridOpacity) ?? d.gridOpacity
        items = try c.decodeIfPresent([BoardItem].self, forKey: .items) ?? d.items
    }
}

/// Корень файла досок (`boards.json`) — как `BoardStoreFile`.
public struct BoardStoreFile: Codable, Equatable, Sendable {
    public var version: Int = 2
    public var boards: [Board] = []

    public init(boards: [Board] = []) {
        self.boards = boards
    }

    enum CodingKeys: String, CodingKey {
        case version = "Version", boards = "Boards"
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        version = try c.decodeIfPresent(Int.self, forKey: .version) ?? 2
        boards = try c.decodeIfPresent([Board].self, forKey: .boards) ?? []
    }
}
