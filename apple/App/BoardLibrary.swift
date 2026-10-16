import Foundation
import DoskaPiCore

/// Хранилище досок приложения: один файл `boards.json` в папке документов —
/// тот же формат, что у Windows-версии.
@MainActor
final class BoardLibrary: ObservableObject {
    @Published private(set) var boards: [Board] = []
    @Published var lastError: String?

    private let fileURL: URL = {
        let docs = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0]
        return docs.appendingPathComponent("boards.json")
    }()

    init() {
        load()
    }

    func load() {
        guard let data = try? Data(contentsOf: fileURL) else {
            boards = []
            return
        }
        do {
            boards = try BoardCodec.decodeStore(data).boards
        } catch {
            lastError = "Не удалось прочитать доски: \(error.localizedDescription)"
        }
    }

    /// Импорт `boards.json` из Windows-версии: доски добавляются к своим,
    /// совпадающие по Id заменяются.
    func importStore(from url: URL) {
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }

        do {
            let imported = try BoardCodec.decodeStore(Data(contentsOf: url)).boards
            var byId = Dictionary(uniqueKeysWithValues: boards.map { ($0.id, $0) })
            for board in imported { byId[board.id] = board }
            boards = byId.values.sorted { $0.modified > $1.modified }
            try save()
        } catch {
            lastError = "Не удалось импортировать файл: \(error.localizedDescription)"
        }
    }

    func save() throws {
        let data = try BoardCodec.encodeStore(BoardStoreFile(boards: boards))
        try data.write(to: fileURL, options: .atomic)
    }
}
