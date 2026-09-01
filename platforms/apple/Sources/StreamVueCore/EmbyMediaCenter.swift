import Foundation

struct EmbyServerIdentity: Equatable, Sendable {
    let serverID: String
    let name: String
    let version: String?
}

struct EmbyAuthenticationResult: Equatable, Sendable {
    let accessToken: String
    let userID: String
    let serverID: String
    let userName: String
}

struct EmbyMediaCenterClient: Sendable {
    private let httpClient: any MediaCenterHTTPClient
    private let connection: MediaCenterConnection
    private let token: String
    private let userID: String
    private let apiBaseURL: URL
    private let headers: [String: String]
    private let device: MediaCenterDeviceIdentity

    init(
        httpClient: any MediaCenterHTTPClient,
        connection: MediaCenterConnection,
        token: String,
        device: MediaCenterDeviceIdentity
    ) throws {
        guard connection.provider == .emby else { throw MediaCenterError.providerMismatch }
        self.httpClient = httpClient
        self.connection = connection
        self.token = try MediaCenterHeaderPolicy.credential(token)
        self.userID = try MediaCenterURLPolicy.requireIdentifier(
            connection.userID ?? "",
            label: "Emby user"
        )
        self.apiBaseURL = try Self.apiBaseURL(for: connection.baseURL)
        self.device = device
        self.headers = try Self.authenticatedHeaders(
            token: token,
            userID: self.userID,
            device: device
        )
    }

    static func authenticate(
        httpClient: any MediaCenterHTTPClient,
        baseURL: URL,
        username rawUsername: String,
        password: String,
        device: MediaCenterDeviceIdentity
    ) async throws -> EmbyAuthenticationResult {
        let username = rawUsername.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !username.isEmpty,
              username.utf8.count <= 256,
              !password.isEmpty,
              password.utf8.count <= 16_384 else {
            throw MediaCenterError.invalidCredential
        }
        let apiBaseURL = try apiBaseURL(for: baseURL.absoluteString)
        let publicIdentity = try await discoverIdentity(
            httpClient: httpClient,
            apiBaseURL: apiBaseURL
        )
        let url = try MediaCenterURLPolicy.resolveServerPath(
            baseURL: apiBaseURL,
            path: "/Users/AuthenticateByName"
        )
        let body = try JSONSerialization.data(
            withJSONObject: ["Username": username, "Pw": password],
            options: [.sortedKeys]
        )
        let payload = try await MediaCenterAPI.json(
            MediaCenterHTTPRequest(
                method: .post,
                url: url,
                headers: [
                    "Accept": "application/json",
                    "Content-Type": "application/json",
                    "X-Emby-Authorization": authorization(device: device)
                ],
                body: body
            ),
            using: httpClient
        )
        let root = payload.objectValue
        guard let rawToken = root.text("AccessToken"),
              let rawServerID = root.text("ServerId") else {
            throw MediaCenterError.invalidResponse
        }
        let token = try MediaCenterHeaderPolicy.credential(rawToken)
        let serverID = try secureIdentifier(
            rawServerID,
            label: "Emby server",
            excluding: [token, password]
        )
        guard serverID == publicIdentity.serverID else {
            throw MediaCenterError.providerMismatch
        }
        let rawUser: [String: MediaCenterJSON]
        if let first = root.array("User").first {
            rawUser = first.objectValue
        } else {
            rawUser = root.object("User")
        }
        guard let rawUserID = rawUser.text("Id") else {
            throw MediaCenterError.invalidResponse
        }
        let userID = try secureIdentifier(
            rawUserID,
            label: "Emby user",
            excluding: [token, password]
        )
        let userName = MediaCenterTextPolicy.optionalMetadata(
            rawUser.text("Name"),
            redacting: token,
            maximumLength: 256
        ) ?? username
        return EmbyAuthenticationResult(
            accessToken: token,
            userID: userID,
            serverID: serverID,
            userName: userName
        )
    }

    func libraries() async throws -> [MediaCenterLibrary] {
        let payload = try await get("/Users/\(pathComponent(userID))/Views")
        var libraries: [MediaCenterLibrary] = []
        for value in payload.array("Items") {
            let raw = value.objectValue
            guard let rawID = raw.text("Id"),
                  let id = try? Self.secureIdentifier(
                    rawID,
                    label: "Emby library",
                    excluding: [token]
                  ),
                  let title = MediaCenterTextPolicy.optionalMetadata(
                    raw.text("Name"),
                    redacting: token,
                    maximumLength: 512
                  ) else { continue }
            libraries.append(
                MediaCenterLibrary(
                    id: id,
                    title: title,
                    kind: Self.libraryKind(raw.text("CollectionType") ?? raw.text("Type")),
                    itemCount: nonnegative(raw.integer("ChildCount"))
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
            label: "Emby library",
            excluding: [token]
        )
        let endpoint = try MediaCenterURLPolicy.resolveServerPath(
            baseURL: apiBaseURL,
            path: "/Users/\(pathComponent(userID))/Items"
        )
        let url = try MediaCenterURLPolicy.appendingQuery(
            [
                "ParentId": libraryID,
                "Recursive": "true",
                "IncludeItemTypes": "Movie,Episode,Video,MusicVideo,Recording,LiveTvChannel,Audio",
                "Fields": "MediaSources,MediaStreams,Path,PrimaryImageAspectRatio,SortName,Overview,DateCreated",
                "EnableImages": "true",
                "EnableUserData": "true",
                "StartIndex": String(page.start),
                "Limit": String(page.size)
            ],
            to: endpoint
        )
        let payload = try await get(url: url)
        var items: [MediaCenterItem] = []
        for value in payload.array("Items") {
            if let item = try? parseItem(value.objectValue, library: library) {
                items.append(item)
            }
        }
        return MediaCenterPage(
            items: items,
            start: page.start,
            size: items.count,
            total: max(items.count, payload.integer("TotalRecordCount") ?? items.count)
        )
    }

    func playbackPlan(
        for item: MediaCenterItem,
        mediaSourceID: String?,
        startPositionMS: Int
    ) async throws -> MediaCenterPlaybackPlan {
        guard item.provider == .emby, item.serverID == connection.serverID else {
            throw MediaCenterError.providerMismatch
        }
        let itemID = try Self.secureIdentifier(
            item.id,
            label: "Emby item",
            excluding: [token]
        )
        let endpoint = try MediaCenterURLPolicy.resolveServerPath(
            baseURL: apiBaseURL,
            path: "/Items/\(pathComponent(itemID))/PlaybackInfo"
        )
        let boundedStartPosition = min(
            max(0, startPositionMS),
            Int.max / 10_000
        )
        let infoURL = try MediaCenterURLPolicy.appendingQuery(
            [
                "UserId": userID,
                "StartTimeTicks": String(boundedStartPosition * 10_000)
            ],
            to: endpoint
        )
        let payload = try await get(url: infoURL)
        let candidates = payload.array("MediaSources").map(\.objectValue)
        let source = mediaSourceID.flatMap { requestedID in
            candidates.first { $0.text("Id") == requestedID }
        } ?? candidates.first
        guard let source else { throw MediaCenterError.noPlayableMedia }

        let sourceID = try Self.secureIdentifier(
            source.text("Id") ?? item.mediaSources.first?.id ?? "default",
            label: "Emby media source",
            excluding: [token]
        )
        let playSessionID = try Self.secureIdentifier(
            payload.text("PlaySessionId") ?? UUID().uuidString,
            label: "Emby play session",
            excluding: [token]
        )
        let supportsDirectPlay = source.boolean("SupportsDirectPlay")
        let supportsDirectStream = source.boolean("SupportsDirectStream")
        let supportsTranscode = source.boolean("SupportsTranscoding")
        let method: MediaCenterPlaybackMethod
        let url: URL
        if supportsDirectPlay || supportsDirectStream {
            method = supportsDirectPlay ? .directPlay : .directStream
            if let directStreamPath = source.text("DirectStreamUrl") {
                url = try MediaCenterURLPolicy.resolveServerPath(
                    baseURL: apiBaseURL,
                    path: directStreamPath
                )
            } else {
                let endpoint = try MediaCenterURLPolicy.resolveServerPath(
                    baseURL: apiBaseURL,
                    path: "/Videos/\(pathComponent(itemID))/stream.\(safeContainer(source.text("Container")))"
                )
                url = try MediaCenterURLPolicy.appendingQuery(
                    [
                        "MediaSourceId": sourceID,
                        "PlaySessionId": playSessionID,
                        "Static": "true"
                    ],
                    to: endpoint
                )
            }
        } else if supportsTranscode, let transcodePath = source.text("TranscodingUrl") {
            method = .transcode
            url = try MediaCenterURLPolicy.resolveServerPath(
                baseURL: apiBaseURL,
                path: transcodePath
            )
        } else {
            throw MediaCenterError.noPlayableMedia
        }

        let requiredHeaders = MediaCenterHeaderPolicy.providerHeaders(
            source.object("RequiredHttpHeaders").compactMapValues { $0.textValue }
        )
        let liveStreamID = try source.text("LiveStreamId").map {
            try Self.secureIdentifier(
                $0,
                label: "Emby live stream",
                excluding: [token]
            )
        }
        return MediaCenterPlaybackPlan(
            itemID: itemID,
            mediaSourceID: sourceID,
            method: method,
            url: url,
            requestHeaders: requiredHeaders.merging(headers) { _, protected in protected },
            sensitiveHeaderNames: ["X-Emby-Token", "X-Emby-Authorization"],
            playSessionID: playSessionID,
            liveStreamID: liveStreamID,
            requiresPlaybackReporting: true
        )
    }

    func artworkPlan(
        for item: MediaCenterItem,
        maximumWidth: Int
    ) throws -> MediaCenterPlaybackPlan? {
        guard item.provider == .emby, item.serverID == connection.serverID else {
            throw MediaCenterError.providerMismatch
        }
        guard let artworkPath = item.artworkPath else { return nil }
        let artworkURL = try MediaCenterURLPolicy.resolveServerPath(
            baseURL: apiBaseURL,
            path: artworkPath
        )
        let url = try MediaCenterURLPolicy.appendingQuery(
            ["MaxWidth": String(min(2_000, max(64, maximumWidth)))],
            to: artworkURL
        )
        return MediaCenterPlaybackPlan(
            itemID: item.id,
            mediaSourceID: "artwork",
            method: .directPlay,
            url: url,
            requestHeaders: headers,
            sensitiveHeaderNames: ["X-Emby-Token", "X-Emby-Authorization"],
            requiresPlaybackReporting: false
        )
    }

    private static func discoverIdentity(
        httpClient: any MediaCenterHTTPClient,
        apiBaseURL: URL
    ) async throws -> EmbyServerIdentity {
        let url = try MediaCenterURLPolicy.resolveServerPath(
            baseURL: apiBaseURL,
            path: "/System/Info/Public"
        )
        let payload = try await MediaCenterAPI.json(
            MediaCenterHTTPRequest(
                method: .get,
                url: url,
                headers: ["Accept": "application/json"]
            ),
            using: httpClient
        )
        let root = payload.objectValue
        guard let serverID = try root.text("Id").map({
            try secureIdentifier($0, label: "Emby server", excluding: [])
        }) else {
            throw MediaCenterError.invalidResponse
        }
        return EmbyServerIdentity(
            serverID: serverID,
            name: MediaCenterTextPolicy.optionalMetadata(
                root.text("ServerName"),
                redacting: "",
                maximumLength: 256
            ) ?? "Emby",
            version: MediaCenterTextPolicy.optionalMetadata(
                root.text("Version"),
                redacting: "",
                maximumLength: 64
            )
        )
    }

    private func get(_ path: String) async throws -> MediaCenterJSON {
        let url = try MediaCenterURLPolicy.resolveServerPath(baseURL: apiBaseURL, path: path)
        return try await get(url: url)
    }

    private func get(url: URL) async throws -> MediaCenterJSON {
        let identity = try await Self.discoverIdentity(
            httpClient: httpClient,
            apiBaseURL: apiBaseURL
        )
        guard identity.serverID == connection.serverID else {
            throw MediaCenterError.providerMismatch
        }
        return try await MediaCenterAPI.json(
            MediaCenterHTTPRequest(method: .get, url: url, headers: headers),
            using: httpClient
        )
    }

    private func parseItem(
        _ raw: [String: MediaCenterJSON],
        library: MediaCenterLibrary
    ) throws -> MediaCenterItem {
        guard let rawID = raw.text("Id"),
              let rawTitle = raw.text("Name"),
              let kind = Self.itemKind(raw.text("Type")) else {
            throw MediaCenterError.invalidResponse
        }
        let id = try Self.secureIdentifier(rawID, label: "Emby item", excluding: [token])
        let title = MediaCenterTextPolicy.metadata(rawTitle, redacting: token, maximumLength: 512)
        guard !title.isEmpty else { throw MediaCenterError.invalidResponse }
        let userData = raw.object("UserData")
        let imageTags = raw.object("ImageTags")
        let primaryTag = imageTags.text("Primary") ?? raw.text("PrimaryImageTag")
        let artworkPath = primaryTag.map {
            "/Items/\(pathComponent(id))/Images/Primary?Tag=\(pathComponent($0))"
        }
        let durationTicks = nonnegative(raw.integer("RunTimeTicks"))
        let resumeTicks = nonnegative(userData.integer("PlaybackPositionTicks"))
        return MediaCenterItem(
            id: id,
            provider: .emby,
            serverID: connection.serverID,
            libraryID: library.id,
            libraryTitle: library.title,
            kind: kind,
            title: title,
            sortTitle: metadata(raw.text("SortName"), maximumLength: 512),
            seriesTitle: metadata(raw.text("SeriesName"), maximumLength: 512),
            seasonNumber: nonnegative(raw.integer("ParentIndexNumber")),
            episodeNumber: nonnegative(raw.integer("IndexNumber")),
            year: validYear(raw.integer("ProductionYear")),
            durationMS: durationTicks.map { $0 / 10_000 },
            resumePositionMS: resumeTicks.map { $0 / 10_000 },
            played: userData.boolean("Played"),
            addedAt: isoDate(raw.text("DateCreated")),
            lastPlayedAt: isoDate(userData.text("LastPlayedDate")),
            artworkPath: artworkPath,
            mediaSources: raw.array("MediaSources").enumerated().compactMap { index, value in
                parseMediaSource(value.objectValue, index: index)
            }
        )
    }

    private func parseMediaSource(
        _ raw: [String: MediaCenterJSON],
        index: Int
    ) -> MediaCenterMediaSource? {
        let rawID = raw.text("Id") ?? "source-\(index)"
        guard let id = try? Self.secureIdentifier(
            rawID,
            label: "Emby media source",
            excluding: [token]
        ) else { return nil }
        let tracks = raw.array("MediaStreams").compactMap { parseTrack($0.objectValue) }
        let video = tracks.first { $0.type == .video }
        let audio = tracks.first { $0.type == .audio }
        let rawVideo = raw.array("MediaStreams").map(\.objectValue).first {
            $0.text("Type")?.lowercased() == "video"
        }
        let playbackPath = raw.text("DirectStreamUrl").flatMap {
            try? MediaCenterURLPolicy.sanitizedPathForStorage(
                baseURL: apiBaseURL,
                path: $0
            )
        }
        return MediaCenterMediaSource(
            id: id,
            playbackPath: playbackPath,
            container: metadata(raw.text("Container"), maximumLength: 64),
            videoCodec: video?.codec,
            audioCodec: audio?.codec,
            width: positive(rawVideo?.integer("Width")),
            height: positive(rawVideo?.integer("Height")),
            bitrate: nonnegative(raw.integer("Bitrate")),
            supportsDirectPlay: raw.boolean("SupportsDirectPlay"),
            supportsDirectStream: raw.boolean("SupportsDirectStream"),
            supportsTranscode: raw.boolean("SupportsTranscoding"),
            tracks: tracks
        )
    }

    private func parseTrack(_ raw: [String: MediaCenterJSON]) -> MediaCenterTrack? {
        guard let index = nonnegative(raw.integer("Index")),
              let type = raw.text("Type").flatMap({ MediaCenterTrackType(embyType: $0) }) else {
            return nil
        }
        return MediaCenterTrack(
            index: index,
            type: type,
            codec: metadata(raw.text("Codec"), maximumLength: 64),
            language: metadata(raw.text("Language"), maximumLength: 64),
            title: metadata(raw.text("DisplayTitle") ?? raw.text("Title"), maximumLength: 256),
            isDefault: raw.boolean("IsDefault"),
            isForced: raw.boolean("IsForced"),
            channels: raw.integer("Channels").flatMap { (1...64).contains($0) ? $0 : nil }
        )
    }

    private static func apiBaseURL(for input: String) throws -> URL {
        let baseURL = try MediaCenterURLPolicy.normalizeBaseURL(input)
        guard baseURL.lastPathComponent.lowercased() != "emby" else { return baseURL }
        return baseURL.appendingPathComponent("emby", isDirectory: false)
    }

    private static func authenticatedHeaders(
        token: String,
        userID: String,
        device: MediaCenterDeviceIdentity
    ) throws -> [String: String] {
        let safeToken = try MediaCenterHeaderPolicy.credential(token)
        return [
            "Accept": "application/json",
            "X-Emby-Token": safeToken,
            "X-Emby-Authorization": authorization(
                device: device,
                userID: userID,
                token: safeToken
            )
        ]
    }

    private static func authorization(
        device: MediaCenterDeviceIdentity,
        userID: String? = nil,
        token: String? = nil
    ) -> String {
        let fields = [
            ("Client", device.client),
            ("Device", device.device),
            ("DeviceId", device.deviceID),
            ("Version", device.version)
        ] + (userID.map { [("UserId", $0)] } ?? [])
            + (token.map { [("Token", $0)] } ?? [])
        return "Emby " + fields.map { name, value in
            let safeValue = MediaCenterHeaderPolicy.applicationValue(value, fallback: "OrbitalVue")
            return "\(name)=\"\(safeValue)\""
        }.joined(separator: ", ")
    }

    private static func secureIdentifier(
        _ value: String,
        label: String,
        excluding credentials: [String]
    ) throws -> String {
        let identifier = try MediaCenterURLPolicy.requireIdentifier(value, label: label)
        guard credentials.allSatisfy({ $0.isEmpty || !identifier.contains($0) }) else {
            throw MediaCenterError.invalidResponse
        }
        return identifier
    }

    private static func libraryKind(_ value: String?) -> MediaCenterLibraryKind {
        switch value?.lowercased() {
        case "movies": .movies
        case "tvshows": .shows
        case "livetv": .liveTV
        case "music": .music
        case "recordings": .recordings
        default: .other
        }
    }

    private static func itemKind(_ value: String?) -> MediaCenterItemKind? {
        switch value?.lowercased() {
        case "movie": .movie
        case "episode": .episode
        case "video", "musicvideo": .video
        case "recording": .recording
        case "livetvchannel": .liveTV
        case "audio": .audio
        default: nil
        }
    }

    private func pathComponent(_ value: String) -> String {
        value.addingPercentEncoding(withAllowedCharacters: .streamVuePathComponentAllowed) ?? value
    }

    private func metadata(_ value: String?, maximumLength: Int) -> String? {
        MediaCenterTextPolicy.optionalMetadata(
            value,
            redacting: token,
            maximumLength: maximumLength
        )
    }

    private func safeContainer(_ value: String?) -> String {
        let candidate = value?.split(separator: ",").first?
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .lowercased()
        guard let candidate,
              !candidate.isEmpty,
              candidate.count <= 12,
              candidate.allSatisfy({ $0.isASCII && ($0.isLetter || $0.isNumber) }) else {
            return "mkv"
        }
        return candidate
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

    private func isoDate(_ value: String?) -> String? {
        guard let value,
              let date = ISO8601DateFormatter.streamVueDate(from: value),
              date < Date(timeIntervalSince1970: 32_534_352_000) else { return nil }
        return ISO8601DateFormatter.streamVueString(from: date)
    }
}

private extension MediaCenterTrackType {
    init?(embyType: String) {
        switch embyType.lowercased() {
        case "video": self = .video
        case "audio": self = .audio
        case "subtitle": self = .subtitle
        default: return nil
        }
    }
}
