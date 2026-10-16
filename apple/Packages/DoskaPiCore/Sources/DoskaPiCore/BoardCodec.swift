import Foundation

/// Чтение и запись досок в формате Windows-версии.
///
/// Даты там пишет System.Text.Json: местное время с 7 знаками долей
/// секунды и смещением — `2026-10-05T19:35:12.1234567+03:00`. Бывает и без
/// долей, и без смещения (тогда это местное время), и с `Z`.
public enum BoardCodec {
    public static func decodeStore(_ data: Data) throws -> BoardStoreFile {
        try decoder().decode(BoardStoreFile.self, from: data)
    }

    public static func encodeStore(_ store: BoardStoreFile) throws -> Data {
        try encoder().encode(store)
    }

    public static func decodeBoard(_ data: Data) throws -> Board {
        try decoder().decode(Board.self, from: data)
    }

    public static func encodeBoard(_ board: Board) throws -> Data {
        try encoder().encode(board)
    }

    public static func decoder() -> JSONDecoder {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { decoder in
            let text = try decoder.singleValueContainer().decode(String.self)
            guard let date = parseDate(text) else {
                throw DecodingError.dataCorrupted(.init(
                    codingPath: decoder.codingPath,
                    debugDescription: "Непонятная дата: \(text)"))
            }
            return date
        }
        return decoder
    }

    public static func encoder() -> JSONEncoder {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .custom { date, encoder in
            var c = encoder.singleValueContainer()
            try c.encode(formatDate(date))
        }
        return encoder
    }

    // MARK: - Даты

    /// Разбирает ISO 8601 с любой точностью долей секунды.
    public static func parseDate(_ text: String) -> Date? {
        let pattern = #"^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?(Z|[+-]\d{2}:\d{2})?$"#
        guard let regex = try? NSRegularExpression(pattern: pattern),
              let m = regex.firstMatch(in: text, range: NSRange(text.startIndex..., in: text))
        else { return nil }

        func group(_ i: Int) -> String? {
            guard let r = Range(m.range(at: i), in: text) else { return nil }
            return String(text[r])
        }

        var parts = DateComponents()
        parts.year = Int(group(1)!)
        parts.month = Int(group(2)!)
        parts.day = Int(group(3)!)
        parts.hour = Int(group(4)!)
        parts.minute = Int(group(5)!)
        parts.second = Int(group(6)!)

        if let fraction = group(7) {
            let digits = String(fraction.prefix(9)).padding(toLength: 9, withPad: "0", startingAt: 0)
            parts.nanosecond = Int(digits)
        }

        var calendar = Calendar(identifier: .gregorian)
        if let zone = group(8) {
            if zone == "Z" {
                calendar.timeZone = TimeZone(secondsFromGMT: 0)!
            } else {
                let sign = zone.hasPrefix("-") ? -1 : 1
                let hm = zone.dropFirst().split(separator: ":")
                let seconds = sign * ((Int(hm[0]) ?? 0) * 3600 + (Int(hm[1]) ?? 0) * 60)
                calendar.timeZone = TimeZone(secondsFromGMT: seconds)!
            }
        } else {
            calendar.timeZone = .current
        }
        return calendar.date(from: parts)
    }

    /// Местное время со смещением, 7 знаков долей — как пишет Windows-версия.
    public static func formatDate(_ date: Date) -> String {
        let zone = TimeZone.current
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = zone
        let p = calendar.dateComponents([.year, .month, .day, .hour, .minute, .second, .nanosecond], from: date)

        let ticks = (p.nanosecond ?? 0) / 100
        let offset = zone.secondsFromGMT(for: date)
        let sign = offset < 0 ? "-" : "+"
        let absOffset = abs(offset)

        return String(format: "%04d-%02d-%02dT%02d:%02d:%02d.%07d%@%02d:%02d",
                      p.year ?? 0, p.month ?? 0, p.day ?? 0,
                      p.hour ?? 0, p.minute ?? 0, p.second ?? 0, ticks,
                      sign, absOffset / 3600, (absOffset % 3600) / 60)
    }
}
