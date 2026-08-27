import Foundation

struct PlexServerIdentity: Equatable, Sendable {
    let serverID: String
    let name: String
    let version: String?
}

struct PlexMediaCenterClient: Sendable {
    private let httpClient: any MediaCenterHTTPClient
    private let connection: MediaCenterConnection
    private let token: String
    private let baseURL: URL
    private let headers: [String: String]
    private let clientIdentifier: String
    private let device: MediaCenterDeviceIdentity

    init(
        httpClient: any MediaCenterHTTPClient,
        connection: MediaCenterConnection,
        token: String,
        clientIdentifier: String,
        device: MediaCenterDeviceIdentity
    ) throws {
        guard connection.provider == .plex else { throw MediaCenterError.providerMismatch }
        self.httpClient = httpClient
        self.connection = connection
        self.token = try MediaCenterHeaderPolicy.credential(token)
        self.baseURL = try MediaCenterURLPolicy.normalizeBaseURL(connection.baseURL)
        self.clientIdentifier = try MediaCenterURLPolicy.requireIdentifier(
            clientIdentifier,
            label: "Plex client"
        )
        self.device = device
        self.headers = try Self.headers(
            token: token,
            clientIdentifier: self.clientIdentifier,
            device: device
        )
    }

    static func discoverIdentity(
        httpClient: any MediaCenterHTTPClient,
        baseURL: URL,
        clientIdentifier: String,
        device: MediaCenterDeviceIdentity
    ) async throws -> PlexServerIdentity {
        let url = try MediaCenterURLPolicy.resolveServerPath(baseURL: baseURL, path: "/identity")
        let payload = try await MediaCenterAPI.json(
            MediaCenterHTTPRequest(
                method: .get,
                url: url,
                headers: try clientHeaders(
                    clientIdentifier: clientIdentifier,
                    device: device
                )
            ),
            using: httpClient
        )
        let container = payload.object("MediaContainer")
        guard let rawServerID = container.text("machineIdentifier") else {
            throw MediaCenterError.invalidResponse
        }
        let serverID = try secureIdentifier(
            rawServerID,
            label: "Plex server",
            excluding: ""
        )
        let name = MediaCenterTextPolicy.optionalMetadata(
            container.text("friendlyName"),
            redacting: "",
            maximumLength: 256
        ) ?? "Plex"
        return PlexServerIdentity(
            serverID: serverID,
            name: name,
            version: MediaCenterTextPolicy.optionalMetadata(
                container.text("version"),
                redacting: "",
                maximumLength: 64
            )
        )
    }

    func libraries() async throws -> [MediaCenterLibrary] {
        let payload = try await get("/library/sections")
        let directories = payload.object("MediaContainer").array("Directory")
        var libraries: [MediaCenterLibrary] = []
        libraries.reserveCapacity(directories.count)
        for value in directories {
            let raw = value.objectValue
            guard let rawID = raw.text("key"),
                  let id = try? Self.secureIdentifier(
                    rawID,
                    label: "Plex library",
                    excluding: token
                  ),
                  let title = MediaCenterTextPolicy.optionalMetadata(
                    raw.text("title"),
                    redacting: token,
                    maximumLength: 512
                  ) else { continue }
            libraries.append(
                MediaCenterLibrary(
                    id: id,
                    title: title,
                    kind: Self.libraryKind(raw.text("type")),
                    itemCount: raw.integer("totalSize") ?? raw.integer("size")
                )
            )
        }
        return libraries
    }

    func items(
        in library: MediaCenterLibrary,
        page: MediaCenterPageBounds
    ) async throws -> MediaCenterPage<MediaCenterItem> {
        let libraryID = try Self.secureIdentifier(
            library.id,
            label: "Plex library",
            excluding: token
        )
        let payload = try await get(
            "/library/sections/\(pathComponent(libraryID))/all",
            additionalHeaders: [
                "X-Plex-Container-Start": String(page.start),
                "X-Plex-Container-Size": String(page.size)
            ]
        )
        let container = payload.object("MediaContainer")
        var items: [MediaCenterItem] = []
        for value in container.array("Metadata") {
            if let item = try? parseItem(value.objectValue, library: library) {
                items.append(item)
            }
        }
        return MediaCenterPage(
            items: items,
            start: max(0, container.integer("offset") ?? page.start),
            size: items.count,
            total: max(items.count, container.integer("totalSize") ?? container.integer("size") ?? items.count)
        )
    }

    func playbackPlan(
        for item: MediaCenterItem,
        mediaSourceID: String?
    ) throws -> MediaCenterPlaybackPlan {
        guard item.provider == .plex, item.serverID == connection.serverID else {
            throw MediaCenterError.providerMismatch
        }
        let source = mediaSourceID.map { id in
            item.mediaSources.first { $0.id == id }
        } ?? item.mediaSources.first
        guard let source, let playbackPath = source.playbackPath else {
            throw MediaCenterError.noPlayableMedia
        }
        let url = try MediaCenterURLPolicy.resolveServerPath(baseURL: baseURL, path: playbackPath)
        return MediaCenterPlaybackPlan(
            itemID: item.id,
            mediaSourceID: source.id,
            method: .directPlay,
            url: url,
            requestHeaders: headers,
            sensitiveHeaderNames: ["X-Plex-Token"],
            requiresPlaybackReporting: true
        )
    }

    func artworkPlan(
        for item: MediaCenterItem,
        maximumWidth: Int
    ) throws -> MediaCenterPlaybackPlan? {
        guard item.provider == .plex, item.serverID == connection.serverID else {
            throw MediaCenterError.providerMismatch
        }
        guard let artworkPath = item.artworkPath else { return nil }
        let artworkURL = try MediaCenterURLPolicy.resolveServerPath(baseURL: baseURL, path: artworkPath)
        let url = try MediaCenterURLPolicy.appendingQuery(
            ["width": String(min(2_000, max(64, maximumWidth)))],
            to: artworkURL
        )
        return MediaCenterPlaybackPlan(
            itemID: item.id,
            mediaSourceID: "artwork",
            method: .directPlay,
            url: url,
            requestHeaders: headers,
            sensitiveHeaderNames: ["X-Plex-Token"],
            requiresPlaybackReporting: false
        )
    }

    private func get(
        _ path: String,
        additionalHeaders: [String: String] = [:]
    ) async throws -> MediaCenterJSON {
        let identity = try await Self.discoverIdentity(
            httpClient: httpClient,
            baseURL: baseURL,
            clientIdentifier: clientIdentifier,
            device: device
        )
        guard identity.serverID == connection.serverID else {
            throw MediaCenterError.providerMismatch
        }
        let url = try MediaCenterURLPolicy.resolveServerPath(baseURL: baseURL, path: path)
        return try await MediaCenterAPI.json(
            MediaCenterHTTPRequest(
                method: .get,
                url: url,
                headers: headers.merging(additionalHeaders) { _, additional in additional }
            ),
            using: httpClient
        )
    }

    private func parseItem(
        _ raw: [String: MediaCenterJSON],
        library: MediaCenterLibrary
    ) throws -> MediaCenterItem {
        guard let rawID = raw.text("ratingKey"),
              let rawTitle = raw.text("title"),
              let kind = Self.itemKind(raw.text("type")) else {
            throw MediaCenterError.invalidResponse
        }
        let id = try Self.secureIdentifier(rawID, label: "Plex item", excluding: token)
        let title = MediaCenterTextPolicy.metadata(rawTitle, redacting: token, maximumLength: 512)
        guard !title.isEmpty else { throw MediaCenterError.invalidResponse }
        let mediaSources = raw.array("Media").enumerated().flatMap { mediaIndex, value in
            parseMediaSources(value.objectValue, mediaIndex: mediaIndex)
        }
        let artworkPath = raw.text("thumb").flatMap {
            try? MediaCenterURLPolicy.sanitizedPathForStorage(baseURL: baseURL, path: $0)
        }
        return MediaCenterItem(
            id: id,
            provider: .plex,
            serverID: connection.serverID,
            libraryID: library.id,
            libraryTitle: library.title,
            kind: kind,
            title: title,
            sortTitle: MediaCenterTextPolicy.optionalMetadata(
                raw.text("titleSort"),
                redacting: token,
                maximumLength: 512
            ),
            seriesTitle: MediaCenterTextPolicy.optionalMetadata(
                raw.text("grandparentTitle"),
                redacting: token,
                maximumLength: 512
            ),
            seasonNumber: nonnegative(raw.integer("parentIndex")),
            episodeNumber: nonnegative(raw.integer("index")),
            year: validYear(raw.integer("year")),
            durationMS: nonnegative(raw.integer("duration")),
            resumePositionMS: nonnegative(raw.integer("viewOffset")),
            played: (raw.integer("viewCount") ?? 0) > 0,
            artworkPath: artworkPath,
            mediaSources: mediaSources
        )
    }

    private func parseMediaSources(
        _ media: [String: MediaCenterJSON],
        mediaIndex: Int
    ) -> [MediaCenterMediaSource] {
        let mediaID = media.text("id")
        return media.array("Part").enumerated().compactMap { partIndex, value in
            let part = value.objectValue
            guard let rawPath = part.text("key"),
                  let playbackPath = try? MediaCenterURLPolicy.sanitizedPathForStorage(
                    baseURL: baseURL,
                    path: rawPath
                  ) else { return nil }
            let rawSourceID = part.text("id") ?? mediaID ?? "media-\(mediaIndex)-part-\(partIndex)"
            guard let sourceID = try? Self.secureIdentifier(
                rawSourceID,
                label: "Plex media source",
                excluding: token
            ) else { return nil }
            return MediaCenterMediaSource(
                id: sourceID,
                playbackPath: playbackPath,
                container: MediaCenterTextPolicy.optionalMetadata(
                    part.text("container") ?? media.text("container"),
                    redacting: token,
                    maximumLength: 64
                ),
                videoCodec: MediaCenterTextPolicy.optionalMetadata(
                    media.text("videoCodec"),
                    redacting: token,
                    maximumLength: 64
                ),
                audioCodec: MediaCenterTextPolicy.optionalMetadata(
                    media.text("audioCodec"),
                    redacting: token,
                    maximumLength: 64
                ),
                width: positive(media.integer("width")),
                height: positive(media.integer("height")),
                bitrate: nonnegative(media.integer("bitrate")),
                supportsDirectPlay: true,
                supportsDirectStream: true,
                supportsTranscode: true,
                tracks: part.array("Stream").compactMap { parseTrack($0.objectValue) }
            )
        }
    }

    private func parseTrack(_ raw: [String: MediaCenterJSON]) -> MediaCenterTrack? {
        guard let index = nonnegative(raw.integer("index") ?? raw.integer("id")),
              let streamType = raw.integer("streamType"),
              let type = MediaCenterTrackType(plexStreamType: streamType) else { return nil }
        return MediaCenterTrack(
            index: index,
            type: type,
            codec: MediaCenterTextPolicy.optionalMetadata(
                raw.text("codec"),
                redacting: token,
                maximumLength: 64
            ),
            language: MediaCenterTextPolicy.optionalMetadata(
                raw.text("languageCode") ?? raw.text("language"),
                redacting: token,
                maximumLength: 64
            ),
            title: MediaCenterTextPolicy.optionalMetadata(
                raw.text("title") ?? raw.text("displayTitle"),
                redacting: token,
                maximumLength: 256
            ),
            isDefault: raw.boolean("default") || raw.boolean("selected"),
            isForced: raw.boolean("forced"),
            channels: raw.integer("channels").flatMap { (1...64).contains($0) ? $0 : nil }
        )
    }

    private static func headers(
        token: String,
        clientIdentifier: String,
        device: MediaCenterDeviceIdentity
    ) throws -> [String: String] {
        var result = try clientHeaders(
            clientIdentifier: clientIdentifier,
            device: device
        )
        result["X-Plex-Token"] = try MediaCenterHeaderPolicy.credential(token)
        return result
    }

    private static func clientHeaders(
        clientIdentifier: String,
        device: MediaCenterDeviceIdentity
    ) throws -> [String: String] {
        [
            "Accept": "application/json",
            "X-Plex-Client-Identifier": try MediaCenterURLPolicy.requireIdentifier(
                clientIdentifier,
                label: "Plex client"
            ),
            "X-Plex-Product": MediaCenterHeaderPolicy.applicationValue(
                device.client,
                fallback: "StreamVue"
            ),
            "X-Plex-Version": MediaCenterHeaderPolicy.applicationValue(
                device.version,
                fallback: "5.1.0"
            ),
            "X-Plex-Platform": "Apple",
            "X-Plex-Pms-Api-Version": "1.2.2"
        ]
    }

    private static func secureIdentifier(
        _ value: String,
        label: String,
        excluding credential: String
    ) throws -> String {
        let identifier = try MediaCenterURLPolicy.requireIdentifier(value, label: label)
        guard credential.isEmpty || !identifier.contains(credential) else {
            throw MediaCenterError.invalidResponse
        }
        return identifier
    }

    private static func libraryKind(_ value: String?) -> MediaCenterLibraryKind {
        switch value?.lowercased() {
        case "movie": .movies
        case "show": .shows
        case "artist": .music
        default: .other
        }
    }

    private static func itemKind(_ value: String?) -> MediaCenterItemKind? {
        switch value?.lowercased() {
        case "movie": .movie
        case "episode": .episode
        case "clip": .video
        case "track": .audio
        default: nil
        }
    }

    private func pathComponent(_ value: String) -> String {
        value.addingPercentEncoding(withAllowedCharacters: .streamVuePathComponentAllowed) ?? value
    }

    private func nonnegative(_ value: Int?) -> Int? {
        value.flatMap { $0 >= 0 ? $0 : nil }
    }

    private func positive(_ value: Int?) -> Int? {
        value.flatMap { $0 > 0 ? $0 : nil }
    }

    private func validYear(_ value: Int?) -> Int? {
        value.flatMap { (1800...3000).contains($0) ? $0 : nil }
    }
}

private extension MediaCenterTrackType {
    init?(plexStreamType: Int) {
        switch plexStreamType {
        case 1: self = .video
        case 2: self = .audio
        case 3: self = .subtitle
        default: return nil
        }
    }
}

extension CharacterSet {
    static let streamVuePathComponentAllowed = CharacterSet.alphanumerics
        .union(CharacterSet(charactersIn: "-._~"))
}
