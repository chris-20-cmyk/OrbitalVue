import Foundation
import Testing
@testable import StreamVueCore

@Suite("Media library editorial browsing")
struct MediaLibraryBrowsePolicyTests {
    @Test("Matches the shared resume and recency boundaries")
    func boundaries() throws {
        let now = try #require(ISO8601DateFormatter().date(from: "2026-09-01T12:00:00Z"))
        let resumable = channel(
            number: 1,
            duration: 3_600_000,
            position: 600_000,
            addedAt: "2026-08-20T12:00:00Z"
        )
        let nearBeginning = channel(number: 2, duration: 3_600_000, position: 29_999)
        let nearEnd = channel(number: 3, duration: 3_600_000, position: 3_570_001)
        let old = channel(number: 4, duration: 3_600_000, position: 0, addedAt: "2026-07-01T12:00:00Z")

        #expect(resumable.canResume)
        #expect(!nearBeginning.canResume)
        #expect(!nearEnd.canResume)
        #expect(MediaLibraryBrowsePolicy.matches(resumable, mode: .recentlyAdded, now: now))
        #expect(!MediaLibraryBrowsePolicy.matches(old, mode: .recentlyAdded, now: now))
    }

    @Test("Counts and orders editorial rows")
    func countsAndOrdering() throws {
        let now = try #require(ISO8601DateFormatter().date(from: "2026-09-01T12:00:00Z"))
        let olderResume = channel(
            number: 1,
            duration: 3_600_000,
            position: 900_000,
            lastPlayedAt: "2026-08-30T12:00:00Z"
        )
        let newestResume = channel(
            number: 2,
            duration: 3_600_000,
            position: 300_000,
            lastPlayedAt: "2026-09-01T11:00:00Z"
        )
        let recentMovie = channel(
            number: 3,
            kind: .movie,
            duration: 7_200_000,
            position: 0,
            addedAt: "2026-08-31T12:00:00Z"
        )
        let channels = [olderResume, recentMovie, newestResume]
        let summary = MediaLibraryBrowseSummary(channels: channels, now: now)

        #expect(summary.all == 3)
        #expect(summary.continueWatching == 2)
        #expect(summary.recentlyAdded == 1)
        #expect(summary.movies == 1)
        #expect(MediaLibraryBrowsePolicy.ordered(channels, mode: .continueWatching, now: now).map(\.number) == [2, 1])
    }

    private func channel(
        number: Int,
        kind: ChannelKind = .series,
        duration: Int,
        position: Int,
        addedAt: String? = nil,
        lastPlayedAt: String? = nil
    ) -> CatalogChannel {
        CatalogChannel(
            id: "media-\(number)",
            number: number,
            name: "Item \(number)",
            group: "Library",
            kind: kind,
            sourceId: "source",
            stream: StreamDescriptor(uri: "streamvue-media://plex/server/item-\(number)"),
            tags: ["media-center"],
            media: CatalogMediaMetadata(
                libraryId: "library",
                libraryTitle: "Library",
                durationMs: duration,
                resumePositionMs: position,
                played: false,
                addedAt: addedAt,
                lastPlayedAt: lastPlayedAt
            )
        )
    }
}
