import Foundation
import Security

public enum PlaylistRepositoryError: LocalizedError, Equatable, Sendable {
    case sourceUnavailable
    case missingSavedAddress
    case invalidServerResponse
    case serverStatus(Int)
    case insecureRedirect
    case tooManyRedirects
    case keychain(OSStatus)

    public var errorDescription: String? {
        switch self {
        case .sourceUnavailable:
            "The saved playlist is unavailable. Connect the source again."
        case .missingSavedAddress:
            "The protected playlist address is missing. Connect the source again."
        case .invalidServerResponse:
            "The playlist provider returned an invalid response."
        case .serverStatus(let status):
            "The playlist provider returned HTTP \(status)."
        case .insecureRedirect:
            "The playlist provider attempted an insecure HTTPS-to-HTTP redirect."
        case .tooManyRedirects:
            "The playlist provider redirected too many times."
        case .keychain:
            "OrbitalVue could not access the protected playlist credential."
        }
    }
}

public protocol PlaylistHTTPClient: Sendable {
    func load(url: URL) async throws -> Data
}

public protocol SourceSecretStore: Sendable {
    func save(_ value: String, for key: String) async throws
    func value(for key: String) async throws -> String?
    func removeValue(for key: String) async throws
}

public final class URLSessionPlaylistHTTPClient: NSObject, PlaylistHTTPClient, URLSessionTaskDelegate, @unchecked Sendable {
    private let maximumBytes: Int
    private let maximumRedirects: Int
    private let lock = NSLock()
    private var redirectCounts: [Int: Int] = [:]
    private lazy var session: URLSession = {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 15
        configuration.timeoutIntervalForResource = 30
        configuration.requestCachePolicy = .reloadIgnoringLocalCacheData
        configuration.waitsForConnectivity = true
        configuration.httpMaximumConnectionsPerHost = 2
        return URLSession(configuration: configuration, delegate: self, delegateQueue: nil)
    }()

    public init(
        maximumBytes: Int = M3UParser.defaultMaximumBytes,
        maximumRedirects: Int = 5
    ) {
        self.maximumBytes = maximumBytes
        self.maximumRedirects = maximumRedirects
    }

    public func load(url: URL) async throws -> Data {
        var request = URLRequest(url: url)
        request.setValue("application/x-mpegURL, audio/mpegurl, text/plain, */*", forHTTPHeaderField: "Accept")
        request.setValue("OrbitalVue Apple/5.6", forHTTPHeaderField: "User-Agent")
        let (data, response) = try await session.data(for: request)
        defer { clearRedirectCounts() }
        guard let response = response as? HTTPURLResponse else {
            throw PlaylistRepositoryError.invalidServerResponse
        }
        guard (200...299).contains(response.statusCode) else {
            throw PlaylistRepositoryError.serverStatus(response.statusCode)
        }
        if response.expectedContentLength > Int64(maximumBytes) || data.count > maximumBytes {
            throw PlaylistSourcePolicyError.oversizedPlaylist(maximumBytes: maximumBytes)
        }
        return data
    }

    public func urlSession(
        _ session: URLSession,
        task: URLSessionTask,
        willPerformHTTPRedirection response: HTTPURLResponse,
        newRequest request: URLRequest,
        completionHandler: @escaping @Sendable (URLRequest?) -> Void
    ) {
        guard let next = request.url,
              let nextScheme = next.scheme?.lowercased(),
              ["http", "https"].contains(nextScheme),
              next.host?.isEmpty == false else {
            completionHandler(nil)
            return
        }
        if response.url?.scheme?.lowercased() == "https", nextScheme == "http" {
            completionHandler(nil)
            return
        }
        lock.lock()
        let count = (redirectCounts[task.taskIdentifier] ?? 0) + 1
        redirectCounts[task.taskIdentifier] = count
        lock.unlock()
        completionHandler(count <= maximumRedirects ? request : nil)
    }

    private func clearRedirectCounts() {
        lock.lock()
        redirectCounts.removeAll(keepingCapacity: true)
        lock.unlock()
    }
}

public final class KeychainSourceSecretStore: SourceSecretStore, @unchecked Sendable {
    private let service: String

    public init(service: String = "com.orbitalvue.player.playlist-source") {
        self.service = service
    }

    public func save(_ value: String, for key: String) async throws {
        try await removeValue(for: key)
        let query: [CFString: Any] = [
            kSecClass: kSecClassGenericPassword,
            kSecAttrService: service,
            kSecAttrAccount: key,
            kSecAttrAccessible: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly,
            kSecValueData: Data(value.utf8)
        ]
        let status = SecItemAdd(query as CFDictionary, nil)
        guard status == errSecSuccess else { throw PlaylistRepositoryError.keychain(status) }
    }

    public func value(for key: String) async throws -> String? {
        let query: [CFString: Any] = [
            kSecClass: kSecClassGenericPassword,
            kSecAttrService: service,
            kSecAttrAccount: key,
            kSecReturnData: true,
            kSecMatchLimit: kSecMatchLimitOne
        ]
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess,
              let data = result as? Data,
              let value = String(data: data, encoding: .utf8) else {
            throw PlaylistRepositoryError.keychain(status)
        }
        return value
    }

    public func removeValue(for key: String) async throws {
        let query: [CFString: Any] = [
            kSecClass: kSecClassGenericPassword,
            kSecAttrService: service,
            kSecAttrAccount: key
        ]
        let status = SecItemDelete(query as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else {
            throw PlaylistRepositoryError.keychain(status)
        }
    }
}

public actor PlaylistRepository {
    private let fileManager: FileManager
    private let directory: URL
    private let sourceFile: URL
    private let cacheFile: URL
    private let httpClient: any PlaylistHTTPClient
    private let secretStore: any SourceSecretStore

    public init(
        directory: URL? = nil,
        fileManager: FileManager = .default,
        httpClient: any PlaylistHTTPClient = URLSessionPlaylistHTTPClient(),
        secretStore: any SourceSecretStore = KeychainSourceSecretStore()
    ) {
        self.fileManager = fileManager
        let base = directory ?? fileManager.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("OrbitalVue", isDirectory: true)
        self.directory = base
        self.sourceFile = base.appendingPathComponent("source.json")
        self.cacheFile = base.appendingPathComponent("source.m3u")
        self.httpClient = httpClient
        self.secretStore = secretStore
    }

    public func loadSaved() async throws -> LoadedCatalog? {
        guard fileManager.fileExists(atPath: sourceFile.path) else { return nil }
        let record = try JSONDecoder().decode(StoredSource.self, from: Data(contentsOf: sourceFile))
        switch record.type {
        case .m3uURL:
            guard let rawURL = try await secretStore.value(for: record.id),
                  let url = URL(string: rawURL) else {
                throw PlaylistRepositoryError.missingSavedAddress
            }
            do {
                let text = try PlaylistSourcePolicy.decode(try await httpClient.load(url: url))
                let loaded = try buildLoadedCatalog(
                    text: text,
                    record: record,
                    notice: "Playlist refreshed at launch",
                    usedCachedFallback: false
                )
                try writeProtected(Data(text.utf8), to: cacheFile)
                return loaded
            } catch {
                guard fileManager.fileExists(atPath: cacheFile.path) else { throw error }
                let text = try PlaylistSourcePolicy.decode(Data(contentsOf: cacheFile))
                return try buildLoadedCatalog(
                    text: text,
                    record: record,
                    notice: "The source could not be refreshed. OrbitalVue protected playback with the last working copy.",
                    usedCachedFallback: true
                )
            }
        case .m3uFile:
            guard fileManager.fileExists(atPath: cacheFile.path) else {
                throw PlaylistRepositoryError.sourceUnavailable
            }
            return try buildLoadedCatalog(
                text: PlaylistSourcePolicy.decode(Data(contentsOf: cacheFile)),
                record: record,
                notice: nil,
                usedCachedFallback: false
            )
        case .xtream, .plex, .emby, .generated:
            throw PlaylistRepositoryError.sourceUnavailable
        }
    }

    public func importDocument(at url: URL) async throws -> LoadedCatalog {
        let accessed = url.startAccessingSecurityScopedResource()
        defer { if accessed { url.stopAccessingSecurityScopedResource() } }
        let displayName = PlaylistSourcePolicy.safeFileDisplayName(url.lastPathComponent)
        let data = try Data(contentsOf: url, options: [.mappedIfSafe])
        return try await importFile(data: data, displayName: displayName)
    }

    public func importFile(data: Data, displayName: String) async throws -> LoadedCatalog {
        let safeName = PlaylistSourcePolicy.safeFileDisplayName(displayName)
        let sourceName = URL(fileURLWithPath: safeName).deletingPathExtension().lastPathComponent.nonEmpty
            ?? "Imported playlist"
        let record = StoredSource(
            id: UUID().uuidString,
            name: sourceName,
            type: .m3uFile,
            displayLocation: safeName,
            refreshOnLaunch: false
        )
        let text = try PlaylistSourcePolicy.decode(data)
        let loaded = try buildLoadedCatalog(text: text, record: record, notice: "Playlist imported", usedCachedFallback: false)
        try await persist(record: record, rawPlaylist: text, protectedAddress: nil)
        return loaded
    }

    public func importURL(_ rawValue: String) async throws -> LoadedCatalog {
        let url = try PlaylistSourcePolicy.normalizeURL(rawValue)
        let displayLocation = PlaylistSourcePolicy.safeDisplayLocation(for: url)
        let record = StoredSource(
            id: UUID().uuidString,
            name: url.host?.nonEmpty ?? "Online playlist",
            type: .m3uURL,
            displayLocation: displayLocation,
            refreshOnLaunch: true
        )
        let text = try PlaylistSourcePolicy.decode(try await httpClient.load(url: url))
        let loaded = try buildLoadedCatalog(
            text: text,
            record: record,
            notice: "Playlist connected and startup refresh enabled",
            usedCachedFallback: false
        )
        try await persist(record: record, rawPlaylist: text, protectedAddress: url.absoluteString)
        return loaded
    }

    public func refreshCurrent() async throws -> LoadedCatalog? {
        try await loadSaved()
    }

    public func removeSource() async throws {
        let oldRecord = try? JSONDecoder().decode(StoredSource.self, from: Data(contentsOf: sourceFile))
        if let oldRecord { try await secretStore.removeValue(for: oldRecord.id) }
        if fileManager.fileExists(atPath: sourceFile.path) { try fileManager.removeItem(at: sourceFile) }
        if fileManager.fileExists(atPath: cacheFile.path) { try fileManager.removeItem(at: cacheFile) }
    }

    private func buildLoadedCatalog(
        text: String,
        record: StoredSource,
        notice: String?,
        usedCachedFallback: Bool
    ) throws -> LoadedCatalog {
        let source = CatalogSource(
            id: record.id,
            name: record.name,
            type: record.type,
            displayLocation: record.displayLocation,
            refreshOnLaunch: record.refreshOnLaunch
        )
        let catalog = try OrbitalVueCatalogFactory.create(
            fromM3U: text,
            catalogId: record.id,
            displayName: record.name,
            source: source
        )
        return LoadedCatalog(catalog: catalog, notice: notice, usedCachedFallback: usedCachedFallback)
    }

    private func persist(record: StoredSource, rawPlaylist: String, protectedAddress: String?) async throws {
        try prepareDirectory()
        let oldRecord = try? JSONDecoder().decode(StoredSource.self, from: Data(contentsOf: sourceFile))
        if let protectedAddress {
            try await secretStore.save(protectedAddress, for: record.id)
        }
        try writeProtected(Data(rawPlaylist.utf8), to: cacheFile)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try writeProtected(try encoder.encode(record), to: sourceFile)
        if let oldRecord, oldRecord.id != record.id {
            try await secretStore.removeValue(for: oldRecord.id)
        }
    }

    private func prepareDirectory() throws {
        try fileManager.createDirectory(at: directory, withIntermediateDirectories: true)
        var values = URLResourceValues()
        values.isExcludedFromBackup = true
        var mutableDirectory = directory
        try? mutableDirectory.setResourceValues(values)
    }

    private func writeProtected(_ data: Data, to url: URL) throws {
        try prepareDirectory()
        #if os(iOS) || os(tvOS)
        try data.write(to: url, options: [.atomic, .completeFileProtection])
        #else
        try data.write(to: url, options: .atomic)
        #endif
    }
}

private struct StoredSource: Codable, Sendable {
    let id: String
    let name: String
    let type: CatalogSourceType
    let displayLocation: String
    let refreshOnLaunch: Bool
}

private extension String {
    var nonEmpty: String? { isEmpty ? nil : self }
}
