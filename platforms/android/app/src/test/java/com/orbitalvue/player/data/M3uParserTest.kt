package com.orbitalvue.player.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class M3uParserTest {
    private val fixture: String
        get() = requireNotNull(javaClass.classLoader?.getResource("iptv-features.m3u"))
            .readText(Charsets.UTF_8)

    @Test
    fun parsesPortableFixtureWithoutFlatteningGroups() {
        val result = M3uParser.parse(fixture, "fixture-source", "IPTV feature fixture")

        assertEquals(3, result.channels.size)
        assertEquals(listOf("https://guide.example.invalid/us.xml"), result.guideSources)
        assertEquals(listOf("News", "Sports | Football", "Cinema"), result.channels.map(Channel::group))
        assertEquals(listOf(ChannelKind.Live, ChannelKind.Live, ChannelKind.Movie), result.channels.map(Channel::kind))
    }

    @Test
    fun preservesPlaybackHeadersAndCatchupMetadata() {
        val channel = M3uParser.parse(fixture, "fixture-source", "fixture").channels.first()

        assertEquals("FixturePlayer/1.0", channel.requestHeaders["User-Agent"])
        assertEquals("https://portal.example.invalid/", channel.requestHeaders["Referer"])
        assertEquals("append", channel.catchup?.mode)
        assertEquals("?utc={utc}", channel.catchup?.source)
        assertEquals(7, channel.catchup?.days)
        assertEquals(90, channel.catchup?.correctionMinutes)
    }

    @Test
    fun stableIdsMatchTheWindowsIdentityContract() {
        val channels = M3uParser.parse(fixture, "fixture-source", "fixture").channels

        assertEquals(
            "E7D6336BB7664A5E3A6156542C52F350E2EDF5323F207676CE39AAFEA3F44CC1",
            channels[0].id
        )
        assertEquals(
            "7617D47CBA0D76A3562D704627314962032367602167F66C273AE06BC3B1AD07",
            channels[1].id
        )
        assertEquals(
            "25B453162D2C91E47ECE63690AEE810CAA87EE5DA2AC1E6C27A80DE3D4354568",
            channels[2].id
        )
    }

    @Test
    fun acceptsAPlayableEntryWithoutExtInfMetadata() {
        val result = M3uParser.parse(
            "#EXTM3U\nhttps://stream.example.invalid/live/raw.m3u8\n",
            "raw-source",
            "Raw"
        )

        assertEquals("Channel 1", result.channels.single().name)
        assertEquals("Uncategorized", result.channels.single().group)
        assertTrue(result.channels.single().id.matches(Regex("[A-F0-9]{64}")))
    }

    @Test
    fun rejectsAFileWithNoPlayableEntries() {
        assertThrows(IllegalArgumentException::class.java) {
            M3uParser.parse("#EXTM3U\n# no streams", "empty-source", "Empty")
        }
    }

    @Test
    fun preservesMultipleGuideSourcesAndClampsCatchupBounds() {
        val playlist = """
            #EXTM3U url-tvg="https://one.example.invalid/guide.xml, https://two.example.invalid/guide.xml"
            #EXTINF:-1 catchup="append" catchup-source="?utc={utc}" catchup-days="999" catchup-correction="-99",One
            https://stream.example.invalid/live/one.m3u8
        """.trimIndent()

        val result = M3uParser.parse(playlist, "source", "Source")

        assertEquals(
            listOf("https://one.example.invalid/guide.xml", "https://two.example.invalid/guide.xml"),
            result.guideSources
        )
        assertEquals(365, result.channels.single().catchup?.days)
        assertEquals(-1_440, result.channels.single().catchup?.correctionMinutes)
    }

    @Test
    fun enforcesThePortableChannelSafetyLimit() {
        val playlist = """
            #EXTM3U
            #EXTINF:-1,One
            https://one.example.invalid/live
            #EXTINF:-1,Two
            https://two.example.invalid/live
        """.trimIndent()

        assertThrows(IllegalArgumentException::class.java) {
            M3uParser.parse(playlist, "source", "Too many", maximumChannels = 1)
        }
    }
}
