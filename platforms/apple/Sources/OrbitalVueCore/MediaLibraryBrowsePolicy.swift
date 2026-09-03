import Foundation

public enum MediaLibraryBrowseMode: String, CaseIterable, Hashable, Identifiable, Sendable {
    case all
    case continueWatching = "continue-watching"
    case recentlyAdded = "recently-added"
    case live
    case movies
    case series

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .all: "All"
        case .continueWatching: "Continue"
        case .recentlyAdded: "Recent"
        case .live: "Live"
        case .movies: "Movies"
        case .series: "Series"
        }
    }

    public var sectionTitle: String {
        switch self {
        case .continueWatching: "Continue Watching"
        case .recentlyAdded: "Recently Added"
        default: title
        }
    }

    public var systemImage: String {
        switch self {
        case .all: "rectangle.stack"
        case .continueWatching: "play.circle"
        case .recentlyAdded: "clock.badge.plus"
        case .live: "dot.radiowaves.left.and.right"
        case .movies: "film"
        case .series: "tv"
        }
    }
}

public struct MediaLibraryBrowseSummary: Equatable, Sendable {
    public let all: Int
    public let continueWatching: Int
    public let recentlyAdded: Int
    public let live: Int
    public let movies: Int
    public let series: Int

    public init(channels: [CatalogChannel], now: Date = Date()) {
        all = channels.filter(\.isMediaCenterItem).count
        continueWatching = channels.filter { MediaLibraryBrowsePolicy.matches($0, mode: .continueWatching, now: now) }.count
        recentlyAdded = channels.filter { MediaLibraryBrowsePolicy.matches($0, mode: .recentlyAdded, now: now) }.count
        live = channels.filter { MediaLibraryBrowsePolicy.matches($0, mode: .live, now: now) }.count
        movies = channels.filter { MediaLibraryBrowsePolicy.matches($0, mode: .movies, now: now) }.count
        series = channels.filter { MediaLibraryBrowsePolicy.matches($0, mode: .series, now: now) }.count
    }

    public func count(for mode: MediaLibraryBrowseMode) -> Int {
        switch mode {
        case .all: all
        case .continueWatching: continueWatching
        case .recentlyAdded: recentlyAdded
        case .live: live
        case .movies: movies
        case .series: series
        }
    }
}

public enum MediaLibraryBrowsePolicy {
    public static let recentWindow: TimeInterval = 30 * 24 * 60 * 60
    public static let futureClockAllowance: TimeInterval = 24 * 60 * 60
    public static let resumeEdgeMilliseconds = 30_000

    public static func matches(
        _ channel: CatalogChannel,
        mode: MediaLibraryBrowseMode,
        now: Date = Date()
    ) -> Bool {
        guard channel.isMediaCenterItem else { return false }
        switch mode {
        case .all: return true
        case .continueWatching: return channel.canResume
        case .recentlyAdded:
            guard let addedAt = channel.media?.addedAt.flatMap(parseDate) else { return false }
            return addedAt >= now.addingTimeInterval(-recentWindow)
                && addedAt <= now.addingTimeInterval(futureClockAllowance)
        case .live: return channel.kind == .live
        case .movies: return channel.kind == .movie
        case .series: return channel.kind == .series
        }
    }

    public static func ordered(
        _ channels: [CatalogChannel],
        mode: MediaLibraryBrowseMode,
        now: Date = Date()
    ) -> [CatalogChannel] {
        let matching = channels.filter { matches($0, mode: mode, now: now) }
        switch mode {
        case .continueWatching:
            return matching.sorted {
                let leftDate = $0.media?.lastPlayedAt.flatMap(parseDate) ?? .distantPast
                let rightDate = $1.media?.lastPlayedAt.flatMap(parseDate) ?? .distantPast
                if leftDate != rightDate { return leftDate > rightDate }
                let leftPosition = $0.media?.resumePositionMs ?? 0
                let rightPosition = $1.media?.resumePositionMs ?? 0
                return leftPosition == rightPosition ? $0.number < $1.number : leftPosition > rightPosition
            }
        case .recentlyAdded:
            return matching.sorted {
                let leftDate = $0.media?.addedAt.flatMap(parseDate) ?? .distantPast
                let rightDate = $1.media?.addedAt.flatMap(parseDate) ?? .distantPast
                return leftDate == rightDate ? $0.number < $1.number : leftDate > rightDate
            }
        default:
            return matching.sorted { $0.number < $1.number }
        }
    }

    public static func parseDate(_ value: String) -> Date? {
        ISO8601DateFormatter.orbitalVueDate(from: value)
    }
}

public extension CatalogChannel {
    var isMediaCenterItem: Bool {
        tags?.contains("media-center") == true || ["orbitalvue-media"].contains(URL(string: stream.uri)?.scheme?.lowercased() ?? "")
    }

    var canResume: Bool {
        guard isMediaCenterItem,
              let position = media?.resumePositionMs,
              position >= MediaLibraryBrowsePolicy.resumeEdgeMilliseconds else { return false }
        let duration = media?.durationMs ?? 0
        return duration <= 0 || position < duration - MediaLibraryBrowsePolicy.resumeEdgeMilliseconds
    }

    var watchProgress: Double? {
        guard canResume,
              let duration = media?.durationMs,
              let position = media?.resumePositionMs else { return nil }
        guard duration > 0 else { return nil }
        return min(1, max(0, Double(position) / Double(duration)))
    }

    var watchProgressLabel: String? {
        guard let progress = watchProgress else { return nil }
        return "\(Int((progress * 100).rounded()))% watched"
    }

    var mediaMetadataLine: String? {
        guard let media else { return nil }
        var values: [String] = []
        if let series = media.seriesTitle { values.append(series) }
        if let season = media.seasonNumber, let episode = media.episodeNumber {
            values.append(String(format: "S%02dE%02d", season, episode))
        }
        if let year = media.year { values.append(String(year)) }
        if values.isEmpty, let library = media.libraryTitle { values.append(library) }
        return values.isEmpty ? nil : values.joined(separator: " • ")
    }
}

extension ISO8601DateFormatter {
    static func orbitalVueDate(from value: String) -> Date? {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let date = formatter.date(from: value) { return date }
        formatter.formatOptions = [.withInternetDateTime]
        return formatter.date(from: value)
    }

    static func orbitalVueString(from date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: date)
    }
}
