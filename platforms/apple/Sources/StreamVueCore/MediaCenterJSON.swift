import Foundation

enum MediaCenterJSON: Decodable, Sendable {
    case object([String: MediaCenterJSON])
    case array([MediaCenterJSON])
    case string(String)
    case number(Double)
    case bool(Bool)
    case null

    init(from decoder: any Decoder) throws {
        let container = try decoder.singleValueContainer()
        if container.decodeNil() {
            self = .null
        } else if let value = try? container.decode(Bool.self) {
            self = .bool(value)
        } else if let value = try? container.decode(Double.self) {
            self = .number(value)
        } else if let value = try? container.decode(String.self) {
            self = .string(value)
        } else if let value = try? container.decode([String: MediaCenterJSON].self) {
            self = .object(value)
        } else if let value = try? container.decode([MediaCenterJSON].self) {
            self = .array(value)
        } else {
            throw DecodingError.dataCorruptedError(
                in: container,
                debugDescription: "Unsupported JSON value."
            )
        }
    }

    var objectValue: [String: MediaCenterJSON] {
        guard case .object(let value) = self else { return [:] }
        return value
    }

    var arrayValue: [MediaCenterJSON] {
        guard case .array(let value) = self else { return [] }
        return value
    }

    var textValue: String? {
        switch self {
        case .string(let value): value
        case .number(let value) where value.isFinite && value.rounded(.towardZero) == value:
            String(format: "%.0f", value)
        default: nil
        }
    }

    var integerValue: Int? {
        switch self {
        case .number(let value) where value.isFinite
            && value >= Double(Int.min)
            && value <= Double(Int.max):
            Int(value.rounded(.towardZero))
        case .string(let value): Int(value)
        default: nil
        }
    }

    var boolValue: Bool {
        switch self {
        case .bool(let value): value
        case .number(let value): value != 0
        case .string(let value):
            ["true", "yes", "1"].contains(value.lowercased())
        default: false
        }
    }

    subscript(_ key: String) -> MediaCenterJSON? {
        objectValue[key]
    }

    func text(_ key: String) -> String? {
        self[key]?.textValue?.trimmingCharacters(in: .whitespacesAndNewlines)
            .nonEmptyMediaCenterValue
    }

    func integer(_ key: String) -> Int? {
        self[key]?.integerValue
    }

    func boolean(_ key: String) -> Bool {
        self[key]?.boolValue ?? false
    }

    func object(_ key: String) -> [String: MediaCenterJSON] {
        self[key]?.objectValue ?? [:]
    }

    func array(_ key: String) -> [MediaCenterJSON] {
        self[key]?.arrayValue ?? []
    }
}

extension Dictionary where Key == String, Value == MediaCenterJSON {
    func text(_ key: String) -> String? {
        self[key]?.textValue?.trimmingCharacters(in: .whitespacesAndNewlines).nonEmptyMediaCenterValue
    }

    func integer(_ key: String) -> Int? {
        self[key]?.integerValue
    }

    func boolean(_ key: String) -> Bool {
        self[key]?.boolValue ?? false
    }

    func object(_ key: String) -> [String: MediaCenterJSON] {
        self[key]?.objectValue ?? [:]
    }

    func array(_ key: String) -> [MediaCenterJSON] {
        self[key]?.arrayValue ?? []
    }
}

private extension String {
    var nonEmptyMediaCenterValue: String? { isEmpty ? nil : self }
}
