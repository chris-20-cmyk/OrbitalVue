package com.orbitalvue.player.data

import android.content.ContextWrapper
import com.google.gson.Gson
import com.google.gson.JsonParser
import com.orbitalvue.player.premium.PremiumAccessPolicy
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.net.URI
import java.nio.file.Files

class MediaCenterSecurityTest {
    @Test
    fun `http requires explicit consent and provider paths stay on the verified origin`() {
        val base = MediaCenterUrlPolicy.normalizeBaseUrl("http://192.168.1.20:32400")
        assertThrows(IllegalStateException::class.java) {
            MediaCenterUrlPolicy.requireAllowedTransport(base, allowInsecureHttp = false)
        }
        MediaCenterUrlPolicy.requireAllowedTransport(base, allowInsecureHttp = true)

        assertThrows(IllegalArgumentException::class.java) {
            MediaCenterUrlPolicy.resolveServerPath(base, "https://attacker.invalid/video.m3u8")
        }
        val safe = MediaCenterUrlPolicy.resolveServerPath(
            base,
            "/library/parts/1/file.ts?X-Plex-Token=blocked&quality=original"
        )
        assertEquals("http://192.168.1.20:32400/library/parts/1/file.ts?quality=original", safe.toASCIIString())
    }

    @Test
    fun `internal playback locator round trips without exposing server identifiers`() {
        val raw = MediaCenterLocator.playbackUri(MediaCenterProvider.Plex, "server-123", "item-456")
        assertFalse(raw.contains("server-123"))
        assertFalse(raw.contains("item-456"))
        assertEquals(
            MediaCenterPlaybackLocator(MediaCenterProvider.Plex, "server-123", "item-456"),
            MediaCenterLocator.parsePlaybackUri(raw)
        )
    }

    @Test
    fun `plex probes identity without credentials before every protected request`() {
        val vault = MemoryVault()
        val transport = PlexFixtureTransport()
        val service = MediaCenterService(
            transport = transport,
            credentialVault = vault,
            device = testDevice,
            gson = Gson()
        )
        val token = "server-secret-token"
        val connection = service.connectPlex("https://plex.example:32400", token)
        val snapshot = service.snapshot(connection)
        val encoded = Gson().toJson(snapshot)

        assertEquals(1, snapshot.items.size)
        assertFalse(encoded.contains(token))
        assertFalse(encoded.contains("X-Plex-Token", ignoreCase = true))
        assertTrue(transport.requests.filter { it.url.path == "/identity" }.all {
            it.headers.keys.none { name -> name.equals("X-Plex-Token", true) }
        })
        assertTrue(transport.requests.filter { it.url.path != "/identity" }.all {
            it.headers["X-Plex-Token"] == token
        })

        val plan = service.playbackPlan(connection, snapshot.items.single())
        assertEquals(token, plan.requestHeaders["X-Plex-Token"])
        assertFalse(plan.url.contains(token))
        service.reportPlayback(
            connection,
            plan,
            MediaCenterPlaybackReport(
                kind = MediaCenterPlaybackReportKind.Started,
                state = MediaCenterPlaybackState.Playing,
                positionMs = 1_000,
                durationMs = 120_000
            )
        )
        service.reportPlayback(
            connection,
            plan,
            MediaCenterPlaybackReport(
                kind = MediaCenterPlaybackReportKind.Stopped,
                state = MediaCenterPlaybackState.Playing,
                positionMs = 130_000,
                durationMs = 120_000
            )
        )
        val reports = transport.requests.filter { it.url.path == "/:/timeline" }
        assertEquals(listOf("playing", "stopped"), reports.map { queryValue(it.url, "state") })
        assertEquals(listOf("1000", "120000"), reports.map { queryValue(it.url, "time") })
        assertTrue(reports.all { it.headers["X-Plex-Session-Identifier"] == plan.playSessionId })
        assertTrue(reports.none { it.url.toASCIIString().contains(token) })
    }

    @Test
    fun `emby playback reports preserve session ids and clamp provider payloads`() {
        val transport = EmbyFixtureTransport()
        val service = MediaCenterService(transport, MemoryVault(), testDevice, Gson())
        val connection = service.connectEmby(
            "https://emby.example",
            "chris",
            "fixture-password"
        )
        val snapshot = service.snapshot(connection)
        val plan = service.playbackPlan(connection, snapshot.items.single())

        service.reportPlayback(
            connection,
            plan,
            MediaCenterPlaybackReport(
                kind = MediaCenterPlaybackReportKind.Started,
                state = MediaCenterPlaybackState.Playing,
                positionMs = 1_200,
                durationMs = 3_600_000,
                volumePercent = 101
            )
        )
        service.reportPlayback(
            connection,
            plan,
            MediaCenterPlaybackReport(
                kind = MediaCenterPlaybackReportKind.Progress,
                state = MediaCenterPlaybackState.Paused,
                positionMs = 4_000_000,
                durationMs = 3_600_000,
                event = MediaCenterPlaybackEvent.Pause
            )
        )
        service.reportPlayback(
            connection,
            plan,
            MediaCenterPlaybackReport(
                kind = MediaCenterPlaybackReportKind.Stopped,
                state = MediaCenterPlaybackState.Playing,
                positionMs = -1
            )
        )

        val reports = transport.requests.filter { it.url.path.startsWith("/emby/Sessions/Playing") }
        assertEquals(
            listOf(
                "/emby/Sessions/Playing",
                "/emby/Sessions/Playing/Progress",
                "/emby/Sessions/Playing/Stopped"
            ),
            reports.map { it.url.path }
        )
        val started = JsonParser.parseString(reports[0].body!!.toString(Charsets.UTF_8)).asJsonObject
        val paused = JsonParser.parseString(reports[1].body!!.toString(Charsets.UTF_8)).asJsonObject
        val stopped = JsonParser.parseString(reports[2].body!!.toString(Charsets.UTF_8)).asJsonObject
        assertEquals("play-session-1", started.get("PlaySessionId").asString)
        assertEquals("live-stream-1", started.get("LiveStreamId").asString)
        assertEquals(12_000_000L, started.get("PositionTicks").asLong)
        assertEquals(100, started.get("VolumeLevel").asInt)
        assertEquals(36_000_000_000L, paused.get("PositionTicks").asLong)
        assertEquals("Pause", paused.get("EventName").asString)
        assertEquals(0L, stopped.get("PositionTicks").asLong)
        assertTrue(reports.all { it.headers["X-Emby-Token"] == EmbyFixtureTransport.TOKEN })
        assertTrue(reports.none { it.url.toASCIIString().contains(EmbyFixtureTransport.TOKEN) })
    }

    @Test
    fun `changed plex identity blocks the token from the next request`() {
        val vault = MemoryVault()
        val transport = PlexFixtureTransport()
        val service = MediaCenterService(transport, vault, testDevice, Gson())
        val connection = service.connectPlex("https://plex.example:32400", "protected-token")
        transport.serverId = "different-server"
        val protectedBefore = transport.protectedRequestCount

        assertThrows(IllegalArgumentException::class.java) {
            service.snapshot(connection)
        }
        assertEquals(protectedBefore, transport.protectedRequestCount)
        assertEquals("/identity", transport.requests.last().url.path)
    }

    @Test
    fun `locked store repository rejects a connection before any network request`() {
        val directory = Files.createTempDirectory("orbitalvue-premium-access-test").toFile()
        try {
            val context = object : ContextWrapper(null) {
                override fun getFilesDir() = directory
            }
            val vault = MemoryVault()
            val transport = PlexFixtureTransport()
            val service = MediaCenterService(transport, vault, testDevice, Gson())
            val repository = MediaCenterRepository(
                context = context,
                gson = Gson(),
                service = service,
                premiumAccess = PremiumAccessPolicy.evaluate("store", false)
            )

            assertThrows(IllegalStateException::class.java) {
                runBlocking {
                    repository.connectPlex(
                        serverAddress = "https://plex.example:32400",
                        token = "must-never-leave-this-test"
                    )
                }
            }
            assertTrue(transport.requests.isEmpty())
        } finally {
            directory.deleteRecursively()
        }
    }

    @Test
    fun `verified runtime purchase unlocks an existing repository`() {
        val directory = Files.createTempDirectory("orbitalvue-premium-runtime-test").toFile()
        try {
            val context = object : ContextWrapper(null) {
                override fun getFilesDir() = directory
            }
            val transport = PlexFixtureTransport()
            var access = PremiumAccessPolicy.evaluate("store", false)
            val repository = MediaCenterRepository(
                context = context,
                gson = Gson(),
                service = MediaCenterService(transport, MemoryVault(), testDevice, Gson()),
                premiumAccessProvider = { access }
            )

            assertThrows(IllegalStateException::class.java) {
                runBlocking {
                    repository.connectPlex("https://plex.example:32400", "runtime-token")
                }
            }
            assertTrue(transport.requests.isEmpty())

            access = PremiumAccessPolicy.evaluate(
                "store",
                true,
                "com.orbitalvue.personal-media-centers"
            )
            val loaded = runBlocking {
                repository.connectPlex("https://plex.example:32400", "runtime-token")
            }
            assertEquals(1, loaded.catalog.channels.size)
            assertTrue(transport.requests.isNotEmpty())
        } finally {
            directory.deleteRecursively()
        }
    }

    private class MemoryVault : MediaCenterCredentialVault {
        private val values = HashMap<String, String>()
        override fun save(id: String, value: String) { values[id] = value }
        override fun read(id: String): String? = values[id]
        override fun remove(id: String) { values.remove(id) }
    }

    private class PlexFixtureTransport : MediaCenterTransport {
        val requests = ArrayList<MediaHttpRequest>()
        var serverId = "plex-server"
        var protectedRequestCount = 0

        override fun execute(request: MediaHttpRequest): MediaHttpResponse {
            requests += request
            if (request.headers.keys.any { it.equals("X-Plex-Token", true) }) {
                protectedRequestCount += 1
            }
            val body = when (request.url.path) {
                "/identity" -> """{"MediaContainer":{"machineIdentifier":"$serverId","friendlyName":"Living Room Plex"}}"""
                "/library/sections" -> """{"MediaContainer":{"Directory":[{"key":"1","title":"Movies","type":"movie","totalSize":1}]}}"""
                "/library/sections/1/all" -> """
                    {"MediaContainer":{"offset":0,"totalSize":1,"Metadata":[{
                      "ratingKey":"movie-1","title":"Fixture Movie","type":"movie","year":2026,
                      "thumb":"/library/metadata/movie-1/thumb?X-Plex-Token=blocked",
                      "Media":[{"id":"media-1","container":"mkv","videoCodec":"hevc","audioCodec":"eac3",
                        "Part":[{"id":"part-1","key":"/library/parts/part-1/file.mkv?X-Plex-Token=blocked"}]}]
                    }]}}
                """.trimIndent()
                "/:/timeline" -> ""
                else -> error("Unexpected fixture request: ${request.url.path}")
            }
            return MediaHttpResponse(200, body.toByteArray(Charsets.UTF_8))
        }
    }

    private class EmbyFixtureTransport : MediaCenterTransport {
        val requests = ArrayList<MediaHttpRequest>()

        override fun execute(request: MediaHttpRequest): MediaHttpResponse {
            requests += request
            val body = when (request.url.path) {
                "/emby/System/Info/Public" ->
                    """{"Id":"emby-server","ServerName":"Fixture Emby"}"""
                "/emby/Users/AuthenticateByName" ->
                    """{"AccessToken":"$TOKEN","ServerId":"emby-server","User":{"Id":"user-1","Name":"Chris"}}"""
                "/emby/Users/user-1/Views" ->
                    """{"Items":[{"Id":"movies","Name":"Movies","CollectionType":"movies","ChildCount":1}]}"""
                "/emby/Users/user-1/Items" -> """
                    {"TotalRecordCount":1,"Items":[{
                      "Id":"item-1","Name":"Fixture Movie","Type":"Movie","RunTimeTicks":36000000000,
                      "UserData":{"PlaybackPositionTicks":12000000,"Played":false},
                      "MediaSources":[{"Id":"source-1","Container":"mkv","SupportsDirectPlay":true,
                        "SupportsDirectStream":true,"SupportsTranscoding":true,"MediaStreams":[]}]
                    }]}
                """.trimIndent()
                "/emby/Items/item-1/PlaybackInfo" -> """
                    {"PlaySessionId":"play-session-1","MediaSources":[{
                      "Id":"source-1","Container":"mkv","SupportsDirectPlay":true,
                      "SupportsDirectStream":true,"SupportsTranscoding":true,
                      "DirectStreamUrl":"/Videos/item-1/stream.mkv","LiveStreamId":"live-stream-1"
                    }]}
                """.trimIndent()
                "/emby/Sessions/Playing",
                "/emby/Sessions/Playing/Progress",
                "/emby/Sessions/Playing/Stopped" -> ""
                else -> error("Unexpected Emby fixture request: ${request.url.path}")
            }
            return MediaHttpResponse(200, body.toByteArray(Charsets.UTF_8))
        }

        companion object {
            const val TOKEN = "emby-fixture-token"
        }
    }

    private fun queryValue(uri: URI, name: String): String? = uri.rawQuery
        ?.split('&')
        ?.mapNotNull { value -> value.split('=', limit = 2).takeIf { it.size == 2 } }
        ?.firstOrNull { it[0] == name }
        ?.get(1)

    private companion object {
        val testDevice = MediaCenterDeviceIdentity(
            client = "OrbitalVue",
            device = "Android Test",
            deviceId = "android-test-device",
            version = "5.7.0"
        )
    }
}
