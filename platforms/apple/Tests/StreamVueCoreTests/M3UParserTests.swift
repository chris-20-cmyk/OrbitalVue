import Foundation
import Testing
@testable import StreamVueCore

@Suite("Portable M3U contract")
struct M3UParserTests {
    @Test("Matches the shared catalog fixture exactly")
    func matchesSharedFixture() throws {
        let playlist = try String(contentsOf: fixtureURL("iptv-features.m3u"), encoding: .utf8)
        let expectedData = try Data(contentsOf: fixtureURL("catalog.expected.json"))
        let expected = try JSONDecoder().decode(StreamVueCatalog.self, from: expectedData)
        let parsed = try M3UParser.parse(
            playlist,
            sourceId: "fixture-source",
            sourceName: "IPTV feature fixture"
        )

        #expect(parsed.channels == expected.channels)
        #expect(parsed.guideSources == expected.guideSources)
        #expect(parsed.channels.map(\.group) == ["News", "Sports | Football", "Cinema"])
    }

    @Test("Stable identities ignore rotating query tokens")
    func tokenIndependentIdentity() {
        let first = M3UParser.stableChannelID(
            tvgId: "news.one",
            name: "News One",
            group: "News",
            streamURI: "https://example.invalid/live.m3u8?token=one"
        )
        let second = M3UParser.stableChannelID(
            tvgId: "news.one",
            name: "News One",
            group: "News",
            streamURI: "https://example.invalid/live.m3u8?token=two"
        )
        #expect(first == second)
        #expect(first.count == 64)
    }

    @Test("Parses raw playable entries without EXTINF")
    func parsesRawEntry() throws {
        let parsed = try M3UParser.parse(
            "#EXTM3U\nhttps://stream.example.invalid/live/raw.m3u8\n",
            sourceId: "raw-source",
            sourceName: "Raw"
        )
        #expect(parsed.channels.count == 1)
        #expect(parsed.channels[0].name == "Channel 1")
        #expect(parsed.channels[0].group == "Uncategorized")
    }

    @Test("Quoted commas remain inside metadata values")
    func parsesQuotedComma() throws {
        let parsed = try M3UParser.parse(
            "#EXTM3U\n#EXTINF:-1 group-title=\"News, Local\",City News\nhttps://stream.example.invalid/city.m3u8",
            sourceId: "source",
            sourceName: "Source"
        )
        #expect(parsed.channels[0].group == "News, Local")
        #expect(parsed.channels[0].name == "City News")
    }

    @Test("Parses VLC and EXHTTP request headers")
    func parsesRequestHeaders() throws {
        let playlist = """
        #EXTM3U
        #EXTINF:-1,One
        #EXTVLCOPT:http-user-agent=Agent/1
        #EXTHTTP:{"Referer":"https://portal.example.invalid/"}
        https://stream.example.invalid/one.m3u8
        """
        let parsed = try M3UParser.parse(playlist, sourceId: "source", sourceName: "Source")
        #expect(parsed.channels[0].stream.requestHeaders["User-Agent"] == "Agent/1")
        #expect(parsed.channels[0].stream.requestHeaders["Referer"] == "https://portal.example.invalid/")
    }

    @Test("Preserves every valid guide source in source order")
    func parsesMultipleGuideSources() throws {
        let playlist = """
        #EXTM3U url-tvg="https://one.example.invalid/guide.xml, https://two.example.invalid/guide.xml"
        #EXTINF:-1,One
        https://stream.example.invalid/one.m3u8
        """
        let parsed = try M3UParser.parse(playlist, sourceId: "source", sourceName: "Source")
        #expect(parsed.guideSources == [
            "https://one.example.invalid/guide.xml",
            "https://two.example.invalid/guide.xml"
        ])
    }

    @Test("Infers live, movie, and series kinds")
    func infersKinds() throws {
        let playlist = """
        #EXTM3U
        #EXTINF:-1 group-title="Live",One
        https://stream.example.invalid/live/one.m3u8
        #EXTINF:-1 group-title="Cinema",Two
        https://stream.example.invalid/movie/two.mp4
        #EXTINF:-1 group-title="Shows",Three
        https://stream.example.invalid/series/three.m3u8
        """
        let parsed = try M3UParser.parse(playlist, sourceId: "source", sourceName: "Source")
        #expect(parsed.channels.map(\.kind) == [.live, .movie, .series])
    }

    @Test("Rejects empty playlists")
    func rejectsEmptyPlaylist() {
        #expect(throws: M3UParserError.noPlayableEntries) {
            try M3UParser.parse("#EXTM3U\n# no streams", sourceId: "source", sourceName: "Empty")
        }
    }

    @Test("Enforces the channel safety limit")
    func rejectsTooManyChannels() {
        let playlist = """
        #EXTM3U
        #EXTINF:-1,One
        https://one.example.invalid/live
        #EXTINF:-1,Two
        https://two.example.invalid/live
        """
        #expect(throws: M3UParserError.tooManyChannels(maximum: 1)) {
            try M3UParser.parse(playlist, sourceId: "source", sourceName: "Large", maximumChannels: 1)
        }
    }

    @Test("Enforces the input byte safety limit")
    func rejectsOversizedInput() {
        #expect(throws: M3UParserError.oversizedPlaylist(maximumBytes: 8)) {
            try M3UParser.parse(
                "#EXTM3U\nhttps://one.example.invalid/live",
                sourceId: "source",
                sourceName: "Large",
                maximumBytes: 8
            )
        }
    }

    @Test("Requires source identity")
    func requiresSourceIdentity() {
        #expect(throws: M3UParserError.missingSourceIdentity) {
            try M3UParser.parse("https://one.example.invalid/live", sourceId: "", sourceName: "")
        }
    }

    @Test("Clamps catch-up metadata to contract bounds")
    func clampsCatchupBounds() throws {
        let playlist = """
        #EXTM3U
        #EXTINF:-1 catchup="append" catchup-source="?utc={utc}" catchup-days="999" catchup-correction="99",One
        https://one.example.invalid/live
        """
        let parsed = try M3UParser.parse(playlist, sourceId: "source", sourceName: "Source")
        #expect(parsed.channels[0].catchup?.days == 365)
        #expect(parsed.channels[0].catchup?.correctionMinutes == 1_440)
    }

    @Test("Catalog JSON does not place source secrets in display metadata")
    func catalogSourceIsPrivacySafe() throws {
        let source = CatalogSource(
            id: "source",
            name: "Provider",
            type: .m3uURL,
            displayLocation: "provider.example.invalid:8443",
            refreshOnLaunch: true
        )
        let catalog = try StreamVueCatalogFactory.create(
            fromM3U: "#EXTM3U\nhttps://stream.example.invalid/live.m3u8?token=fixture",
            catalogId: "catalog",
            displayName: "Provider",
            source: source,
            loadedAt: Date(timeIntervalSince1970: 0)
        )
        let json = String(decoding: try JSONEncoder().encode(catalog.sources), as: UTF8.self)
        #expect(!json.contains("token=fixture"))
        #expect(!json.contains("username"))
    }

    private func fixtureURL(_ name: String) -> URL {
        repositoryRoot()
            .appendingPathComponent("contracts/fixtures")
            .appendingPathComponent(name)
    }

    private func repositoryRoot() -> URL {
        var url = URL(fileURLWithPath: #filePath)
        for _ in 0..<5 { url.deleteLastPathComponent() }
        return url
    }
}
