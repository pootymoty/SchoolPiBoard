import SwiftUI

/// «Доска Пи» для iPad и Mac (Mac — через Mac Catalyst, тот же код).
/// Этап 0: каркас. Список досок, импорт файла досок с Windows,
/// чтобы проверить на устройстве, что формат читается.
@main
struct DoskaPiApp: App {
    @StateObject private var library = BoardLibrary()

    var body: some Scene {
        WindowGroup {
            BoardListView()
                .environmentObject(library)
        }
    }
}
