import Foundation

public let orbitalVueCatalogContractVersion = "1.0"

public enum ChannelKind: String, Codable, CaseIterable, Sendable {
    case live
    case movie
    case series
    case recording
    case replay
    case music

    public var label: String { rawValue.uppercased() }
}

public enum CatalogSourceType: String, Codable, CaseIterable, Sendable {
    case m3uFile = "m3u-file"
    case m3uURL = "m3u-url"
    case xtream
    case plex
    case emby
    case generated
}

public struct CatalogSource: Codable, Equatable, Sendable, Identifiable {
    public let id: String
    public let name: String
    public let type: CatalogSourceType
    public let displayLocation: String
    public let refreshOnLaunch: Bool

    public init(
        id: String,
        name: String,
        type: CatalogSourceType,
        displayLocation: String,
        refreshOnLaunch: Bool
    ) {
        self.id = id
        self.name = name
        self.type = type
        self.displayLocation = displayLocation
        self.refreshOnLaunch = refreshOnLaunch
    }
}

public struct StreamDescriptor: Codable, Equatable, Sendable {
    public let uri: String
    public let requestHeaders: [String: String]

    public init(uri: String, requestHeaders: [String: String] = [:]) {
        self.uri = uri
        self.requestHeaders = requestHeaders
    }
}

public struct GuideMetadata: Codable, Equatable, Sendable {
    public let tvgId: String?
    public let tvgName: String?
    public let logoUri: String?

    public init(tvgId: String? = nil, tvgName: String? = nil, logoUri: String? = nil) {
        self.tvgId = tvgId
        self.tvgName = tvgName
        self.logoUri = logoUri
    }

    public var isEmpty: Bool {
        tvgId == nil && tvgName == nil && logoUri == nil
    }
}

public struct CatchupMetadata: Codable, Equatable, Sendable {
    public let mode: String
    public let source: String
    public let days: Int
    public let correctionMinutes: Int

    public init(mode: String, source: String, days: Int, correctionMinutes: Int) {
        self.mode = mode
        self.source = source
        self.days = days
        self.correctionMinutes = correctionMinutes
    }
}

public struct CatalogMediaMetadata: Codable, Equatable, Sendable {
    public let libraryId: String?
    public let libraryTitle: String?
    public let seriesTitle: String?
    public let seasonNumber: Int?
    public let episodeNumber: Int?
    public let year: Int?
    public let durationMs: Int?
    public let resumePositionMs: Int?
    public let played: Bool?
    public let addedAt: String?
    public let lastPlayedAt: String?

    public init(
        libraryId: String? = nil,
        libraryTitle: String? = nil,
        seriesTitle: String? = nil,
        seasonNumber: Int? = nil,
        episodeNumber: Int? = nil,
        year: Int? = nil,
        durationMs: Int? = nil,
        resumePositionMs: Int? = nil,
        played: Bool? = nil,
        addedAt: String? = nil,
        lastPlayedAt: String? = nil
    ) {
        self.libraryId = libraryId
        self.libraryTitle = libraryTitle
        self.seriesTitle = seriesTitle
        self.seasonNumber = seasonNumber
        self.episodeNumber = episodeNumber
        self.year = year
        self.durationMs = durationMs
        self.resumePositionMs = resumePositionMs
        self.played = played
        self.addedAt = addedAt
        self.lastPlayedAt = lastPlayedAt
    }
}

public struct CatalogChannel: Codable, Equatable, Sendable, Identifiable {
    public let id: String
    public let number: Int
    public let name: String
    public let group: String
    public let kind: ChannelKind
    public let sourceId: String
    public let stream: StreamDescriptor
    public let guide: GuideMetadata?
    public let catchup: CatchupMetadata?
    public let tags: [String]?
    public let media: CatalogMediaMetadata?

    public init(
        id: String,
        number: Int,
        name: String,
        group: String,
        kind: ChannelKind,
        sourceId: String,
        stream: StreamDescriptor,
        guide: GuideMetadata? = nil,
        catchup: CatchupMetadata? = nil,
        tags: [String]? = nil,
        media: CatalogMediaMetadata? = nil
    ) {
        self.id = id
        self.number = number
        self.name = name
        self.group = group
        self.kind = kind
        self.sourceId = sourceId
        self.stream = stream
        self.guide = guide
        self.catchup = catchup
        self.tags = tags
        self.media = media
    }

    public var initials: String {
        let words = name.split(whereSeparator: \.isWhitespace)
        if words.isEmpty { return "TV" }
        if words.count == 1 { return String(words[0].prefix(2)).uppercased() }
        return words.prefix(2).compactMap(\.first).map(String.init).joined().uppercased()
    }

    public var searchableText: String {
        [
            name,
            group,
            guide?.tvgName ?? "",
            media?.libraryTitle ?? "",
            media?.seriesTitle ?? "",
            media?.year.map(String.init) ?? ""
        ].joined(separator: "\n").uppercased()
    }
}

public struct OrbitalVueCatalog: Codable, Equatable, Sendable {
    public let contractVersion: String
    public let catalogId: String
    public let displayName: String
    public let loadedAt: String
    public let sources: [CatalogSource]
    public let guideSources: [String]
    public let channels: [CatalogChannel]

    public init(
        contractVersion: String = orbitalVueCatalogContractVersion,
        catalogId: String,
        displayName: String,
        loadedAt: String,
        sources: [CatalogSource],
        guideSources: [String],
        channels: [CatalogChannel]
    ) {
        self.contractVersion = contractVersion
        self.catalogId = catalogId
        self.displayName = displayName
        self.loadedAt = loadedAt
        self.sources = sources
        self.guideSources = guideSources
        self.channels = channels
    }

    public var groups: [ChannelGroup] {
        var positions: [String: Int] = [:]
        var values: [ChannelGroup] = []
        for channel in channels {
            if let index = positions[channel.group] {
                values[index].count += 1
            } else {
                positions[channel.group] = values.count
                values.append(ChannelGroup(name: channel.group, count: 1))
            }
        }
        return values
    }
}

public struct ChannelGroup: Equatable, Sendable, Identifiable {
    public let name: String
    public var count: Int
    public var id: String { name }

    public init(name: String, count: Int) {
        self.name = name
        self.count = count
    }
}

public struct ParsedPlaylist: Equatable, Sendable {
    public let channels: [CatalogChannel]
    public let guideSources: [String]

    public init(channels: [CatalogChannel], guideSources: [String]) {
        self.channels = channels
        self.guideSources = guideSources
    }
}

public struct LoadedCatalog: Equatable, Sendable {
    public let catalog: OrbitalVueCatalog
    public let notice: String?
    public let usedCachedFallback: Bool

    public init(catalog: OrbitalVueCatalog, notice: String? = nil, usedCachedFallback: Bool = false) {
        self.catalog = catalog
        self.notice = notice
        self.usedCachedFallback = usedCachedFallback
    }
}
