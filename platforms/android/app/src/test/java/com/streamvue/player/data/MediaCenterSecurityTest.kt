package com.streamvue.player.data

import com.google.gson.Gson
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.net.URI

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
                else -> error("Unexpected fixture request: ${request.url.path}")
            }
            return MediaHttpResponse(200, body.toByteArray(Charsets.UTF_8))
        }
    }

    private companion object {
        val testDevice = MediaCenterDeviceIdentity(
            client = "StreamVue",
            device = "Android Test",
            deviceId = "android-test-device",
            version = "5.1.0"
        )
    }
}
