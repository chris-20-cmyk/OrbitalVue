#if os(iOS) || os(tvOS)
import Foundation
import Observation
import StreamVueCore

public struct ChannelSection: Identifiable, Sendable {
    public let name: String
    public let channels: [CatalogChannel]
    public var id: String { name }
}

@MainActor
@Observable
public final class StreamVueStore {
    public private(set) var isLoading = true
    public private(set) var loadingLabel = "Preparing your library…"
    public private(set) var catalog: StreamVueCatalog?
    public private(set) var groups: [ChannelGroup] = []
    public private(set) var visibleSections: [ChannelSection] = []
    public private(set) var favoriteSections: [ChannelSection] = []
    public private(set) var notice: String?
    public private(set) var errorMessage: String?
    public private(set) var favorites: Set<String>
    public private(set) var browseMode: MediaLibraryBrowseMode = .all
    public private(set) var browseSummary = MediaLibraryBrowseSummary(channels: [])
    public var selectedGroup: String?
    public var query = ""
    public var selectedChannel: CatalogChannel?

    public var isMediaCenterSource: Bool {
        guard let type = catalog?.sources.first?.type else { return false }
        return type == .plex || type == .emby
    }

    private let repository: PlaylistRepository
    private let mediaCenterRepository: MediaCenterRepository
    private let defaults: UserDefaults
    private var hasStarted = false

    public init(
        repository: PlaylistRepository = PlaylistRepository(),
        mediaCenterRepository: MediaCenterRepository = MediaCenterRepository(),
        defaults: UserDefaults = .standard
    ) {
        self.repository = repository
        self.mediaCenterRepository = mediaCenterRepository
        self.defaults = defaults
        favorites = Set(defaults.stringArray(forKey: Keys.favorites) ?? [])
    }

    public func start() async {
        guard !hasStarted else { return }
        hasStarted = true
        do {
            if let loaded = try await loadPreferredSource() {
                apply(loaded)
            } else {
                isLoading = false
                loadingLabel = ""
            }
        } catch {
            show(error)
        }
    }

    public func importURL(_ value: String) async {
        await perform(label: "Connecting securely…", sourceKind: .playlist) {
            try await repository.importURL(value)
        }
    }

    public func importDocument(_ url: URL) async {
        await perform(label: "Reading playlist…", sourceKind: .playlist) {
            try await repository.importDocument(at: url)
        }
    }

    @discardableResult
    public func connectPlex(
        serverAddress: String,
        token: String,
        displayName: String? = nil,
        allowInsecureHTTP: Bool = false
    ) async -> Bool {
        await perform(label: "Connecting to Plex…", sourceKind: .mediaCenter) {
            try await mediaCenterRepository.connectPlex(
                serverAddress: serverAddress,
                token: token,
                displayName: displayName,
                allowInsecureHTTP: allowInsecureHTTP
            )
        }
    }

    public func createPlexSignInChallenge() async throws -> PlexPinChallenge {
        try await mediaCenterRepository.createPlexSignInChallenge()
    }

    public func completePlexSignIn(
        challenge: PlexPinChallenge
    ) async throws -> PlexServerDiscovery? {
        try await mediaCenterRepository.completePlexSignIn(challenge: challenge)
    }

    @discardableResult
    public func connectDiscoveredPlexServer(
        discovery: PlexServerDiscovery,
        serverID: String,
        connectionURL: URL,
        allowInsecureHTTP: Bool = false
    ) async -> Bool {
        await perform(label: "Connecting discovered Plex server…", sourceKind: .mediaCenter) {
            try await mediaCenterRepository.connectDiscoveredPlexServer(
                discovery: discovery,
                serverID: serverID,
                connectionURL: connectionURL,
                allowInsecureHTTP: allowInsecureHTTP
            )
        }
    }

    public func cancelPlexDiscovery(sessionID: String) async {
        await mediaCenterRepository.cancelPlexDiscovery(sessionID: sessionID)
    }

    @discardableResult
    public func connectEmby(
        serverAddress: String,
        username: String,
        password: String,
        displayName: String? = nil,
        allowInsecureHTTP: Bool = false
    ) async -> Bool {
        await perform(label: "Connecting to Emby…", sourceKind: .mediaCenter) {
            try await mediaCenterRepository.connectEmby(
                serverAddress: serverAddress,
                username: username,
                password: password,
                displayName: displayName,
                allowInsecureHTTP: allowInsecureHTTP
            )
        }
    }

    public func refresh() async {
        let sourceKind = activeSourceKind
        await perform(label: "Refreshing library…", sourceKind: sourceKind) {
            let loaded: LoadedCatalog?
            switch sourceKind {
            case .playlist:
                loaded = try await repository.refreshCurrent()
            case .mediaCenter:
                loaded = try await mediaCenterRepository.refreshCurrent()
            }
            guard let loaded else {
                throw PlaylistRepositoryError.sourceUnavailable
            }
            return loaded
        }
    }

    public func removeSource() async {
        isLoading = true
        do {
            switch activeSourceKind {
            case .playlist:
                try await repository.removeSource()
            case .mediaCenter:
                try await mediaCenterRepository.removeSource()
            }
            defaults.removeObject(forKey: Keys.activeSourceKind)
            catalog = nil
            groups = []
            visibleSections = []
            favoriteSections = []
            selectedChannel = nil
            selectedGroup = nil
            browseMode = .all
            browseSummary = MediaLibraryBrowseSummary(channels: [])
            query = ""
            notice = "Source removed from this device"
            isLoading = false
        } catch {
            show(error)
        }
    }

    public func selectGroup(_ group: String?) {
        selectedGroup = group
        rebuildBrowse()
    }

    public func selectBrowseMode(_ mode: MediaLibraryBrowseMode) {
        browseMode = mode
        rebuildBrowse()
    }

    public func updateQuery(_ value: String) {
        query = value
        rebuildBrowse()
    }

    public func selectChannel(_ channel: CatalogChannel) {
        selectedChannel = channel
        errorMessage = nil
    }

    public func toggleFavorite(_ channel: CatalogChannel) {
        if favorites.contains(channel.id) {
            favorites.remove(channel.id)
        } else {
            favorites.insert(channel.id)
        }
        defaults.set(Array(favorites).sorted(), forKey: Keys.favorites)
        rebuildBrowse()
    }

    public func dismissNotice() { notice = nil }
    public func dismissError() { errorMessage = nil }

    public func premiumAccessDidChange(
        from previous: PremiumAccessSnapshot,
        to current: PremiumAccessSnapshot
    ) async {
        if previous.canUseMediaCenters, !current.canUseMediaCenters {
            await mediaCenterRepository.cancelAllPlexDiscovery()
            guard isMediaCenterSource else { return }
            catalog = nil
            groups = []
            visibleSections = []
            favoriteSections = []
            selectedChannel = nil
            selectedGroup = nil
            browseMode = .all
            browseSummary = MediaLibraryBrowseSummary(channels: [])
            query = ""
            notice = nil
            errorMessage = "Premium media-center access is no longer verified. Playlist sources remain available."
            return
        }

        guard !previous.canUseMediaCenters, current.canUseMediaCenters,
              activeSourceKind == .mediaCenter, catalog == nil else { return }
        isLoading = true
        loadingLabel = "Opening your verified media center…"
        errorMessage = nil
        do {
            if let loaded = try await mediaCenterRepository.loadSaved() {
                apply(loaded)
            } else {
                isLoading = false
                loadingLabel = ""
            }
        } catch {
            show(error)
        }
    }

    /// Resolves a cache-safe media-center locator into an ephemeral URL and
    /// protected headers immediately before playback. The resolved descriptor
    /// is never written back to the catalog.
    public func playbackChannel(for channel: CatalogChannel) async -> CatalogChannel? {
        guard URL(string: channel.stream.uri)?.scheme?.lowercased() == "streamvue-media" else {
            return channel
        }
        do {
            let plan = try await mediaCenterRepository.playbackPlan(for: channel.stream.uri)
            return CatalogChannel(
                id: channel.id,
                number: channel.number,
                name: channel.name,
                group: channel.group,
                kind: channel.kind,
                sourceId: channel.sourceId,
                stream: StreamDescriptor(
                    uri: plan.url.absoluteString,
                    requestHeaders: plan.requestHeaders
                ),
                guide: channel.guide,
                catchup: channel.catchup,
                tags: channel.tags,
                media: channel.media
            )
        } catch {
            show(error)
            return nil
        }
    }

    @discardableResult
    private func perform(
        label: String,
        sourceKind: ActiveSourceKind,
        operation: () async throws -> LoadedCatalog
    ) async -> Bool {
        isLoading = true
        loadingLabel = label
        errorMessage = nil
        do {
            let loaded = try await operation()
            if sourceKind == .mediaCenter {
                try await mediaCenterRepository.ensurePremiumAccess()
            }
            apply(loaded)
            activeSourceKind = sourceKind
            return true
        } catch is CancellationError {
            isLoading = false
            loadingLabel = ""
            return false
        } catch {
            show(error)
            return false
        }
    }

    private func loadPreferredSource() async throws -> LoadedCatalog? {
        let preferred = activeSourceKind
        if let loaded = try await loadSaved(preferred) { return loaded }
        let alternate: ActiveSourceKind = preferred == .playlist ? .mediaCenter : .playlist
        if let loaded = try await loadSaved(alternate) {
            activeSourceKind = alternate
            return loaded
        }
        return nil
    }

    private func loadSaved(_ sourceKind: ActiveSourceKind) async throws -> LoadedCatalog? {
        switch sourceKind {
        case .playlist:
            return try await repository.loadSaved()
        case .mediaCenter:
            return try await mediaCenterRepository.loadSaved()
        }
    }

    private var activeSourceKind: ActiveSourceKind {
        get {
            defaults.string(forKey: Keys.activeSourceKind)
                .flatMap(ActiveSourceKind.init(rawValue:)) ?? .playlist
        }
        set { defaults.set(newValue.rawValue, forKey: Keys.activeSourceKind) }
    }

    private func apply(_ loaded: LoadedCatalog) {
        let selectedID = selectedChannel?.id
        catalog = loaded.catalog
        groups = loaded.catalog.groups
        browseSummary = MediaLibraryBrowseSummary(channels: loaded.catalog.channels)
        if !isMediaCenterSource { browseMode = .all }
        if let selectedGroup, !groups.contains(where: { $0.name == selectedGroup }) {
            self.selectedGroup = nil
        }
        selectedChannel = selectedID.flatMap { id in loaded.catalog.channels.first { $0.id == id } }
        notice = loaded.notice
        isLoading = false
        loadingLabel = ""
        errorMessage = nil
        rebuildBrowse()
    }

    private func show(_ error: Error) {
        isLoading = false
        loadingLabel = ""
        errorMessage = PlaylistSourcePolicy.redactedErrorMessage(error)
    }

    private func rebuildBrowse() {
        guard let catalog else {
            visibleSections = []
            favoriteSections = []
            return
        }
        let normalizedQuery = query.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
        let browsable = isMediaCenterSource
            ? MediaLibraryBrowsePolicy.ordered(catalog.channels, mode: browseMode)
            : catalog.channels
        let matching = browsable.filter { channel in
            (selectedGroup == nil || channel.group == selectedGroup) &&
            (normalizedQuery.isEmpty || channel.searchableText.contains(normalizedQuery))
        }
        visibleSections = makeSections(matching, mode: browseMode)
        favoriteSections = makeSections(matching.filter { favorites.contains($0.id) }, mode: .all)
    }

    private func makeSections(
        _ channels: [CatalogChannel],
        mode: MediaLibraryBrowseMode = .all
    ) -> [ChannelSection] {
        if mode == .continueWatching || mode == .recentlyAdded {
            return channels.isEmpty ? [] : [ChannelSection(name: mode.sectionTitle, channels: channels)]
        }
        var positions: [String: Int] = [:]
        var values: [(String, [CatalogChannel])] = []
        for channel in channels {
            if let index = positions[channel.group] {
                values[index].1.append(channel)
            } else {
                positions[channel.group] = values.count
                values.append((channel.group, [channel]))
            }
        }
        return values.map { ChannelSection(name: $0.0, channels: $0.1) }
    }

    public static func preview() -> StreamVueStore {
        let suite = UserDefaults(suiteName: "StreamVuePreview-\(UUID().uuidString)")!
        let store = StreamVueStore(defaults: suite)
        let source = CatalogSource(
            id: "preview-source",
            name: "Premium Preview",
            type: .generated,
            displayLocation: "preview.invalid",
            refreshOnLaunch: false
        )
        let groups = ["USA Premium", "Sports", "Cinema"]
        let channels = (1...12).map { number in
            CatalogChannel(
                id: String(format: "%064X", number),
                number: number,
                name: ["NFL Network HD", "TNT", "Comedy Central", "History", "Food Network", "Discovery"][number % 6],
                group: groups[number % groups.count],
                kind: number % 5 == 0 ? .movie : .live,
                sourceId: source.id,
                stream: StreamDescriptor(uri: "https://stream.example.invalid/\(number).m3u8")
            )
        }
        store.apply(
            LoadedCatalog(
                catalog: StreamVueCatalog(
                    catalogId: "preview",
                    displayName: "Premium Preview",
                    loadedAt: "2026-08-26T12:00:00Z",
                    sources: [source],
                    guideSources: [],
                    channels: channels
                )
            )
        )
        store.selectedChannel = channels[0]
        return store
    }

    private enum Keys {
        static let favorites = "apple.favorite-channel-ids"
        static let activeSourceKind = "apple.active-source-kind"
    }

    private enum ActiveSourceKind: String {
        case playlist
        case mediaCenter = "media-center"
    }
}
#endif
