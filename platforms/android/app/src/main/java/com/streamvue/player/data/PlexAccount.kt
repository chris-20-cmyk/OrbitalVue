package com.streamvue.player.data

import com.google.gson.Gson
import com.google.gson.JsonElement
import com.google.gson.JsonObject
import com.google.gson.JsonParser
import java.net.URI
import java.net.URLEncoder
import java.nio.charset.StandardCharsets
import java.time.Instant
import java.time.temporal.ChronoUnit
import java.util.Locale

data class PlexPinChallenge(
    val id: Int,
    val code: String,
    val authorizationUrl: String,
    val expiresAt: Instant
)

data class PlexServerConnectionChoice(
    val url: String,
    val isLocal: Boolean,
    val isRelay: Boolean,
    val isSecure: Boolean,
    val isIpv6: Boolean
)

data class PlexDiscoveredServer(
    val serverId: String,
    val name: String,
    val isOwned: Boolean,
    val connections: List<PlexServerConnectionChoice>
) {
    val preferredConnection: PlexServerConnectionChoice?
        get() = connections.firstOrNull()
}

/** An opaque, short-lived discovery lease. It never contains a Plex token. */
data class PlexServerDiscovery(
    val sessionId: String,
    val servers: List<PlexDiscoveredServer>,
    val expiresAt: Instant
)

internal data class PlexAccountToken(
    val value: String,
    val expiresAt: Instant?
)

internal data class PlexAccountServerSecret(
    val server: PlexDiscoveredServer,
    val accessToken: String
)

internal data class PlexAccountDiscoverySecret(
    val servers: List<PlexAccountServerSecret>,
    val expiresAt: Instant
)

internal interface PlexDeviceSigner {
    val publicJwk: JsonObject
    fun sign(claims: Map<String, Any>): String
}

/** Implements Plex's signed strong-PIN account flow without retaining account tokens. */
internal class PlexAccountClient(
    private val transport: MediaCenterTransport,
    private val signer: PlexDeviceSigner,
    private val device: MediaCenterDeviceIdentity,
    private val gson: Gson = Gson(),
    private val now: () -> Instant = Instant::now
) {
    fun createPin(): PlexPinChallenge {
        val publicKey = validatePublicJwk(signer.publicJwk)
        val body = JsonObject().apply {
            add("jwk", publicKey)
            addProperty("strong", true)
        }
        val payload = json(
            MediaHttpRequest(
                method = MediaHttpMethod.POST,
                url = URI("$CLIENTS_BASE_URL/pins"),
                headers = headers + ("Content-Type" to "application/json"),
                body = gson.toJson(body).toByteArray(Charsets.UTF_8)
            )
        ).objectValue()
        val id = payload.integer("id")?.takeIf { it > 0 }
            ?: error("Plex returned an incomplete sign-in challenge.")
        val code = payload.text("code")?.let {
            MediaCenterUrlPolicy.requireIdentifier(it, "Plex sign-in code")
        } ?: error("Plex returned an incomplete sign-in challenge.")
        val createdAt = now()
        val expiresAt = expiry(payload, createdAt) ?: createdAt.plus(5, ChronoUnit.MINUTES)
        require(expiresAt.isAfter(createdAt)) { "Plex returned an expired sign-in challenge." }
        return PlexPinChallenge(
            id = id,
            code = code,
            authorizationUrl = authorizationUrl(code),
            expiresAt = expiresAt
        )
    }

    fun claimPin(challenge: PlexPinChallenge): PlexAccountToken? {
        require(challenge.id > 0 && challenge.expiresAt.isAfter(now())) {
            "The Plex sign-in request expired. Start a new sign-in."
        }
        val issuedAt = now().epochSecond
        require(issuedAt > 0) { "The device clock is invalid." }
        val proof = validateCompactJwt(
            signer.sign(
                sortedMapOf(
                    "aud" to "plex.tv",
                    "exp" to issuedAt + 300,
                    "iat" to issuedAt,
                    "iss" to clientIdentifier
                )
            )
        )
        val payload = json(
            MediaHttpRequest(
                method = MediaHttpMethod.GET,
                url = MediaCenterUrlPolicy.appendQuery(
                    URI("$CLIENTS_BASE_URL/pins/${challenge.id}"),
                    mapOf("deviceJWT" to proof)
                ),
                headers = headers
            )
        ).objectValue()
        return accountToken(payload)
    }

    fun verifyAccountToken(rawToken: String) {
        val token = MediaCenterUrlPolicy.credential(rawToken)
        json(
            MediaHttpRequest(
                method = MediaHttpMethod.GET,
                url = URI("$ACCOUNT_BASE_URL/user"),
                headers = authenticatedHeaders(token)
            )
        )
    }

    fun discoverServers(rawToken: String): List<PlexAccountServerSecret> {
        val accountToken = MediaCenterUrlPolicy.credential(rawToken)
        val payload = json(
            MediaHttpRequest(
                method = MediaHttpMethod.GET,
                url = MediaCenterUrlPolicy.appendQuery(
                    URI("$CLIENTS_BASE_URL/resources"),
                    mapOf("includeHttps" to "1", "includeRelay" to "1", "includeIPv6" to "1")
                ),
                headers = authenticatedHeaders(accountToken)
            )
        )
        return payload.arrayValue().mapNotNull { parseServer(it, accountToken) }
    }

    private val clientIdentifier: String
        get() = MediaCenterUrlPolicy.requireIdentifier(device.deviceId, "Plex client")

    private val headers: Map<String, String>
        get() = mapOf(
            "Accept" to "application/json",
            "X-Plex-Client-Identifier" to clientIdentifier,
            "X-Plex-Product" to safeHeader(device.client, "StreamVue"),
            "X-Plex-Version" to safeHeader(device.version, "5.1.0")
        )

    private fun authenticatedHeaders(token: String): Map<String, String> =
        headers + ("X-Plex-Token" to MediaCenterUrlPolicy.credential(token))

    private fun json(request: MediaHttpRequest): JsonElement {
        require(
            request.url.scheme.equals("https", true) &&
                request.url.host?.lowercase(Locale.ROOT) in PROVIDER_HOSTS &&
                request.url.userInfo == null
        ) { "The Plex account request address is unsafe." }
        val response = transport.execute(request)
        require(response.status in 200..299) { "Plex sign-in returned HTTP ${response.status}." }
        require(response.body.isNotEmpty() && response.body.size <= MAX_RESPONSE_BYTES) {
            "Plex returned an invalid sign-in response."
        }
        return runCatching { JsonParser.parseString(response.body.toString(Charsets.UTF_8)) }
            .getOrElse { error("Plex returned an invalid sign-in response.") }
    }

    private fun parseServer(value: JsonElement, accountToken: String): PlexAccountServerSecret? {
        val raw = value.objectValue()
        val provides = raw.text("provides").orEmpty().split(',').mapTo(HashSet()) {
            it.trim().lowercase(Locale.ROOT)
        }
        if ("server" !in provides) return null
        val serverId = raw.text("clientIdentifier")?.let {
            runCatching { MediaCenterUrlPolicy.requireIdentifier(it, "Plex server") }.getOrNull()
        } ?: return null
        val accessToken = raw.text("accessToken")?.let {
            runCatching { MediaCenterUrlPolicy.credential(it) }.getOrNull()
        } ?: return null
        if (serverId.contains(accessToken) || serverId.contains(accountToken)) return null
        val redactedName = MediaCenterUrlPolicy.safeMetadata(raw.text("name"), accessToken, 256)
        val name = MediaCenterUrlPolicy.safeMetadata(redactedName, accountToken, 256) ?: return null
        val connections = raw.arrayAt("connections")
            .mapNotNull { parseConnection(it, listOf(accountToken, accessToken)) }
            .sortedBy(::connectionPriority)
        if (connections.isEmpty()) return null
        return PlexAccountServerSecret(
            server = PlexDiscoveredServer(
                serverId = serverId,
                name = name,
                isOwned = raw.boolean("owned"),
                connections = connections
            ),
            accessToken = accessToken
        )
    }

    private fun parseConnection(
        value: JsonElement,
        credentials: List<String>
    ): PlexServerConnectionChoice? {
        val raw = value.objectValue()
        var candidate = raw.text("uri")
        if (candidate == null) {
            val scheme = raw.text("protocol")?.lowercase(Locale.ROOT)
            val address = raw.text("address")
            val port = raw.integer("port")
            if (scheme in setOf("http", "https") && address != null && port in 1..65_535) {
                val host = if (':' in address && !address.startsWith('[')) "[$address]" else address
                candidate = "$scheme://$host:$port"
            }
        }
        val url = candidate?.let {
            runCatching { MediaCenterUrlPolicy.normalizeBaseUrl(it) }.getOrNull()
        } ?: return null
        if (credentials.any { it.isEmpty() || url.toASCIIString().contains(it) }) return null
        return PlexServerConnectionChoice(
            url = url.toASCIIString(),
            isLocal = raw.boolean("local"),
            isRelay = raw.boolean("relay"),
            isSecure = url.scheme.equals("https", true),
            isIpv6 = raw.boolean("IPv6") || ':' in url.host.orEmpty()
        )
    }

    private fun accountToken(payload: JsonObject): PlexAccountToken? {
        val token = (payload.text("authToken") ?: payload.text("auth_token"))?.let {
            runCatching { MediaCenterUrlPolicy.credential(it) }.getOrNull()
        } ?: return null
        return PlexAccountToken(token, expiry(payload, now()))
    }

    private fun expiry(payload: JsonObject, relativeTo: Instant): Instant? {
        val explicit = payload.text("expiresAt") ?: payload.text("expires_at")
        if (explicit != null) runCatching { Instant.parse(explicit) }.getOrNull()?.let { return it }
        val seconds = payload.number("expiresIn") ?: payload.number("expires_in") ?: return null
        if (seconds <= 0) return null
        return runCatching { relativeTo.plusSeconds(seconds) }.getOrNull()
    }

    private fun authorizationUrl(code: String): String {
        val query = linkedMapOf(
            "clientID" to clientIdentifier,
            "code" to code,
            "context[device][product]" to safeHeader(device.client, "StreamVue")
        ).entries.joinToString("&") { (name, value) ->
            "${encode(name)}=${encode(value)}"
        }
        return "https://app.plex.tv/auth#?$query"
    }

    private fun validatePublicJwk(untrusted: JsonObject): JsonObject {
        require(!untrusted.has("d")) { "A Plex public device key cannot contain private key material." }
        require(
            untrusted.text("kty") == "OKP" && untrusted.text("crv") == "Ed25519" &&
                untrusted.text("alg") == "EdDSA"
        ) { "Plex requires an Ed25519 device signing key." }
        val x = untrusted.text("x").orEmpty()
        require(x.matches(Regex("^[A-Za-z0-9_-]{40,64}$"))) {
            "The Plex device public key is invalid."
        }
        val kid = MediaCenterUrlPolicy.requireIdentifier(
            untrusted.text("kid").orEmpty(),
            "Plex device key"
        )
        return JsonObject().apply {
            addProperty("kty", "OKP")
            addProperty("crv", "Ed25519")
            addProperty("x", x)
            addProperty("kid", kid)
            addProperty("alg", "EdDSA")
        }
    }

    private fun validateCompactJwt(rawValue: String): String {
        val value = rawValue.trim()
        require(value.length <= 16_384 && value.matches(COMPACT_JWT)) {
            "The Plex device proof is invalid."
        }
        return value
    }

    private fun connectionPriority(value: PlexServerConnectionChoice): Int =
        (if (value.isSecure) 0 else 1_000) +
            (if (value.isLocal) 0 else 100) +
            (if (value.isRelay) 50 else 0) +
            (if (value.isIpv6) 1 else 0)

    private fun safeHeader(value: String, fallback: String): String = value
        .filterNot(Char::isISOControl)
        .trim()
        .take(256)
        .ifEmpty { fallback }

    private fun encode(value: String): String = URLEncoder
        .encode(value, StandardCharsets.UTF_8.name())
        .replace("+", "%20")

    private companion object {
        const val CLIENTS_BASE_URL = "https://clients.plex.tv/api/v2"
        const val ACCOUNT_BASE_URL = "https://plex.tv/api/v2"
        const val MAX_RESPONSE_BYTES = 2 * 1_024 * 1_024
        val PROVIDER_HOSTS = setOf("clients.plex.tv", "plex.tv")
        val COMPACT_JWT = Regex("^[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+$")
    }
}
