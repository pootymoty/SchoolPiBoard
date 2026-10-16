import XCTest
@testable import DoskaPiCore

/// Проверяет, что доски Windows-версии читаются и пишутся без потерь.
/// Образец ниже повторяет то, что пишет System.Text.Json в offline/BoardStore.cs:
/// имена полей с большой буквы, перечисления строками, даты с 7 знаками долей.
final class BoardCodecTests: XCTestCase {
    private let windowsSample = #"""
    {"Version":2,"Boards":[{
      "Id":"6f1c2a9b0d3e4f5a8b7c6d5e4f3a2b1c","Name":"111026",
      "Created":"2026-10-05T19:35:12.1234567+03:00",
      "Modified":"2026-10-05T21:28:00+03:00",
      "Archived":false,"Compressed":false,"Grid":"Square",
      "BackgroundColor":"#FF1B1B1F","GridColor":"","GridOpacity":0.6,
      "Items":[
        {"Id":"a1","Kind":"Stroke","Shape":"Rectangle","LineStyle":"Solid",
         "X":10,"Y":20,"W":30,"H":40,"Rotation":0,"Z":3,
         "StrokeColor":"#FFFFFFFF","FillColor":"","Thickness":5,"Opacity":1,
         "Marker":false,"StrokeSource":"Pen","IsStraightStroke":false,
         "Points":[],"StrokeSegments":[[10,20,40,60],[15,25,20,30]],"ErasePoints":[],
         "Text":"","FontSize":20,"TextColor":"#FFFFFFFF","ImageBase64":"","AttachedToId":"",
         "GroupId":"g1","InnerGroupIds":["g0"],"FlipX":false,"FlipY":false},
        {"Id":"a2","Kind":"Shape","Shape":"Tetrahedron","X":100,"Y":100,"W":80,"H":90,
         "Rotation":45,"FlipX":true,"Text":"АВС"},
        {"Id":"a3","Kind":"Shape","Shape":"Hexagon","FutureField":123}
      ]}]}
    """#

    func testReadsWindowsBoards() throws {
        let store = try BoardCodec.decodeStore(Data(windowsSample.utf8))
        XCTAssertEqual(store.boards.count, 1)

        let board = store.boards[0]
        XCTAssertEqual(board.name, "111026")
        XCTAssertEqual(board.grid, .square)
        XCTAssertEqual(board.gridOpacity, 0.6, accuracy: 1e-9)
        XCTAssertEqual(board.items.count, 3)

        let stroke = board.items[0]
        XCTAssertEqual(stroke.kind, .stroke)
        XCTAssertEqual(stroke.strokeSegments.count, 2)
        XCTAssertEqual(stroke.groupId, "g1")
        XCTAssertEqual(stroke.innerGroupIds, ["g0"])

        // Отсутствующие поля получают умолчания Windows-версии.
        let shape = board.items[1]
        XCTAssertEqual(shape.shape, .tetrahedron)
        XCTAssertEqual(shape.rotation, 45)
        XCTAssertTrue(shape.flipX)
        XCTAssertEqual(shape.strokeColor, "#FFE53935")
        XCTAssertEqual(shape.thickness, 3)
        XCTAssertEqual(shape.text, "АВС")

        // Незнакомая фигура и лишнее поле не роняют чтение.
        XCTAssertEqual(board.items[2].shape, .rectangle)
    }

    func testDates() throws {
        let date = try XCTUnwrap(BoardCodec.parseDate("2026-10-05T19:35:12.1234567+03:00"))
        let utc = try XCTUnwrap(BoardCodec.parseDate("2026-10-05T16:35:12.1234567Z"))
        XCTAssertEqual(date.timeIntervalSince1970, utc.timeIntervalSince1970, accuracy: 1e-6)
        XCTAssertNotNil(BoardCodec.parseDate("2026-10-05T19:35:12"))

        // Запись и обратное чтение дают тот же момент (с точностью до 100 нс, как в .NET).
        let again = try XCTUnwrap(BoardCodec.parseDate(BoardCodec.formatDate(date)))
        XCTAssertEqual(again.timeIntervalSince1970, date.timeIntervalSince1970, accuracy: 1e-6)
    }

    func testRoundTripKeepsEverything() throws {
        let store = try BoardCodec.decodeStore(Data(windowsSample.utf8))
        let again = try BoardCodec.decodeStore(BoardCodec.encodeStore(store))
        XCTAssertEqual(again.boards[0].items, store.boards[0].items)
        XCTAssertEqual(again.boards[0].name, store.boards[0].name)
    }

    func testPressuresWrittenOnlyWhenPresent() throws {
        var item = BoardItem()
        var json = String(decoding: try BoardCodec.encoder().encode(item), as: UTF8.self)
        XCTAssertFalse(json.contains("PointPressures"))

        item.pointPressures = [[0.2, 0.8]]
        json = String(decoding: try BoardCodec.encoder().encode(item), as: UTF8.self)
        XCTAssertTrue(json.contains("PointPressures"))
    }
}
