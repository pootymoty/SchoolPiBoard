// swift-tools-version:5.9
// Общее ядро «Доски Пи» для iPad и Mac: модель доски и её чтение/запись
// в том же JSON, что и у Windows-версии (offline/BoardModels.cs).
import PackageDescription

let package = Package(
    name: "DoskaPiCore",
    platforms: [.iOS(.v17), .macOS(.v14), .macCatalyst(.v17)],
    products: [
        .library(name: "DoskaPiCore", targets: ["DoskaPiCore"])
    ],
    targets: [
        .target(name: "DoskaPiCore"),
        .testTarget(name: "DoskaPiCoreTests", dependencies: ["DoskaPiCore"])
    ]
)
