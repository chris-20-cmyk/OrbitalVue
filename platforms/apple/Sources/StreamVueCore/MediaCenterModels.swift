import Foundation

public let streamVueMediaCenterContractVersion = "1.0"

public enum MediaCenterProvider: String, Codable, CaseIterable, Hashable, Identifiable, Sendable {
    case plex
    case emby

    public var id: String { rawValue }

    public var catalogSourceType: CatalogSourceType {
        switch self {
        case .plex: .plex
        case .emby: .emby
        }
    }

    public var displayName: String {
        switch self {
        case .plex: "Plex"
        case .emby: "Emby"
        }
    }
}

/// Cache-safe connection metadata. The credential itself is held by a
/// `SourceSecretStore` under `credentialID` and is deliberately not Codable here.
public struct MediaCenterConnection: Codable, Equatable, Sendable {
    public let contractVersion: String
    public let provider: MediaCenterProvider
    public let serverID: String
    public let displayName: String
    public let baseURL: String
    public let displayLocation: String
    public let credentialID: String
    public let userID: String?

    public init(
        provider: MediaCenterProvider,
        serverID: String,
        displayName: String,
        baseURL: String,
        credentialID: String,
        userID: String? = nil
    ) throws {
        let safeServerID = try MediaCenterURLPolicy.requireIdentifier(serverID, label: "server")
        let safeCredentialID = try MediaCenterURLPolicy.requireIdentifier(
            credentialID,
            label: "credential reference"
        )
        let safeUserID = try userID.map {
            try MediaCenterURLPolicy.requireIdentifier($0, label: "user")
        }
        let safeName = displayName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !safeName.isEmpty, safeName.count <= 256 else {
            throw MediaCenterError.invalidDisplayName
        }
        let normalizedURL = try MediaCenterURLPolicy.normalizeBaseURL(baseURL)

        self.contractVersion = streamVueMediaCenterContractVersion
        self.provider = provider
        self.serverID = safeServerID
        self.displayName = safeName
        self.baseURL = normalizedURL.absoluteString
        self.displayLocation = MediaCenterURLPolicy.safeDisplayLocation(for: normalizedURL)
        self.credentialID = safeCredentialID
        self.userID = safeUserID
    }

    private enum CodingKeys: String, CodingKey {
        case contractVersion
        case provider
        case serverID = "serverId"
        case displayName
        case baseURL = "baseUrl"
        case displayLocation
        case credentialID = "credentialId"
        case userID = "userId"
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let version = try container.decode(String.self, forKey: .contractVersion)
        guard version == streamVueMediaCenterContractVersion else {
            throw DecodingError.dataCorruptedError(
                forKey: .contractVersion,
                in: container,
                debugDescription: "Unsupported media-center connection contract."
            )
        }
        do {
            try self.init(
                provider: container.decode(MediaCenterProvider.self, forKey: .provider),
                serverID: container.decode(String.self, forKey: .serverID),
                displayName: container.decode(String.self, forKey: .displayName),
                baseURL: container.decode(String.self, forKey: .baseURL),
                credentialID: container.decode(String.self, forKey: .credentialID),
                userID: container.decodeIfPresent(String.self, forKey: .userID)
            )
        } catch {
            throw DecodingError.dataCorruptedError(
                forKey: .baseURL,
                in: container,
                debugDescription: "The media-center connection is unsafe or malformed."
            )
        }
    }
}

public enum MediaCenterLibraryKind: String, Codable, CaseIterable, Sendable {
    case movies
    case shows
    case recordings
    case liveTV = "live-tv"
    case music
    case other
}

public struct MediaCenterLibrary: Codable, Equatable, Sendable, Identifiable {
    public let id: String
    public let title: String
    public let kind: MediaCenterLibraryKind
    public let itemCount: Int?

    public init(id: String, title: String, kind: MediaCenterLibraryKind, itemCount: Int? = nil) {
        self.id = id
        self.title = title
        self.kind = kind
        self.itemCount = itemCount
    }
}

public enum MediaCenterItemKind: String, Codable, CaseIterable, Sendable {
    case movie
    case episode
    case video
    case recording
    case liveTV = "live-tv"
    case audio
}

public enum MediaCenterTrackType: String, Codable, CaseIterable, Sendable {
    case video
    case audio
    case subtitle
}

public struct MediaCenterTrack: Codable, Equatable, Sendable {
    public let index: Int
    public let type: MediaCenterTrackType
    public let codec: String?
    public let language: String?
    public let title: String?
    public let isDefault: Bool
    public let isForced: Bool
    public let channels: Int?

    public init(
        index: Int,
        type: MediaCenterTrackType,
        codec: String? = nil,
        language: String? = nil,
        title: String? = nil,
        isDefault: Bool = false,
        isForced: Bool = false,
        channels: Int? = nil
    ) {
        self.index = index
        self.type = type
        self.codec = codec
        self.language = language
        self.title = title
        self.isDefault = isDefault
        self.isForced = isForced
        self.channels = channels
    }
}

public struct MediaCenterMediaSource: Codable, Equatable, Sendable, Identifiable {
    public let id: String
    /// Sanitized provider-relative path. Never contains an access token.
    public let playbackPath: String?
    public let container: String?
    public let videoCodec: String?
    public let audioCodec: String?
    public let width: Int?
    public let height: Int?
    public let bitrate: Int?
    public let supportsDirectPlay: Bool
    public let supportsDirectStream: Bool
    public let supportsTranscode: Bool
    public let tracks: [MediaCenterTrack]

    public init(
        id: String,
        playbackPath: String? = nil,
        container: String? = nil,
        videoCodec: String? = nil,
        audioCodec: String? = nil,
        width: Int? = nil,
        height: Int? = nil,
        bitrate: Int? = nil,
        supportsDirectPlay: Bool,
        supportsDirectStream: Bool,
        supportsTranscode: Bool,
        tracks: [MediaCenterTrack] = []
    ) {
        self.id = id
        self.playbackPath = playbackPath
        self.container = container
        self.videoCodec = videoCodec
        self.audioCodec = audioCodec
        self.width = width
        self.height = height
        self.bitrate = bitrate
        self.supportsDirectPlay = supportsDirectPlay
        self.supportsDirectStream = supportsDirectStream
        self.supportsTranscode = supportsTranscode
        self.tracks = tracks
    }
}

public struct MediaCenterItem: Codable, Equatable, Sendable, Identifiable {
    public let id: String
    public let provider: MediaCenterProvider
    public let serverID: String
    public let libraryID: String
    public let libraryTitle: String
    public let kind: MediaCenterItemKind
    public let title: String
    public let sortTitle: String?
    public let seriesTitle: String?
    public let seasonNumber: Int?
    public let episodeNumber: Int?
    public let year: Int?
    public let durationMS: Int?
    public let resumePositionMS: Int?
    public let played: Bool
    /// Sanitized provider-relative path. Never contains an access token.
    public let artworkPath: String?
    public let mediaSources: [MediaCenterMediaSource]

    public init(
        id: String,
        provider: MediaCenterProvider,
        serverID: String,
        libraryID: String,
        libraryTitle: String,
        kind: MediaCenterItemKind,
        title: String,
        sortTitle: String? = nil,
        seriesTitle: String? = nil,
        seasonNumber: Int? = nil,
        episodeNumber: Int? = nil,
        year: Int? = nil,
        durationMS: Int? = nil,
        resumePositionMS: Int? = nil,
        played: Bool,
        artworkPath: String? = nil,
        mediaSources: [MediaCenterMediaSource]
    ) {
        self.id = id
        self.provider = provider
        self.serverID = serverID
        self.libraryID = libraryID
        self.libraryTitle = libraryTitle
        self.kind = kind
        self.title = title
        self.sortTitle = sortTitle
        self.seriesTitle = seriesTitle
        self.seasonNumber = seasonNumber
        self.episodeNumber = episodeNumber
        self.year = year
        self.durationMS = durationMS
        self.resumePositionMS = resumePositionMS
        self.played = played
        self.artworkPath = artworkPath
        self.mediaSources = mediaSources
    }

    private enum CodingKeys: String, CodingKey {
        case id
        case provider
        case serverID = "serverId"
        case libraryID = "libraryId"
        case libraryTitle
        case kind
        case title
        case sortTitle
        case seriesTitle
        case seasonNumber
        case episodeNumber
        case year
        case durationMS = "durationMs"
        case resumePositionMS = "resumePositionMs"
        case played
        case artworkPath
        case mediaSources
    }
}

public struct MediaCenterPage<Element: Equatable & Sendable>: Equatable, Sendable {
    public let items: [Element]
    public let start: Int
    public let size: Int
    public let total: Int

    public init(items: [Element], start: Int, size: Int, total: Int) {
        self.items = items
        self.start = start
        self.size = size
        self.total = total
    }
}

/// A portable snapshot that is safe to write to app storage.
public struct MediaCenterSnapshot: Codable, Equatable, Sendable {
    public let contractVersion: String
    public let loadedAt: String
    public let connection: MediaCenterConnection
    public let libraries: [MediaCenterLibrary]
    public let items: [MediaCenterItem]

    public init(
        loadedAt: String,
        connection: MediaCenterConnection,
        libraries: [MediaCenterLibrary],
        items: [MediaCenterItem]
    ) {
        self.contractVersion = streamVueMediaCenterContractVersion
        self.loadedAt = loadedAt
        self.connection = connection
        self.libraries = libraries
        self.items = items
    }
}

public enum MediaCenterPlaybackMethod: String, Codable, CaseIterable, Sendable {
    case directPlay = "direct-play"
    case directStream = "direct-stream"
    case transcode
}

/// Ephemeral playback data. Do not persist this value or include it in diagnostics.
public struct MediaCenterPlaybackPlan: Equatable, Sendable {
    public let itemID: String
    public let mediaSourceID: String
    public let method: MediaCenterPlaybackMethod
    public let url: URL
    public let requestHeaders: [String: String]
    public let sensitiveHeaderNames: Set<String>
    public let playSessionID: String?
    public let liveStreamID: String?
    public let requiresPlaybackReporting: Bool

    public init(
        itemID: String,
        mediaSourceID: String,
        method: MediaCenterPlaybackMethod,
        url: URL,
        requestHeaders: [String: String],
        sensitiveHeaderNames: Set<String>,
        playSessionID: String? = nil,
        liveStreamID: String? = nil,
        requiresPlaybackReporting: Bool
    ) {
        self.itemID = itemID
        self.mediaSourceID = mediaSourceID
        self.method = method
        self.url = url
        self.requestHeaders = requestHeaders
        self.sensitiveHeaderNames = sensitiveHeaderNames
        self.playSessionID = playSessionID
        self.liveStreamID = liveStreamID
        self.requiresPlaybackReporting = requiresPlaybackReporting
    }
}

public struct MediaCenterDeviceIdentity: Equatable, Sendable {
    public let client: String
    public let device: String
    public let deviceID: String
    public let version: String

    public init(client: String, device: String, deviceID: String, version: String) {
        self.client = client
        self.device = device
        self.deviceID = deviceID
        self.version = version
    }

    public static let appleDefault = MediaCenterDeviceIdentity(
        client: "StreamVue",
        device: "Apple",
        deviceID: "streamvue-apple",
        version: "5.1.0"
    )
}

public struct MediaCenterPlaybackLocator: Equatable, Sendable {
    public let provider: MediaCenterProvider
    public let serverID: String
    public let itemID: String

    public init(provider: MediaCenterProvider, serverID: String, itemID: String) {
        self.provider = provider
        self.serverID = serverID
        self.itemID = itemID
    }
}
