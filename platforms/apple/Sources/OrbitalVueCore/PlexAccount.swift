import CryptoKit
import Foundation

/// A short-lived Plex sign-in challenge. It contains no account credential.
public struct PlexPinChallenge: Identifiable, Equatable, Sendable {
    public let id: Int
    public let code: String
    public let authorizationURL: URL
    public let expiresAt: Date

    init(id: Int, code: String, authorizationURL: URL, expiresAt: Date) {
        self.id = id
        self.code = code
        self.authorizationURL = authorizationURL
        self.expiresAt = expiresAt
    }
}

public struct PlexServerConnectionChoice: Identifiable, Equatable, Sendable {
    public let url: URL
    public let isLocal: Bool
    public let isRelay: Bool
    public let isSecure: Bool
    public let isIPv6: Bool

    public var id: String { url.absoluteString }

    init(url: URL, isLocal: Bool, isRelay: Bool, isSecure: Bool, isIPv6: Bool) {
        self.url = url
        self.isLocal = isLocal
        self.isRelay = isRelay
        self.isSecure = isSecure
        self.isIPv6 = isIPv6
    }
}

/// A sanitized account resource. The server-scoped token stays inside
/// `MediaCenterService` and is never exposed to SwiftUI or catalog storage.
public struct PlexDiscoveredServer: Identifiable, Equatable, Sendable {
    public let serverID: String
    public let name: String
    public let isOwned: Bool
    public let connections: [PlexServerConnectionChoice]

    public var id: String { serverID }
    public var preferredConnection: PlexServerConnectionChoice? { connections.first }

    init(
        serverID: String,
        name: String,
        isOwned: Bool,
        connections: [PlexServerConnectionChoice]
    ) {
        self.serverID = serverID
        self.name = name
        self.isOwned = isOwned
        self.connections = connections
    }
}

/// An in-memory discovery lease. Its opaque ID refers to credentials held only
/// by the service actor and expires even if the UI forgets to cancel it.
public struct PlexServerDiscovery: Equatable, Sendable {
    public let sessionID: String
    public let servers: [PlexDiscoveredServer]
    public let expiresAt: Date

    init(sessionID: String, servers: [PlexDiscoveredServer], expiresAt: Date) {
        self.sessionID = sessionID
        self.servers = servers
        self.expiresAt = expiresAt
    }
}

struct PlexAccountServerSecret: Sendable {
    let server: PlexDiscoveredServer
    let accessToken: String
}

struct PlexAccountDiscoverySecret: Sendable {
    let servers: [PlexAccountServerSecret]
    let expiresAt: Date
}

struct PlexAccountToken: Sendable {
    let value: String
    let expiresAt: Date?
}

struct PlexDeviceSigner: Sendable {
    private let privateKey: Curve25519.Signing.PrivateKey

    init() {
        privateKey = Curve25519.Signing.PrivateKey()
    }

    init(rawRepresentation: Data) throws {
        privateKey = try Curve25519.Signing.PrivateKey(rawRepresentation: rawRepresentation)
    }

    var rawRepresentation: Data { privateKey.rawRepresentation }

    var publicJWK: [String: String] {
        let publicKey = privateKey.publicKey.rawRepresentation
        let keyID = Data(SHA256.hash(data: publicKey)).base64URLEncodedString()
        return [
            "kty": "OKP",
            "crv": "Ed25519",
            "x": publicKey.base64URLEncodedString(),
            "kid": keyID,
            "alg": "EdDSA"
        ]
    }

    func sign(claims: [String: Any]) throws -> String {
        let keyID = publicJWK["kid"] ?? ""
        let header: [String: Any] = [
            "alg": "EdDSA",
            "kid": keyID,
            "typ": "JWT"
        ]
        let encodedHeader = try Self.encodedJSON(header)
        let encodedClaims = try Self.encodedJSON(claims)
        let signingInput = "\(encodedHeader).\(encodedClaims)"
        let signature = try privateKey.signature(for: Data(signingInput.utf8))
        return "\(signingInput).\(signature.base64URLEncodedString())"
    }

    private static func encodedJSON(_ value: [String: Any]) throws -> String {
        guard JSONSerialization.isValidJSONObject(value) else {
            throw MediaCenterError.invalidResponse
        }
        let data = try JSONSerialization.data(withJSONObject: value, options: [.sortedKeys])
        return data.base64URLEncodedString()
    }
}

/// Implements Plex's signed, strong-PIN account flow. Account tokens are used
/// only long enough to verify the account and fetch server-scoped resources.
struct PlexAccountClient: Sendable {
    private static let clientsBaseURL = URL(string: "https://clients.plex.tv/api/v2")!
    private static let accountBaseURL = URL(string: "https://plex.tv/api/v2")!
    private static let maximumResponseBytes = 2 * 1_024 * 1_024

    private let httpClient: any MediaCenterHTTPClient
    private let clientIdentifier: String
    private let product: String
    private let version: String
    private let now: @Sendable () -> Date

    init(
        httpClient: any MediaCenterHTTPClient,
        clientIdentifier: String,
        product: String = "OrbitalVue",
        version: String = "5.8.0",
        now: @escaping @Sendable () -> Date = Date.init
    ) throws {
        self.httpClient = httpClient
        self.clientIdentifier = try MediaCenterURLPolicy.requireIdentifier(
            clientIdentifier,
            label: "Plex client"
        )
        self.product = MediaCenterHeaderPolicy.applicationValue(product, fallback: "OrbitalVue")
        self.version = MediaCenterHeaderPolicy.applicationValue(version, fallback: "5.8.0")
        self.now = now
    }

    func createPin(signer: PlexDeviceSigner) async throws -> PlexPinChallenge {
        let body = try JSONSerialization.data(
            withJSONObject: ["jwk": signer.publicJWK, "strong": true],
            options: [.sortedKeys]
        )
        let payload = try await json(
            method: .post,
            url: endpoint("pins"),
            headers: headers.merging(["Content-Type": "application/json"]) { _, new in new },
            body: body
        )
        guard let id = payload.integer("id"), id > 0,
              let rawCode = payload.text("code") else {
            throw MediaCenterError.invalidResponse
        }
        let code = try MediaCenterURLPolicy.requireIdentifier(rawCode, label: "Plex sign-in code")
        let createdAt = now()
        let expiresAt = expiry(in: payload, relativeTo: createdAt)
            ?? createdAt.addingTimeInterval(5 * 60)
        guard expiresAt > createdAt,
              let authorizationURL = authorizationURL(code: code) else {
            throw MediaCenterError.invalidResponse
        }
        return PlexPinChallenge(
            id: id,
            code: code,
            authorizationURL: authorizationURL,
            expiresAt: expiresAt
        )
    }

    func claimPin(
        _ challenge: PlexPinChallenge,
        signer: PlexDeviceSigner
    ) async throws -> PlexAccountToken? {
        guard challenge.id > 0, challenge.expiresAt > now() else {
            throw MediaCenterError.accountSignInExpired
        }
        let issuedAt = Int(now().timeIntervalSince1970.rounded(.down))
        guard issuedAt > 0 else { throw MediaCenterError.invalidResponse }
        let proof = try signer.sign(claims: [
            "aud": "plex.tv",
            "iss": clientIdentifier,
            "iat": issuedAt,
            "exp": issuedAt + 300
        ])
        guard var components = URLComponents(
            url: endpoint("pins/\(challenge.id)"),
            resolvingAgainstBaseURL: false
        ) else { throw MediaCenterError.invalidResponse }
        components.queryItems = [URLQueryItem(name: "deviceJWT", value: proof)]
        guard let url = components.url else { throw MediaCenterError.invalidResponse }
        let payload = try await json(method: .get, url: url, headers: headers)
        return accountToken(in: payload)
    }

    func verifyAccountToken(_ rawToken: String) async throws {
        let token = try MediaCenterHeaderPolicy.credential(rawToken)
        _ = try await json(
            method: .get,
            url: accountEndpoint("user"),
            headers: authenticatedHeaders(token: token)
        )
    }

    func discoverServers(accountToken rawToken: String) async throws -> [PlexAccountServerSecret] {
        let token = try MediaCenterHeaderPolicy.credential(rawToken)
        guard var components = URLComponents(
            url: endpoint("resources"),
            resolvingAgainstBaseURL: false
        ) else { throw MediaCenterError.invalidResponse }
        components.queryItems = [
            URLQueryItem(name: "includeHttps", value: "1"),
            URLQueryItem(name: "includeRelay", value: "1"),
            URLQueryItem(name: "includeIPv6", value: "1")
        ]
        guard let url = components.url else { throw MediaCenterError.invalidResponse }
        let payload = try await json(
            method: .get,
            url: url,
            headers: authenticatedHeaders(token: token)
        )
        return payload.arrayValue.compactMap { parseServer($0, excluding: token) }
    }

    private var headers: [String: String] {
        [
            "Accept": "application/json",
            "X-Plex-Client-Identifier": clientIdentifier,
            "X-Plex-Product": product,
            "X-Plex-Version": version
        ]
    }

    private func authenticatedHeaders(token: String) -> [String: String] {
        headers.merging(["X-Plex-Token": token]) { _, new in new }
    }

    private func endpoint(_ path: String) -> URL {
        Self.clientsBaseURL.appendingPathComponent(path)
    }

    private func accountEndpoint(_ path: String) -> URL {
        Self.accountBaseURL.appendingPathComponent(path)
    }

    private func json(
        method: MediaCenterHTTPMethod,
        url: URL,
        headers: [String: String],
        body: Data? = nil
    ) async throws -> MediaCenterJSON {
        guard url.scheme == "https",
              ["clients.plex.tv", "plex.tv"].contains(url.host?.lowercased() ?? ""),
              url.user == nil, url.password == nil else {
            throw MediaCenterError.unsafeProviderURL
        }
        return try await MediaCenterAPI.json(
            MediaCenterHTTPRequest(
                method: method,
                url: url,
                headers: headers,
                body: body,
                maximumResponseBytes: Self.maximumResponseBytes
            ),
            using: httpClient
        )
    }

    private func authorizationURL(code: String) -> URL? {
        var query = URLComponents()
        query.queryItems = [
            URLQueryItem(name: "clientID", value: clientIdentifier),
            URLQueryItem(name: "code", value: code),
            URLQueryItem(name: "context[device][product]", value: product)
        ]
        guard let encodedQuery = query.percentEncodedQuery else { return nil }
        var components = URLComponents()
        components.scheme = "https"
        components.host = "app.plex.tv"
        components.path = "/auth"
        components.percentEncodedFragment = "?\(encodedQuery)"
        return components.url
    }

    private func accountToken(in payload: MediaCenterJSON) -> PlexAccountToken? {
        let object = payload.objectValue
        guard let rawToken = object.text("authToken") ?? object.text("auth_token"),
              let token = try? MediaCenterHeaderPolicy.credential(rawToken) else {
            return nil
        }
        return PlexAccountToken(value: token, expiresAt: expiry(in: payload, relativeTo: now()))
    }

    private func expiry(in payload: MediaCenterJSON, relativeTo date: Date) -> Date? {
        let object = payload.objectValue
        if let value = object.text("expiresAt") ?? object.text("expires_at"),
           let parsed = ISO8601DateFormatter().date(from: value) {
            return parsed
        }
        guard let seconds = object.integer("expiresIn") ?? object.integer("expires_in"),
              seconds > 0 else { return nil }
        return date.addingTimeInterval(TimeInterval(seconds))
    }

    private func parseServer(
        _ value: MediaCenterJSON,
        excluding accountToken: String
    ) -> PlexAccountServerSecret? {
        let object = value.objectValue
        let provides = Set(
            (object.text("provides") ?? "")
                .split(separator: ",")
                .map { $0.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() }
        )
        guard provides.contains("server"),
              let rawServerID = object.text("clientIdentifier"),
              let rawName = object.text("name"),
              let rawToken = object.text("accessToken"),
              let accessToken = try? MediaCenterHeaderPolicy.credential(rawToken),
              let serverID = try? MediaCenterURLPolicy.requireIdentifier(
                  rawServerID,
                  label: "Plex server"
              ),
              !serverID.contains(accessToken),
              !serverID.contains(accountToken) else { return nil }

        let serverRedactedName = MediaCenterTextPolicy.metadata(
            rawName,
            redacting: accessToken,
            maximumLength: 256
        )
        let name = MediaCenterTextPolicy.metadata(
            serverRedactedName,
            redacting: accountToken,
            maximumLength: 256
        )
        guard let safeName = name.nonEmptyMediaCenterText else { return nil }
        let connections = object.array("connections")
            .compactMap { parseConnection($0, excluding: [accountToken, accessToken]) }
            .sorted { connectionPriority($0) < connectionPriority($1) }
        guard !connections.isEmpty else { return nil }
        let server = PlexDiscoveredServer(
            serverID: serverID,
            name: safeName,
            isOwned: object.boolean("owned"),
            connections: connections
        )
        return PlexAccountServerSecret(server: server, accessToken: accessToken)
    }

    private func parseConnection(
        _ value: MediaCenterJSON,
        excluding credentials: [String]
    ) -> PlexServerConnectionChoice? {
        let object = value.objectValue
        var candidate = object.text("uri")
        if candidate == nil,
           let scheme = object.text("protocol")?.lowercased(),
           ["http", "https"].contains(scheme),
           let address = object.text("address"),
           let port = object.integer("port"),
           (1...65_535).contains(port) {
            let host = address.contains(":") && !address.hasPrefix("[") ? "[\(address)]" : address
            candidate = "\(scheme)://\(host):\(port)"
        }
        guard let candidate,
              let url = try? MediaCenterURLPolicy.normalizeBaseURL(candidate),
              credentials.allSatisfy({ !$0.isEmpty && !url.absoluteString.contains($0) }) else {
            return nil
        }
        return PlexServerConnectionChoice(
            url: url,
            isLocal: object.boolean("local"),
            isRelay: object.boolean("relay"),
            isSecure: url.scheme?.lowercased() == "https",
            isIPv6: object.boolean("IPv6") || (url.host?.contains(":") == true)
        )
    }

    private func connectionPriority(_ value: PlexServerConnectionChoice) -> Int {
        (value.isSecure ? 0 : 1_000)
            + (value.isLocal ? 0 : 100)
            + (value.isRelay ? 50 : 0)
            + (value.isIPv6 ? 1 : 0)
    }
}

private extension Data {
    func base64URLEncodedString() -> String {
        base64EncodedString()
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }
}
