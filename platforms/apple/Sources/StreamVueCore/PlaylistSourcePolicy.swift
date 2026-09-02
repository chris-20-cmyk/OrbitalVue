import Foundation

public enum PlaylistSourcePolicyError: LocalizedError, Equatable, Sendable {
    case missingURL
    case unsupportedURL
    case invalidEncoding
    case oversizedPlaylist(maximumBytes: Int)

    public var errorDescription: String? {
        switch self {
        case .missingURL:
            "Enter a playlist URL."
        case .unsupportedURL:
            "Enter a complete HTTP or HTTPS playlist URL."
        case .invalidEncoding:
            "OrbitalVue could not decode that playlist as UTF-8 or UTF-16 text."
        case .oversizedPlaylist(let maximumBytes):
            "The playlist is larger than the \(maximumBytes / 1_048_576) MB safety limit."
        }
    }
}

public enum PlaylistSourcePolicy {
    public static func normalizeURL(_ rawValue: String) throws -> URL {
        let value = rawValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty else { throw PlaylistSourcePolicyError.missingURL }
        let candidate = value.contains("://") ? value : "https://\(value)"
        guard let components = URLComponents(string: candidate),
              let scheme = components.scheme?.lowercased(),
              ["http", "https"].contains(scheme),
              let host = components.host,
              !host.isEmpty,
              components.user == nil,
              components.password == nil,
              let url = components.url else {
            throw PlaylistSourcePolicyError.unsupportedURL
        }
        return url
    }

    public static func safeDisplayLocation(for url: URL) -> String {
        guard let components = URLComponents(url: url, resolvingAgainstBaseURL: false),
              let host = components.host, !host.isEmpty else {
            return "Online playlist"
        }
        let displayHost = host.contains(":") && !host.hasPrefix("[") ? "[\(host)]" : host
        return components.port.map { "\(displayHost):\($0)" } ?? displayHost
    }

    public static func safeFileDisplayName(_ rawValue: String) -> String {
        let value = URL(fileURLWithPath: rawValue).lastPathComponent
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return value.isEmpty ? "Imported playlist" : String(value.prefix(256))
    }

    public static func decode(
        _ data: Data,
        maximumBytes: Int = M3UParser.defaultMaximumBytes
    ) throws -> String {
        guard data.count <= maximumBytes else {
            throw PlaylistSourcePolicyError.oversizedPlaylist(maximumBytes: maximumBytes)
        }
        if data.starts(with: [0xFF, 0xFE]) {
            guard let value = String(data: Data(data.dropFirst(2)), encoding: .utf16LittleEndian) else {
                throw PlaylistSourcePolicyError.invalidEncoding
            }
            return value
        }
        if data.starts(with: [0xFE, 0xFF]) {
            guard let value = String(data: Data(data.dropFirst(2)), encoding: .utf16BigEndian) else {
                throw PlaylistSourcePolicyError.invalidEncoding
            }
            return value
        }
        guard let value = String(data: data, encoding: .utf8) else {
            throw PlaylistSourcePolicyError.invalidEncoding
        }
        return value.first == "\u{FEFF}" ? String(value.dropFirst()) : value
    }

    public static func redactedErrorMessage(_ error: Error) -> String {
        if let localized = error as? LocalizedError, let description = localized.errorDescription {
            return description
        }
        return "OrbitalVue could not load that source. Your private playlist address was not logged."
    }
}
