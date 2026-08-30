import Foundation

/// Persists one active Plex or Emby source as a cache-safe snapshot while the
/// corresponding credential remains separately bound in Keychain.
public actor MediaCenterRepository {
    public static let defaultMaximumSnapshotBytes = 64 * 1_024 * 1_024

    private let fileManager: FileManager
    private let directory: URL
    private let snapshotFile: URL
    private let service: MediaCenterService
    private let maximumSnapshotBytes: Int
    private let fixedPremiumAccess: PremiumAccessSnapshot?
    private let premiumAccessRuntime: PremiumAccessRuntime
    private var cachedSnapshot: MediaCenterSnapshot?

    public init(
        directory: URL? = nil,
        fileManager: FileManager = .default,
        service: MediaCenterService = MediaCenterService(),
        maximumSnapshotBytes: Int = defaultMaximumSnapshotBytes,
        premiumAccess: PremiumAccessSnapshot? = nil,
        premiumAccessRuntime: PremiumAccessRuntime = .shared
    ) {
        self.fileManager = fileManager
        let base = directory ?? fileManager.urls(
            for: .applicationSupportDirectory,
            in: .userDomainMask
        )[0].appendingPathComponent("StreamVue", isDirectory: true)
        self.directory = base
        self.snapshotFile = base.appendingPathComponent("media-center-source.json")
        self.service = service
        self.maximumSnapshotBytes = max(1, maximumSnapshotBytes)
        self.fixedPremiumAccess = premiumAccess
        self.premiumAccessRuntime = premiumAccessRuntime
    }

    public func loadSaved() async throws -> LoadedCatalog? {
        let access = await currentPremiumAccess()
        guard access.canUseMediaCenters else { return nil }
        guard fileManager.fileExists(atPath: snapshotFile.path) else { return nil }
        let saved = try readSnapshot()
        do {
            let refreshed = try await service.snapshot(for: saved.connection)
            try persist(refreshed)
            cachedSnapshot = refreshed
            return try loadedCatalog(
                from: refreshed,
                notice: "\(refreshed.connection.provider.displayName) refreshed at launch",
                usedCachedFallback: false
            )
        } catch {
            cachedSnapshot = saved
            return try loadedCatalog(
                from: saved,
                notice: "The media server could not be refreshed. StreamVue opened the last protected library snapshot.",
                usedCachedFallback: true
            )
        }
    }

    public func connectPlex(
        serverAddress: String,
        token: String,
        displayName: String? = nil,
        allowInsecureHTTP: Bool = false
    ) async throws -> LoadedCatalog {
        try await requirePremiumAccess()
        let previousConnection = try? currentSnapshot()?.connection
        let connection = try await service.connectPlex(
            serverAddress: serverAddress,
            token: token,
            displayName: displayName,
            allowInsecureHTTP: allowInsecureHTTP
        )
        return try await activate(
            connection,
            previousConnection: previousConnection,
            notice: "Plex library connected"
        )
    }

    public func createPlexSignInChallenge() async throws -> PlexPinChallenge {
        try await requirePremiumAccess()
        return try await service.createPlexSignInChallenge()
    }

    public func completePlexSignIn(
        challenge: PlexPinChallenge
    ) async throws -> PlexServerDiscovery? {
        try await requirePremiumAccess()
        let discovery = try await service.completePlexSignIn(challenge: challenge)
        do {
            try await requirePremiumAccess()
            return discovery
        } catch {
            if let discovery {
                await service.cancelPlexDiscovery(sessionID: discovery.sessionID)
            }
            throw error
        }
    }

    public func connectDiscoveredPlexServer(
        discovery: PlexServerDiscovery,
        serverID: String,
        connectionURL: URL,
        allowInsecureHTTP: Bool = false
    ) async throws -> LoadedCatalog {
        try await requirePremiumAccess()
        let previousConnection = try? currentSnapshot()?.connection
        let connection = try await service.connectDiscoveredPlexServer(
            sessionID: discovery.sessionID,
            serverID: serverID,
            connectionURL: connectionURL,
            allowInsecureHTTP: allowInsecureHTTP
        )
        return try await activate(
            connection,
            previousConnection: previousConnection,
            notice: "Plex account server connected"
        )
    }

    public func cancelPlexDiscovery(sessionID: String) async {
        await service.cancelPlexDiscovery(sessionID: sessionID)
    }

    public func cancelAllPlexDiscovery() async {
        await service.cancelAllPlexDiscovery()
    }

    public func ensurePremiumAccess() async throws {
        try await requirePremiumAccess()
    }

    public func connectEmby(
        serverAddress: String,
        username: String,
        password: String,
        displayName: String? = nil,
        allowInsecureHTTP: Bool = false
    ) async throws -> LoadedCatalog {
        try await requirePremiumAccess()
        let previousConnection = try? currentSnapshot()?.connection
        let connection = try await service.connectEmby(
            serverAddress: serverAddress,
            username: username,
            password: password,
            displayName: displayName,
            allowInsecureHTTP: allowInsecureHTTP
        )
        return try await activate(
            connection,
            previousConnection: previousConnection,
            notice: "Emby library connected"
        )
    }

    public func refreshCurrent() async throws -> LoadedCatalog? {
        try await requirePremiumAccess()
        guard let saved = try currentSnapshot() else { return nil }
        do {
            let refreshed = try await service.snapshot(for: saved.connection)
            try persist(refreshed)
            cachedSnapshot = refreshed
            return try loadedCatalog(
                from: refreshed,
                notice: "\(saved.connection.provider.displayName) library refreshed",
                usedCachedFallback: false
            )
        } catch {
            return try loadedCatalog(
                from: saved,
                notice: "The media server could not be refreshed. StreamVue kept the last protected library snapshot.",
                usedCachedFallback: true
            )
        }
    }

    public func playbackPlan(
        for internalURI: String,
        mediaSourceID: String? = nil,
        startPositionMS: Int? = nil
    ) async throws -> MediaCenterPlaybackPlan {
        try await requirePremiumAccess()
        let locator = try MediaCenterLocator.parsePlaybackURI(internalURI)
        let snapshot = try requireCurrentSnapshot()
        guard snapshot.connection.provider == locator.provider,
              snapshot.connection.serverID == locator.serverID,
              let item = snapshot.items.first(where: { $0.id == locator.itemID }) else {
            throw MediaCenterError.providerMismatch
        }
        return try await service.playbackPlan(
            for: item,
            connection: snapshot.connection,
            mediaSourceID: mediaSourceID,
            startPositionMS: startPositionMS
        )
    }

    public func artworkPlan(
        for locator: MediaCenterPlaybackLocator,
        maximumWidth: Int = 640
    ) async throws -> MediaCenterPlaybackPlan? {
        try await requirePremiumAccess()
        let snapshot = try requireCurrentSnapshot()
        guard snapshot.connection.provider == locator.provider,
              snapshot.connection.serverID == locator.serverID,
              let item = snapshot.items.first(where: { $0.id == locator.itemID }) else {
            throw MediaCenterError.providerMismatch
        }
        return try await service.artworkPlan(
            for: item,
            connection: snapshot.connection,
            maximumWidth: maximumWidth
        )
    }

    public func currentConnection() throws -> MediaCenterConnection? {
        try currentSnapshot()?.connection
    }

    public func removeSource() async throws {
        let connection = try? currentSnapshot()?.connection
        if let connection { try await service.disconnect(connection) }
        cachedSnapshot = nil
        if fileManager.fileExists(atPath: snapshotFile.path) {
            try fileManager.removeItem(at: snapshotFile)
        }
    }

    private func activate(
        _ connection: MediaCenterConnection,
        previousConnection: MediaCenterConnection?,
        notice: String
    ) async throws -> LoadedCatalog {
        do {
            let snapshot = try await service.snapshot(for: connection)
            try await requirePremiumAccess()
            try persist(snapshot)
            cachedSnapshot = snapshot
            if let previousConnection,
               previousConnection.credentialID != connection.credentialID {
                try? await service.disconnect(previousConnection)
            }
            return try loadedCatalog(
                from: snapshot,
                notice: notice,
                usedCachedFallback: false
            )
        } catch {
            try? await service.disconnect(connection)
            throw error
        }
    }

    private func loadedCatalog(
        from snapshot: MediaCenterSnapshot,
        notice: String,
        usedCachedFallback: Bool
    ) throws -> LoadedCatalog {
        LoadedCatalog(
            catalog: try MediaCenterCatalogFactory.create(from: snapshot),
            notice: notice,
            usedCachedFallback: usedCachedFallback
        )
    }

    private func requireCurrentSnapshot() throws -> MediaCenterSnapshot {
        guard let snapshot = try currentSnapshot() else {
            throw MediaCenterError.invalidResponse
        }
        return snapshot
    }

    private func currentSnapshot() throws -> MediaCenterSnapshot? {
        if let cachedSnapshot { return cachedSnapshot }
        guard fileManager.fileExists(atPath: snapshotFile.path) else { return nil }
        let snapshot = try readSnapshot()
        cachedSnapshot = snapshot
        return snapshot
    }

    private func readSnapshot() throws -> MediaCenterSnapshot {
        let values = try snapshotFile.resourceValues(forKeys: [.fileSizeKey])
        if let size = values.fileSize, size > maximumSnapshotBytes {
            throw MediaCenterError.responseTooLarge(maximumBytes: maximumSnapshotBytes)
        }
        let data = try Data(contentsOf: snapshotFile, options: [.mappedIfSafe])
        guard data.count <= maximumSnapshotBytes else {
            throw MediaCenterError.responseTooLarge(maximumBytes: maximumSnapshotBytes)
        }
        do {
            return try JSONDecoder().decode(MediaCenterSnapshot.self, from: data)
        } catch {
            throw MediaCenterError.invalidResponse
        }
    }

    private func persist(_ snapshot: MediaCenterSnapshot) throws {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        let data = try encoder.encode(snapshot)
        guard data.count <= maximumSnapshotBytes else {
            throw MediaCenterError.responseTooLarge(maximumBytes: maximumSnapshotBytes)
        }
        try prepareDirectory()
        #if os(iOS) || os(tvOS)
        try data.write(to: snapshotFile, options: [.atomic, .completeFileProtection])
        #else
        try data.write(to: snapshotFile, options: .atomic)
        #endif
    }

    private func prepareDirectory() throws {
        try fileManager.createDirectory(at: directory, withIntermediateDirectories: true)
        var values = URLResourceValues()
        values.isExcludedFromBackup = true
        var mutableDirectory = directory
        try? mutableDirectory.setResourceValues(values)
    }

    private func currentPremiumAccess() async -> PremiumAccessSnapshot {
        if let fixedPremiumAccess { return fixedPremiumAccess }
        return await premiumAccessRuntime.current()
    }

    private func requirePremiumAccess() async throws {
        let access = await currentPremiumAccess()
        try access.requireMediaCenters()
    }
}
