import SwiftUI
import UniformTypeIdentifiers
import DoskaPiCore

struct BoardListView: View {
    @EnvironmentObject private var library: BoardLibrary
    @State private var importing = false

    var body: some View {
        NavigationStack {
            Group {
                if library.boards.isEmpty {
                    ContentUnavailableView(
                        "Досок пока нет",
                        systemImage: "rectangle.on.rectangle",
                        description: Text("Импортируйте boards.json из Windows-версии, чтобы проверить чтение досок."))
                } else {
                    List(library.boards) { board in
                        VStack(alignment: .leading, spacing: 4) {
                            Text(board.name).font(.headline)
                            Text("Объектов: \(board.items.count) · изменена \(board.modified.formatted(date: .numeric, time: .shortened))")
                                .font(.subheadline)
                                .foregroundStyle(.secondary)
                        }
                    }
                }
            }
            .navigationTitle("Мои доски")
            .toolbar {
                Button {
                    importing = true
                } label: {
                    Label("Импорт", systemImage: "square.and.arrow.down")
                }
            }
            .fileImporter(isPresented: $importing, allowedContentTypes: [.json]) { result in
                if case .success(let url) = result {
                    library.importStore(from: url)
                }
            }
            .alert("Ошибка", isPresented: Binding(
                get: { library.lastError != nil },
                set: { if !$0 { library.lastError = nil } })) {
                Button("OK", role: .cancel) {}
            } message: {
                Text(library.lastError ?? "")
            }
        }
    }
}
