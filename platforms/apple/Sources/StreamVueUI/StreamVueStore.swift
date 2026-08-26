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
    public var selectedGroup: String?
    public var query = ""
    public var selectedChannel: CatalogChannel?

    private let repository: PlaylistRepository
    private let defaults: UserDefaults
    private var hasStarted = false

    public init(
        repository: PlaylistRepository = PlaylistRepository(),
        defaults: UserDefaults = .standard
    ) {
        self.repository = repository
        self.defaults = defaults
        favorites = Set(defaults.stringArray(forKey: Keys.favorites) ?? [])
    }

    public func start() async {
        guard !hasStarted else { return }
        hasStarted = true
        do {
            if let loaded = try await repository.loadSaved() {
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
        await perform(label: "Connecting securely…") {
            try await repository.importURL(value)
        }
    }

    public func importDocument(_ url: URL) async {
        await perform(label: "Reading playlist…") {
            try await repository.importDocument(at: url)
        }
    }

    public func refresh() async {
        await perform(label: "Refreshing channels…") {
            guard let loaded = try await repository.refreshCurrent() else {
                throw PlaylistRepositoryError.sourceUnavailable
            }
            return loaded
        }
    }

    public func removeSource() async {
        isLoading = true
        do {
            try await repository.removeSource()
            catalog = nil
            groups = []
            visibleSections = []
            favoriteSections = []
            selectedChannel = nil
            selectedGroup = nil
            query = ""
            notice = "Playlist removed from this device"
            isLoading = false
        } catch {
            show(error)
        }
    }

    public func selectGroup(_ group: String?) {
        selectedGroup = group
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

    private func perform(label: String, operation: () async throws -> LoadedCatalog) async {
        isLoading = true
        loadingLabel = label
        errorMessage = nil
        do {
            apply(try await operation())
        } catch {
            show(error)
        }
    }

    private func apply(_ loaded: LoadedCatalog) {
        let selectedID = selectedChannel?.id
        catalog = loaded.catalog
        groups = loaded.catalog.groups
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
        let matching = catalog.channels.filter { channel in
            (selectedGroup == nil || channel.group == selectedGroup) &&
            (normalizedQuery.isEmpty || channel.searchableText.contains(normalizedQuery))
        }
        visibleSections = makeSections(matching)
        favoriteSections = makeSections(matching.filter { favorites.contains($0.id) })
    }

    private func makeSections(_ channels: [CatalogChannel]) -> [ChannelSection] {
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
    }
}
#endif
