import CryptoKit
import Foundation

/// Coordinates authentication, secure credential lookup, browsing, and
/// ephemeral playback resolution without ever adding credentials to a catalog.
public actor MediaCenterService {
    private let httpClient: any MediaCenterHTTPClient
    private let secretStore: any SourceSecretStore
    private let device: MediaCenterDeviceIdentity
    private let plexClientIdentifier: String

    public init(
        httpClient: any MediaCenterHTTPClient = URLSessionMediaCenterHTTPClient(),
        secretStore: any SourceSecretStore = KeychainSourceSecretStore(
            service: "com.streamvue.player.media-center"
        ),
        device: MediaCenterDeviceIdentity = .appleDefault,
        plexClientIdentifier: String = "streamvue-apple"
    ) {
        self.httpClient = httpClient
        self.secretStore = secretStore
        self.device = device
        self.plexClientIdentifier = plexClientIdentifier
    }

    /// Connects directly to one Plex Media Server with a user-supplied server
    /// token. Plex account PIN login and account-wide server discovery are a
    /// separate layer and are intentionally not required by this core API.
    public func connectPlex(
        serverAddress: String,
        token rawToken: String,
        displayName: String? = nil
    ) async throws -> MediaCenterConnection {
        let baseURL = try MediaCenterURLPolicy.normalizeBaseURL(serverAddress)
        let token = try MediaCenterHeaderPolicy.credential(rawToken)
        let clientIdentifier = try MediaCenterURLPolicy.requireIdentifier(
            plexClientIdentifier,
            label: "Plex client"
        )
        let identity = try await PlexMediaCenterClient.discoverIdentity(
            httpClient: httpClient,
            baseURL: baseURL,
            clientIdentifier: clientIdentifier,
            device: device
        )
        let requestedName = displayName?.trimmingCharacters(in: .whitespacesAndNewlines)
        let name = MediaCenterTextPolicy.metadata(
            requestedName?.isEmpty == false ? requestedName! : identity.name,
            redacting: token,
            maximumLength: 256
        )
        let credentialID = credentialReference(
            provider: .plex,
            serverID: identity.serverID,
            baseURL: baseURL,
            userID: nil
        )
        let connection = try MediaCenterConnection(
            provider: .plex,
            serverID: identity.serverID,
            displayName: name.nonEmptyMediaCenterText ?? "Plex",
            baseURL: baseURL.absoluteString,
            credentialID: credentialID
        )
        try await saveCredential(token, for: connection)
        return connection
    }

    /// Authenticates by name once. Only the returned access token is saved in
    /// the secure store; the password is never persisted.
    public func connectEmby(
        serverAddress: String,
        username rawUsername: String,
        password: String,
        displayName: String? = nil
    ) async throws -> MediaCenterConnection {
        let baseURL = try MediaCenterURLPolicy.normalizeBaseURL(serverAddress)
        let username = rawUsername.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !username.isEmpty, username.count <= 256,
              !password.isEmpty, password.utf8.count <= 16_384 else {
            throw MediaCenterError.invalidCredential
        }
        let authentication = try await EmbyMediaCenterClient.authenticate(
            httpClient: httpClient,
            baseURL: baseURL,
            username: username,
            password: password,
            device: device
        )
        let token = try MediaCenterHeaderPolicy.credential(authentication.accessToken)
        let requestedName = displayName?.trimmingCharacters(in: .whitespacesAndNewlines)
        let name = MediaCenterTextPolicy.metadata(
            requestedName?.isEmpty == false ? requestedName! : "Emby",
            redacting: token,
            maximumLength: 256
        )
        let credentialID = credentialReference(
            provider: .emby,
            serverID: authentication.serverID,
            baseURL: baseURL,
            userID: authentication.userID
        )
        let connection = try MediaCenterConnection(
            provider: .emby,
            serverID: authentication.serverID,
            displayName: name.nonEmptyMediaCenterText ?? "Emby",
            baseURL: baseURL.absoluteString,
            credentialID: credentialID,
            userID: authentication.userID
        )
        try await saveCredential(token, for: connection)
        return connection
    }

    public func libraries(for connection: MediaCenterConnection) async throws -> [MediaCenterLibrary] {
        let token = try await credential(for: connection)
        switch connection.provider {
        case .plex:
            return try await plexClient(connection: connection, token: token).libraries()
        case .emby:
            return try await embyClient(connection: connection, token: token).libraries()
        }
    }

    public func items(
        in library: MediaCenterLibrary,
        for connection: MediaCenterConnection,
        start: Int = 0,
        size: Int = 200
    ) async throws -> MediaCenterPage<MediaCenterItem> {
        let page = MediaCenterPageBounds(start: start, size: size)
        let token = try await credential(for: connection)
        switch connection.provider {
        case .plex:
            return try await plexClient(connection: connection, token: token)
                .items(in: library, page: page)
        case .emby:
            return try await embyClient(connection: connection, token: token)
                .items(in: library, page: page)
        }
    }

    public func snapshot(
        for connection: MediaCenterConnection,
        maximumItemsPerLibrary: Int = 200,
        loadedAt: Date = Date()
    ) async throws -> MediaCenterSnapshot {
        guard maximumItemsPerLibrary > 0 else { throw MediaCenterError.invalidPage }
        let pageSize = min(maximumItemsPerLibrary, MediaCenterPageBounds.maximumSize)
        let libraries = try await libraries(for: connection)
        var allItems: [MediaCenterItem] = []
        for library in libraries {
            let page = try await items(
                in: library,
                for: connection,
                start: 0,
                size: pageSize
            )
            allItems.append(contentsOf: page.items)
        }
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        return MediaCenterSnapshot(
            loadedAt: formatter.string(from: loadedAt),
            connection: connection,
            libraries: libraries,
            items: allItems
        )
    }

    public func loadCatalog(
        for connection: MediaCenterConnection,
        maximumItemsPerLibrary: Int = 200,
        loadedAt: Date = Date()
    ) async throws -> LoadedCatalog {
        let snapshot = try await snapshot(
            for: connection,
            maximumItemsPerLibrary: maximumItemsPerLibrary,
            loadedAt: loadedAt
        )
        return LoadedCatalog(
            catalog: try MediaCenterCatalogFactory.create(from: snapshot),
            notice: "\(connection.provider.displayName) library refreshed",
            usedCachedFallback: false
        )
    }

    public func playbackPlan(
        for item: MediaCenterItem,
        connection: MediaCenterConnection,
        mediaSourceID: String? = nil,
        startPositionMS: Int? = nil
    ) async throws -> MediaCenterPlaybackPlan {
        try requireOwnership(of: item, by: connection)
        let token = try await credential(for: connection)
        switch connection.provider {
        case .plex:
            return try plexClient(connection: connection, token: token)
                .playbackPlan(for: item, mediaSourceID: mediaSourceID)
        case .emby:
            return try await embyClient(connection: connection, token: token).playbackPlan(
                for: item,
                mediaSourceID: mediaSourceID,
                startPositionMS: max(0, startPositionMS ?? item.resumePositionMS ?? 0)
            )
        }
    }

    public func artworkPlan(
        for item: MediaCenterItem,
        connection: MediaCenterConnection,
        maximumWidth: Int = 640
    ) async throws -> MediaCenterPlaybackPlan? {
        try requireOwnership(of: item, by: connection)
        guard item.artworkPath != nil else { return nil }
        let token = try await credential(for: connection)
        switch connection.provider {
        case .plex:
            return try plexClient(connection: connection, token: token)
                .artworkPlan(for: item, maximumWidth: maximumWidth)
        case .emby:
            return try embyClient(connection: connection, token: token)
                .artworkPlan(for: item, maximumWidth: maximumWidth)
        }
    }

    public func hasCredential(for connection: MediaCenterConnection) async throws -> Bool {
        (try? await credential(for: connection)) != nil
    }

    public func disconnect(_ connection: MediaCenterConnection) async throws {
        try await secretStore.removeValue(for: connection.credentialID)
    }

    private func credential(for connection: MediaCenterConnection) async throws -> String {
        guard let rawValue = try await secretStore.value(for: connection.credentialID) else {
            throw MediaCenterError.missingCredential
        }
        guard let data = rawValue.data(using: .utf8),
              let stored = try? JSONDecoder().decode(MediaCenterVaultCredential.self, from: data),
              stored.contractVersion == streamVueMediaCenterContractVersion,
              stored.provider == connection.provider,
              stored.serverID == connection.serverID,
              stored.userID == connection.userID,
              stored.baseURL == (try normalizedAddress(for: connection)) else {
            throw MediaCenterError.invalidCredential
        }
        return try MediaCenterHeaderPolicy.credential(stored.value)
    }

    private func saveCredential(
        _ value: String,
        for connection: MediaCenterConnection
    ) async throws {
        let stored = MediaCenterVaultCredential(
            provider: connection.provider,
            serverID: connection.serverID,
            userID: connection.userID,
            baseURL: try normalizedAddress(for: connection),
            value: try MediaCenterHeaderPolicy.credential(value)
        )
        let data = try JSONEncoder().encode(stored)
        guard let encoded = String(data: data, encoding: .utf8) else {
            throw MediaCenterError.invalidCredential
        }
        try await secretStore.save(encoded, for: connection.credentialID)
    }

    private func normalizedAddress(for connection: MediaCenterConnection) throws -> String {
        try MediaCenterURLPolicy.normalizeBaseURL(connection.baseURL).absoluteString
    }

    private func plexClient(connection: MediaCenterConnection, token: String) throws -> PlexMediaCenterClient {
        try PlexMediaCenterClient(
            httpClient: httpClient,
            connection: connection,
            token: token,
            clientIdentifier: MediaCenterURLPolicy.requireIdentifier(
                plexClientIdentifier,
                label: "Plex client"
            ),
            device: device
        )
    }

    private func embyClient(connection: MediaCenterConnection, token: String) throws -> EmbyMediaCenterClient {
        try EmbyMediaCenterClient(
            httpClient: httpClient,
            connection: connection,
            token: token,
            device: device
        )
    }

    private func requireOwnership(of item: MediaCenterItem, by connection: MediaCenterConnection) throws {
        guard item.provider == connection.provider, item.serverID == connection.serverID else {
            throw MediaCenterError.providerMismatch
        }
    }

    private func credentialReference(
        provider: MediaCenterProvider,
        serverID: String,
        baseURL: URL,
        userID: String?
    ) -> String {
        let identity = "\(provider.rawValue)|\(serverID)|\(baseURL.absoluteString)|\(userID ?? "server")"
        let digest = SHA256.hash(data: Data(identity.utf8))
            .map { String(format: "%02X", $0) }
            .joined()
        return "mc-\(provider.rawValue)-\(digest.prefix(48))"
    }
}

private struct MediaCenterVaultCredential: Codable, Sendable {
    let contractVersion: String
    let provider: MediaCenterProvider
    let serverID: String
    let userID: String?
    let baseURL: String
    let value: String

    private enum CodingKeys: String, CodingKey {
        case contractVersion
        case provider
        case serverID = "serverId"
        case userID = "userId"
        case baseURL = "baseUrl"
        case value
    }

    init(
        provider: MediaCenterProvider,
        serverID: String,
        userID: String?,
        baseURL: String,
        value: String
    ) {
        self.contractVersion = streamVueMediaCenterContractVersion
        self.provider = provider
        self.serverID = serverID
        self.userID = userID
        self.baseURL = baseURL
        self.value = value
    }
}

struct MediaCenterPageBounds: Sendable {
    static let maximumSize = 500

    let start: Int
    let size: Int

    init(start: Int, size: Int) {
        self.start = max(0, start)
        self.size = min(Self.maximumSize, max(1, size))
    }
}

enum MediaCenterAPI {
    static func json(
        _ request: MediaCenterHTTPRequest,
        using client: any MediaCenterHTTPClient
    ) async throws -> MediaCenterJSON {
        let response = try await client.send(request)
        guard response.body.count <= request.maximumResponseBytes else {
            throw MediaCenterError.responseTooLarge(maximumBytes: request.maximumResponseBytes)
        }
        guard (200...299).contains(response.statusCode) else {
            throw MediaCenterError.serverStatus(response.statusCode)
        }
        do {
            return try JSONDecoder().decode(MediaCenterJSON.self, from: response.body)
        } catch {
            throw MediaCenterError.invalidResponse
        }
    }
}

enum MediaCenterTextPolicy {
    static func metadata(_ rawValue: String, redacting credential: String, maximumLength: Int) -> String {
        var value = rawValue
            .map { $0.isNewline || $0.isASCIIControlCharacter ? " " : $0 }
            .reduce(into: "") { $0.append($1) }
            .trimmingCharacters(in: .whitespacesAndNewlines)
        if !credential.isEmpty {
            value = value.replacingOccurrences(of: credential, with: "[redacted]")
        }
        return String(value.prefix(maximumLength))
    }

    static func optionalMetadata(
        _ rawValue: String?,
        redacting credential: String,
        maximumLength: Int
    ) -> String? {
        guard let rawValue else { return nil }
        return metadata(rawValue, redacting: credential, maximumLength: maximumLength)
            .nonEmptyMediaCenterText
    }
}

extension String {
    var nonEmptyMediaCenterText: String? { isEmpty ? nil : self }
}

private extension Character {
    var isASCIIControlCharacter: Bool {
        unicodeScalars.allSatisfy { $0.value < 32 || $0.value == 127 }
    }
}
