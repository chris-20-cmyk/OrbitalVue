package com.orbitalvue.player.data

import com.google.gson.Gson
import com.google.gson.JsonObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant
import java.util.Collections
import java.util.concurrent.CancellationException
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference
import kotlin.concurrent.thread

class PlexDiscoverySecurityTest {
    @Test
    fun `signed discovery exposes no token and persists only the selected server token`() {
        val fixture = PlexDiscoveryFixture()
        val vault = MemoryVault()
        val service = service(fixture, vault)

        val discovery = completeDiscovery(service)
        val encodedDiscovery = Gson().toJson(discovery)

        assertFalse(encodedDiscovery.contains("account-token"))
        assertFalse(encodedDiscovery.contains("server-token"))
        assertEquals("https://plex.local:32400", discovery.servers.single().preferredConnection?.url)

        val connection = service.connectDiscoveredPlexServer(
            sessionId = discovery.sessionId,
            serverId = "server-1",
            connectionUrl = "https://plex.local:32400"
        )

        assertEquals("server-1", connection.serverId)
        assertTrue(vault.read(connection.credentialId).orEmpty().contains("\"value\":\"server-token\""))
        assertTrue(vault.values.values.none { it.contains("account-token") })
    }

    @Test
    fun `unlisted connection is rejected before a server request or credential write`() {
        val fixture = PlexDiscoveryFixture()
        val vault = MemoryVault()
        val service = service(fixture, vault)
        val discovery = completeDiscovery(service)
        val requestsBeforeSelection = fixture.requests.size

        assertThrows(IllegalStateException::class.java) {
            service.connectDiscoveredPlexServer(
                discovery.sessionId,
                "server-1",
                "https://attacker.invalid:32400"
            )
        }

        assertEquals(requestsBeforeSelection, fixture.requests.size)
        assertTrue(vault.values.isEmpty())
    }

    @Test
    fun `changed server identity is rejected before its token is stored`() {
        val fixture = PlexDiscoveryFixture()
        val vault = MemoryVault()
        val service = service(fixture, vault)
        val discovery = completeDiscovery(service)
        fixture.serverId = "substituted-server"

        assertThrows(IllegalArgumentException::class.java) {
            service.connectDiscoveredPlexServer(
                discovery.sessionId,
                "server-1",
                "https://plex.local:32400"
            )
        }

        assertEquals("/identity", fixture.requests.last().url.path)
        assertTrue(fixture.requests.last().headers.keys.none { it.equals("X-Plex-Token", true) })
        assertTrue(vault.values.isEmpty())
    }

    @Test
    fun `http requires consent and the same discovery can recover after denial`() {
        val fixture = PlexDiscoveryFixture()
        val vault = MemoryVault()
        val service = service(fixture, vault)
        val discovery = completeDiscovery(service)

        assertThrows(IllegalArgumentException::class.java) {
            service.connectDiscoveredPlexServer(
                discovery.sessionId,
                "server-1",
                "http://192.168.1.20:32400"
            )
        }
        assertTrue(vault.values.isEmpty())

        val connection = service.connectDiscoveredPlexServer(
            discovery.sessionId,
            "server-1",
            "http://192.168.1.20:32400",
            allowInsecureHttp = true
        )
        assertEquals("http://192.168.1.20:32400", connection.baseUrl)
        assertTrue(vault.read(connection.credentialId).orEmpty().contains("\"value\":\"server-token\""))
    }

    @Test
    fun `cancelling an in-flight selection removes its newly stored credential`() {
        val fixture = PlexDiscoveryFixture(blockServerIdentity = true)
        val vault = MemoryVault()
        val service = service(fixture, vault)
        val discovery = completeDiscovery(service)
        val failure = AtomicReference<Throwable?>()

        val worker = thread(name = "plex-discovery-cancellation") {
            try {
                service.connectDiscoveredPlexServer(
                    discovery.sessionId,
                    "server-1",
                    "https://plex.local:32400"
                )
            } catch (error: Throwable) {
                failure.set(error)
            }
        }

        assertTrue(fixture.identityEntered.await(2, TimeUnit.SECONDS))
        service.cancelPlexDiscovery(discovery.sessionId)
        fixture.releaseIdentity.countDown()
        worker.join(2_000)

        assertFalse(worker.isAlive)
        assertNotNull(failure.get())
        assertTrue(failure.get() is CancellationException)
        assertTrue(vault.values.isEmpty())
    }

    private fun completeDiscovery(service: MediaCenterService): PlexServerDiscovery {
        val challenge = service.createPlexSignInChallenge()
        return service.completePlexSignIn(challenge)
            ?: error("The fixture should complete Plex sign-in immediately.")
    }

    private fun service(
        fixture: PlexDiscoveryFixture,
        vault: MemoryVault
    ): MediaCenterService = MediaCenterService(
        transport = fixture,
        credentialVault = vault,
        device = testDevice,
        gson = Gson(),
        plexAccountClient = PlexAccountClient(
            transport = fixture,
            signer = FakeSigner(),
            device = testDevice,
            now = Instant::now
        )
    )

    private class MemoryVault : MediaCenterCredentialVault {
        val values = HashMap<String, String>()
        override fun save(id: String, value: String) { values[id] = value }
        override fun read(id: String): String? = values[id]
        override fun remove(id: String) { values.remove(id) }
    }

    private class FakeSigner : PlexDeviceSigner {
        override val publicJwk = JsonObject().apply {
            addProperty("kty", "OKP")
            addProperty("crv", "Ed25519")
            addProperty("x", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")
            addProperty("kid", "plex-device-key")
            addProperty("alg", "EdDSA")
        }

        override fun sign(claims: Map<String, Any>): String = "header.payload.signature"
    }

    private class PlexDiscoveryFixture(
        private val blockServerIdentity: Boolean = false
    ) : MediaCenterTransport {
        val requests: MutableList<MediaHttpRequest> = Collections.synchronizedList(ArrayList())
        val identityEntered = CountDownLatch(1)
        val releaseIdentity = CountDownLatch(1)
        var serverId = "server-1"

        override fun execute(request: MediaHttpRequest): MediaHttpResponse {
            requests += request
            val body = when {
                request.url.host == "clients.plex.tv" && request.url.path == "/api/v2/pins" ->
                    """{"id":42,"code":"ABCD1234","expiresIn":300}"""
                request.url.host == "clients.plex.tv" && request.url.path == "/api/v2/pins/42" ->
                    """{"authToken":"account-token","expiresIn":300}"""
                request.url.host == "plex.tv" && request.url.path == "/api/v2/user" ->
                    """{"id":1}"""
                request.url.host == "clients.plex.tv" && request.url.path == "/api/v2/resources" ->
                    """
                    [{
                      "provides":"server,player",
                      "clientIdentifier":"server-1",
                      "name":"Living Room",
                      "owned":true,
                      "accessToken":"server-token",
                      "connections":[
                        {"uri":"http://192.168.1.20:32400","local":true,"relay":false,"IPv6":false},
                        {"uri":"https://plex.local:32400","local":true,"relay":false,"IPv6":false}
                      ]
                    }]
                    """.trimIndent()
                request.url.path == "/identity" -> {
                    if (blockServerIdentity) {
                        identityEntered.countDown()
                        check(releaseIdentity.await(2, TimeUnit.SECONDS)) {
                            "The cancellation test did not release the identity probe."
                        }
                    }
                    """{"MediaContainer":{"machineIdentifier":"$serverId","friendlyName":"Living Room"}}"""
                }
                else -> error("Unexpected fixture request: ${request.method} ${request.url}")
            }
            return MediaHttpResponse(200, body.toByteArray(Charsets.UTF_8))
        }
    }

    private companion object {
        val testDevice = MediaCenterDeviceIdentity(
            client = "OrbitalVue",
            device = "Android Test",
            deviceId = "android-plex-discovery-test",
            version = "5.8.0"
        )
    }
}
