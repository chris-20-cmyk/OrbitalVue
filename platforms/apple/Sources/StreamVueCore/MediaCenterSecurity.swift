import Foundation

public enum MediaCenterError: LocalizedError, Equatable, Sendable {
    case invalidBaseURL
    case unsafeProviderURL
    case invalidIdentifier(String)
    case invalidDisplayName
    case invalidCredential
    case missingCredential
    case insecureTransportConsentRequired
    case invalidResponse
    case serverStatus(Int)
    case responseTooLarge(maximumBytes: Int)
    case providerMismatch
    case noPlayableMedia
    case invalidPage
    case accountSignInExpired
    case noDiscoveredServers
    case discoverySessionExpired
    case discoveryConnectionInProgress

    public var errorDescription: String? {
        switch self {
        case .invalidBaseURL:
            "Enter a complete HTTP or HTTPS media-center server address without credentials."
        case .unsafeProviderURL:
            "The media server returned an unsafe or cross-origin resource address."
        case .invalidIdentifier(let label):
            "The media-center \(label) is not a safe identifier."
        case .invalidDisplayName:
            "Enter a media-center name with no more than 256 characters."
        case .invalidCredential:
            "The media-center credential is empty or malformed."
        case .missingCredential:
            "The protected media-center credential is missing. Connect the server again."
        case .insecureTransportConsentRequired:
            "This media server uses unencrypted HTTP. Confirm the insecure connection before sending or saving credentials."
        case .invalidResponse:
            "The media center returned an invalid response."
        case .serverStatus(let status):
            "The media center returned HTTP \(status)."
        case .responseTooLarge(let maximumBytes):
            "The media-center response exceeded the \(maximumBytes / 1_048_576) MB safety limit."
        case .providerMismatch:
            "This media item belongs to a different server connection."
        case .noPlayableMedia:
            "The media center did not provide a compatible playback source."
        case .invalidPage:
            "The requested media-center page is outside the supported bounds."
        case .accountSignInExpired:
            "The Plex sign-in request expired. Start a new sign-in and approve it again."
        case .noDiscoveredServers:
            "Plex sign-in succeeded, but this account did not return a reachable Plex Media Server."
        case .discoverySessionExpired:
            "The protected Plex server-selection session expired. Sign in again to continue."
        case .discoveryConnectionInProgress:
            "This Plex server-selection session is already connecting."
        }
    }
}

public enum MediaCenterURLPolicy {
    private static let allowedSchemes = Set(["http", "https"])
    private static let sensitiveQueryKeys = Set([
        "apikey", "accesskey", "accesstoken", "authorization", "auth",
        "credential", "credentials", "password", "passwd", "pw", "secret",
        "token", "username", "user", "xembyauthorization", "xembytoken", "xplextoken"
    ])

    public static func normalizeBaseURL(_ rawValue: String) throws -> URL {
        let trimmed = rawValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, !containsControlCharacter(trimmed) else {
            throw MediaCenterError.invalidBaseURL
        }
        let hasScheme = trimmed.range(
            of: #"^[A-Za-z][A-Za-z0-9+.-]*://"#,
            options: .regularExpression
        ) != nil
        let candidate = hasScheme ? trimmed : "https://\(trimmed)"
        guard var components = URLComponents(string: candidate),
              let scheme = components.scheme?.lowercased(),
              allowedSchemes.contains(scheme),
              components.host?.isEmpty == false,
              components.user == nil,
              components.password == nil,
              components.percentEncodedQuery == nil,
              components.percentEncodedFragment == nil else {
            throw MediaCenterError.invalidBaseURL
        }
        guard isSafeEncodedPath(components.percentEncodedPath) else {
            throw MediaCenterError.invalidBaseURL
        }

        components.scheme = scheme
        while components.percentEncodedPath.count > 1,
              components.percentEncodedPath.hasSuffix("/") {
            components.percentEncodedPath.removeLast()
        }
        if components.percentEncodedPath == "/" { components.percentEncodedPath = "" }
        guard let normalized = components.url else { throw MediaCenterError.invalidBaseURL }
        return normalized
    }

    public static func safeDisplayLocation(for baseURL: URL) -> String {
        let host = baseURL.host ?? "Media server"
        let formattedHost = host.contains(":") ? "[\(host)]" : host
        let authority = baseURL.port.map { "\(formattedHost):\($0)" } ?? formattedHost
        let path = baseURL.path == "/" ? "" : baseURL.path
        return "\(authority)\(path)"
    }

    public static func requireAllowedTransport(
        _ baseURL: URL,
        allowInsecureHTTP: Bool
    ) throws {
        let normalized = try normalizeBaseURL(baseURL.absoluteString)
        if normalized.scheme?.lowercased() == "http", !allowInsecureHTTP {
            throw MediaCenterError.insecureTransportConsentRequired
        }
    }

    public static func requireIdentifier(_ value: String, label: String) throws -> String {
        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty,
              trimmed.utf8.count <= 256,
              trimmed.unicodeScalars.allSatisfy({ scalar in
                  switch scalar.value {
                  case 48...57, 65...90, 97...122, 45, 46, 58, 95: true
                  default: false
                  }
              }) else {
            throw MediaCenterError.invalidIdentifier(label)
        }
        return trimmed
    }

    public static func resolveServerPath(baseURL: URL, path rawPath: String) throws -> URL {
        let normalizedBase = try normalizeBaseURL(baseURL.absoluteString)
        let path = rawPath.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !path.isEmpty,
              !path.contains("\\"),
              !containsControlCharacter(path) else {
            throw MediaCenterError.unsafeProviderURL
        }

        let resolved: URL
        if let components = URLComponents(string: path), components.scheme != nil {
            guard let absolute = components.url else { throw MediaCenterError.unsafeProviderURL }
            resolved = absolute
        } else {
            let basePath = normalizedBase.path
            if path.hasPrefix("/"),
               !basePath.isEmpty,
               (path == basePath || path.hasPrefix("\(basePath)/")) {
                var components = URLComponents()
                components.scheme = normalizedBase.scheme
                components.host = normalizedBase.host
                components.port = normalizedBase.port
                guard let question = path.firstIndex(of: "?") else {
                    components.percentEncodedPath = path
                    guard let absolute = components.url else {
                        throw MediaCenterError.unsafeProviderURL
                    }
                    resolved = absolute
                    return try validateAndSanitize(resolved, relativeTo: normalizedBase)
                }
                components.percentEncodedPath = String(path[..<question])
                components.percentEncodedQuery = String(path[path.index(after: question)...])
                guard let absolute = components.url else { throw MediaCenterError.unsafeProviderURL }
                resolved = absolute
            } else {
                guard let directory = URL(string: normalizedBase.absoluteString + "/"),
                      let relative = URL(string: String(path.drop(while: { $0 == "/" })), relativeTo: directory) else {
                    throw MediaCenterError.unsafeProviderURL
                }
                resolved = relative.absoluteURL
            }
        }
        return try validateAndSanitize(resolved, relativeTo: normalizedBase)
    }

    public static func sanitizedPathForStorage(baseURL: URL, path: String) throws -> String {
        let normalizedBase = try normalizeBaseURL(baseURL.absoluteString)
        let resolved = try resolveServerPath(baseURL: normalizedBase, path: path)
        guard let baseComponents = URLComponents(url: normalizedBase, resolvingAgainstBaseURL: false),
              let resolvedComponents = URLComponents(url: resolved, resolvingAgainstBaseURL: false) else {
            throw MediaCenterError.unsafeProviderURL
        }
        let basePath = baseComponents.percentEncodedPath
        let fullPath = resolvedComponents.percentEncodedPath
        guard fullPath.hasPrefix(basePath) else { throw MediaCenterError.unsafeProviderURL }
        var relative = String(fullPath.dropFirst(basePath.count))
        if relative.isEmpty { relative = "/" }
        if !relative.hasPrefix("/") { relative = "/\(relative)" }
        let stored = resolvedComponents.percentEncodedQuery.map { "\(relative)?\($0)" } ?? relative
        guard stored.utf8.count <= 2_048 else { throw MediaCenterError.unsafeProviderURL }
        return stored
    }

    public static func appendingQuery(_ values: [String: String], to url: URL) throws -> URL {
        guard var components = URLComponents(url: url, resolvingAgainstBaseURL: false) else {
            throw MediaCenterError.unsafeProviderURL
        }
        var items = components.queryItems ?? []
        for (name, value) in values.sorted(by: { $0.key < $1.key }) {
            guard !name.isEmpty, !containsControlCharacter(name), !containsControlCharacter(value) else {
                throw MediaCenterError.unsafeProviderURL
            }
            items.removeAll { $0.name.caseInsensitiveCompare(name) == .orderedSame }
            items.append(URLQueryItem(name: name, value: value))
        }
        components.queryItems = items
        guard let result = components.url else { throw MediaCenterError.unsafeProviderURL }
        return result
    }

    private static func validateAndSanitize(_ candidate: URL, relativeTo baseURL: URL) throws -> URL {
        guard candidate.user == nil,
              candidate.password == nil,
              sameOrigin(candidate, baseURL),
              let candidateComponents = URLComponents(url: candidate, resolvingAgainstBaseURL: false),
              isSafeEncodedPath(candidateComponents.percentEncodedPath) else {
            throw MediaCenterError.unsafeProviderURL
        }
        let rootPath = baseURL.path
        let candidatePath = candidate.standardized.path
        guard rootPath.isEmpty || rootPath == "/" || candidatePath == rootPath || candidatePath.hasPrefix("\(rootPath)/") else {
            throw MediaCenterError.unsafeProviderURL
        }

        var safeComponents = candidateComponents
        if let queryItems = safeComponents.queryItems {
            let safeItems = queryItems.filter { !isSensitiveQueryKey($0.name) }
            safeComponents.queryItems = safeItems.isEmpty ? nil : safeItems
        }
        safeComponents.fragment = nil
        safeComponents.user = nil
        safeComponents.password = nil
        guard let result = safeComponents.url else { throw MediaCenterError.unsafeProviderURL }
        return result
    }

    private static func sameOrigin(_ lhs: URL, _ rhs: URL) -> Bool {
        lhs.scheme?.lowercased() == rhs.scheme?.lowercased()
            && lhs.host?.lowercased() == rhs.host?.lowercased()
            && effectivePort(lhs) == effectivePort(rhs)
    }

    private static func effectivePort(_ url: URL) -> Int? {
        if let port = url.port { return port }
        return switch url.scheme?.lowercased() {
        case "http": 80
        case "https": 443
        default: nil
        }
    }

    private static func isSensitiveQueryKey(_ key: String) -> Bool {
        let normalized = key.lowercased().unicodeScalars
            .filter { CharacterSet.alphanumerics.contains($0) }
            .map(String.init)
            .joined()
        return sensitiveQueryKeys.contains(normalized)
            || normalized.contains("token")
            || normalized.contains("password")
            || normalized.contains("secret")
            || normalized.contains("credential")
    }

    private static func containsControlCharacter(_ value: String) -> Bool {
        value.unicodeScalars.contains { CharacterSet.controlCharacters.contains($0) }
    }

    private static func isSafeEncodedPath(_ path: String) -> Bool {
        guard !path.contains("\\"), !containsControlCharacter(path) else { return false }
        let lowered = path.lowercased()
        return !lowered.contains("%2e") && !lowered.contains("%2f") && !lowered.contains("%5c")
    }
}

enum MediaCenterHeaderPolicy {
    private static let reservedProviderHeaders = Set([
        "authorization", "connection", "content-length", "cookie", "host",
        "proxy-authorization", "proxy-connection", "set-cookie", "te", "trailer",
        "transfer-encoding", "upgrade", "x-emby-authorization", "x-emby-token", "x-plex-token"
    ])

    static func credential(_ rawValue: String) throws -> String {
        let value = rawValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty,
              value.utf8.count <= 8_192,
              !value.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) }) else {
            throw MediaCenterError.invalidCredential
        }
        return value
    }

    static func applicationValue(_ rawValue: String, fallback: String, maximumLength: Int = 512) -> String {
        let value = rawValue
            .filter { !$0.isNewline && !$0.isASCIIControl }
            .replacingOccurrences(of: "\"", with: "")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return String((value.isEmpty ? fallback : value).prefix(maximumLength))
    }

    static func providerHeaders(_ values: [String: String]) -> [String: String] {
        var result: [String: String] = [:]
        for (name, rawValue) in values {
            let lowercaseName = name.lowercased()
            guard !reservedProviderHeaders.contains(lowercaseName),
                  isHeaderName(name) else { continue }
            let value = applicationValue(rawValue, fallback: "", maximumLength: 1_024)
            if !value.isEmpty { result[name] = value }
        }
        return result
    }

    static func isHeaderName(_ value: String) -> Bool {
        !value.isEmpty && value.utf8.count <= 64 && value.unicodeScalars.allSatisfy { scalar in
            switch scalar.value {
            case 48...57, 65...90, 97...122, 45: true
            default: false
            }
        }
    }
}

private extension Character {
    var isASCIIControl: Bool {
        unicodeScalars.allSatisfy { $0.value < 32 || $0.value == 127 }
    }
}
