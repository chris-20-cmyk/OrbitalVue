package com.orbitalvue.player.data

import com.google.gson.JsonObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant

class PlexAccountClientTest {
    private val clock = Instant.parse("2026-08-30T12:00:00Z")
    private val device = MediaCenterDeviceIdentity(
        client = "OrbitalVue",
        device = "Android",
        deviceId = "orbitalvue-android-test",
        version = "5.7.0"
    )

    @Test
    fun `creates a strong pin with public key material only`() {
        val transport = RecordingTransport { request ->
            assertEquals(MediaHttpMethod.POST, request.method)
            assertEquals("clients.plex.tv", request.url.host)
            val body = request.body!!.toString(Charsets.UTF_8)
            assertTrue(body.contains("\"strong\":true"))
            assertTrue(body.contains("\"kty\":\"OKP\""))
            assertFalse(body.contains("\"d\""))
            response("""{"id":42,"code":"ABCD1234","expiresIn":300}""")
        }

        val challenge = client(transport).createPin()

        assertEquals(42, challenge.id)
        assertEquals("ABCD1234", challenge.code)
        assertEquals(clock.plusSeconds(300), challenge.expiresAt)
        assertTrue(challenge.authorizationUrl.startsWith("https://app.plex.tv/auth#?"))
        assertTrue(challenge.authorizationUrl.contains("clientID=orbitalvue-android-test"))
        assertTrue(challenge.authorizationUrl.contains("code=ABCD1234"))
    }

    @Test
    fun `claims the pin with a compact signed device proof`() {
        val signer = FakeSigner()
        val transport = RecordingTransport { request ->
            assertEquals(MediaHttpMethod.GET, request.method)
            assertEquals("/api/v2/pins/42", request.url.path)
            assertTrue(request.url.rawQuery.orEmpty().contains("deviceJWT=header.payload.signature"))
            response("{}")
        }
        val challenge = PlexPinChallenge(
            id = 42,
            code = "ABCD1234",
            authorizationUrl = "https://app.plex.tv/auth",
            expiresAt = clock.plusSeconds(300)
        )

        val token = client(transport, signer).claimPin(challenge)

        assertNull(token)
        assertEquals("plex.tv", signer.claims["aud"])
        assertEquals("orbitalvue-android-test", signer.claims["iss"])
        assertEquals(clock.epochSecond, signer.claims["iat"])
        assertEquals(clock.epochSecond + 300, signer.claims["exp"])
    }

    @Test
    fun `discovers servers and prefers secure local connections`() {
        val transport = RecordingTransport { request ->
            assertEquals("account-token", request.headers["X-Plex-Token"])
            response(
                """
                [
                  {"provides":"client,player","name":"Ignore me"},
                  {
                    "provides":"server,player",
                    "clientIdentifier":"server-1",
                    "name":"Living Room",
                    "owned":true,
                    "accessToken":"server-token",
                    "connections":[
                      {"uri":"http://192.168.1.20:32400","local":true,"relay":false,"IPv6":false},
                      {"uri":"https://secure.example:32400","local":true,"relay":false,"IPv6":false},
                      {"uri":"https://relay.example:443","local":false,"relay":true,"IPv6":false}
                    ]
                  }
                ]
                """.trimIndent()
            )
        }

        val servers = client(transport).discoverServers("account-token")

        assertEquals(1, servers.size)
        assertEquals("server-token", servers.single().accessToken)
        assertEquals("server-1", servers.single().server.serverId)
        assertEquals("https://secure.example:32400", servers.single().server.connections.first().url)
        assertTrue(servers.single().server.connections.first().isSecure)
    }

    @Test
    fun `rejects private key material before contacting Plex`() {
        val jwk = validJwk().apply { addProperty("d", "private-material") }
        val transport = RecordingTransport { error("The network must not be called.") }

        assertThrows(IllegalArgumentException::class.java) {
            client(transport, FakeSigner(jwk)).createPin()
        }
        assertTrue(transport.requests.isEmpty())
    }

    private fun client(
        transport: MediaCenterTransport,
        signer: FakeSigner = FakeSigner()
    ): PlexAccountClient = PlexAccountClient(
        transport = transport,
        signer = signer,
        device = device,
        now = { clock }
    )

    private fun response(body: String) = MediaHttpResponse(
        status = 200,
        body = body.toByteArray(Charsets.UTF_8)
    )

    private class RecordingTransport(
        private val handler: (MediaHttpRequest) -> MediaHttpResponse
    ) : MediaCenterTransport {
        val requests = ArrayList<MediaHttpRequest>()

        override fun execute(request: MediaHttpRequest): MediaHttpResponse {
            requests += request
            return handler(request)
        }
    }

    private class FakeSigner(
        override val publicJwk: JsonObject = validJwk()
    ) : PlexDeviceSigner {
        var claims: Map<String, Any> = emptyMap()

        override fun sign(claims: Map<String, Any>): String {
            this.claims = claims
            return "header.payload.signature"
        }
    }

    private companion object {
        fun validJwk() = JsonObject().apply {
            addProperty("kty", "OKP")
            addProperty("crv", "Ed25519")
            addProperty("x", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")
            addProperty("kid", "plex-device-key")
            addProperty("alg", "EdDSA")
        }
    }
}
