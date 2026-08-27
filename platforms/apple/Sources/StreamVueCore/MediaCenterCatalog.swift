import CryptoKit
import Foundation

public enum MediaCenterLocator {
    public static func playbackURI(for locator: MediaCenterPlaybackLocator) throws -> String {
        try uri(scheme: "streamvue-media", locator: locator)
    }

    public static func artworkURI(for locator: MediaCenterPlaybackLocator) throws -> String {
        try uri(scheme: "streamvue-artwork", locator: locator)
    }

    public static func parsePlaybackURI(_ value: String) throws -> MediaCenterPlaybackLocator {
        guard value == value.trimmingCharacters(in: .whitespacesAndNewlines),
              let components = URLComponents(string: value),
              components.scheme?.lowercased() == "streamvue-media",
              let host = components.host,
              let provider = MediaCenterProvider(rawValue: host.lowercased()),
              components.user == nil,
              components.password == nil,
              components.port == nil,
              components.percentEncodedQuery == nil,
              components.percentEncodedFragment == nil else {
            throw MediaCenterError.unsafeProviderURL
        }
        let parts = components.percentEncodedPath.split(separator: "/", omittingEmptySubsequences: true)
        guard parts.count == 2,
              let serverID = String(parts[0]).removingPercentEncoding,
              let itemID = String(parts[1]).removingPercentEncoding else {
            throw MediaCenterError.unsafeProviderURL
        }
        let locator = MediaCenterPlaybackLocator(
            provider: provider,
            serverID: try MediaCenterURLPolicy.requireIdentifier(serverID, label: "server"),
            itemID: try MediaCenterURLPolicy.requireIdentifier(itemID, label: "item")
        )
        guard components.string == (try playbackURI(for: locator)) else {
            throw MediaCenterError.unsafeProviderURL
        }
        return locator
    }

    private static func uri(scheme: String, locator: MediaCenterPlaybackLocator) throws -> String {
        let serverID = try MediaCenterURLPolicy.requireIdentifier(locator.serverID, label: "server")
        let itemID = try MediaCenterURLPolicy.requireIdentifier(locator.itemID, label: "item")
        var components = URLComponents()
        components.scheme = scheme
        components.host = locator.provider.rawValue
        components.percentEncodedPath = "/\(serverID)/\(itemID)"
        guard let value = components.string else { throw MediaCenterError.unsafeProviderURL }
        return value
    }
}

public enum MediaCenterCatalogFactory {
    public static func create(from snapshot: MediaCenterSnapshot) throws -> StreamVueCatalog {
        guard snapshot.contractVersion == streamVueMediaCenterContractVersion else {
            throw MediaCenterError.invalidResponse
        }
        let connection = snapshot.connection
        let serverID = try MediaCenterURLPolicy.requireIdentifier(connection.serverID, label: "server")
        let sourceID = "MC-\(hash("\(connection.provider.rawValue)|\(serverID)").prefix(48))"
        var libraryNames: [String: String] = [:]
        for library in snapshot.libraries {
            libraryNames[library.id] = library.title
        }
        var channels: [CatalogChannel] = []
        channels.reserveCapacity(snapshot.items.count)

        for item in snapshot.items {
            guard item.provider == connection.provider, item.serverID == connection.serverID else {
                throw MediaCenterError.providerMismatch
            }
            guard let kind = catalogKind(item.kind) else { continue }
            let locator = MediaCenterPlaybackLocator(
                provider: item.provider,
                serverID: item.serverID,
                itemID: item.id
            )
            let artwork = try item.artworkPath.map { _ in
                try MediaCenterLocator.artworkURI(for: locator)
            }
            let group = item.seriesTitle
                ?? libraryNames[item.libraryID]
                ?? item.libraryTitle
            var tags = ["media-center", item.provider.rawValue, item.kind.rawValue]
            if item.played { tags.append("played") }
            if (item.resumePositionMS ?? 0) > 0 { tags.append("resume") }
            channels.append(
                CatalogChannel(
                    id: hash("media-center|\(item.provider.rawValue)|\(item.serverID)|\(item.id)"),
                    number: channels.count + 1,
                    name: displayTitle(item),
                    group: group,
                    kind: kind,
                    sourceId: sourceID,
                    stream: StreamDescriptor(
                        uri: try MediaCenterLocator.playbackURI(for: locator),
                        requestHeaders: [:]
                    ),
                    guide: artwork.map { GuideMetadata(logoUri: $0) },
                    tags: tags
                )
            )
        }

        let normalizedBaseURL = try MediaCenterURLPolicy.normalizeBaseURL(connection.baseURL)
        return StreamVueCatalog(
            catalogId: "MC-\(hash("\(connection.provider.rawValue)|\(serverID)|catalog").prefix(48))",
            displayName: "\(connection.displayName) • \(connection.provider.displayName)",
            loadedAt: snapshot.loadedAt,
            sources: [
                CatalogSource(
                    id: sourceID,
                    name: connection.displayName,
                    type: connection.provider.catalogSourceType,
                    displayLocation: MediaCenterURLPolicy.safeDisplayLocation(for: normalizedBaseURL),
                    refreshOnLaunch: true
                )
            ],
            guideSources: [],
            channels: channels
        )
    }

    private static func catalogKind(_ kind: MediaCenterItemKind) -> ChannelKind? {
        switch kind {
        case .movie, .video: .movie
        case .episode: .series
        case .recording: .recording
        case .liveTV: .live
        case .audio: nil
        }
    }

    private static func displayTitle(_ item: MediaCenterItem) -> String {
        guard item.kind == .episode else { return item.title }
        let season = item.seasonNumber.map { String(format: "S%02d", $0) } ?? ""
        let episode = item.episodeNumber.map { String(format: "E%02d", $0) } ?? ""
        let prefix = "\(season)\(episode)"
        return prefix.isEmpty ? item.title : "\(prefix) • \(item.title)"
    }

    private static func hash(_ value: String) -> String {
        SHA256.hash(data: Data(value.utf8))
            .map { String(format: "%02X", $0) }
            .joined()
    }
}
