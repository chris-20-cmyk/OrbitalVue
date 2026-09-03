package com.orbitalvue.player.data

import java.net.URI
import java.net.URLDecoder
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.util.Base64
import java.util.Locale

internal object MediaCenterUrlPolicy {
    private const val MAX_BASE_URL_BYTES = 4_096
    private const val MAX_PROVIDER_URL_BYTES = 8_192
    private val sensitiveQueryNames = setOf(
        "apikey", "accesstoken", "authtoken", "credential", "password",
        "secret", "token", "xembytoken", "xplextoken"
    )

    fun normalizeBaseUrl(rawValue: String): URI {
        val value = rawValue.trim()
        require(value.isNotEmpty() && value.toByteArray().size <= MAX_BASE_URL_BYTES && !value.hasControls()) {
            "Enter a valid media-server address."
        }
        val candidate = if (SCHEME_PATTERN.containsMatchIn(value)) value else "https://$value"
        val parsed = runCatching { URI(candidate) }.getOrNull()
            ?: error("Enter a valid media-server address.")
        val scheme = parsed.scheme?.lowercase(Locale.ROOT)
        require(scheme in setOf("http", "https") && !parsed.host.isNullOrBlank()) {
            "Enter a complete HTTP or HTTPS media-server address."
        }
        require(parsed.userInfo == null && parsed.rawQuery == null && parsed.rawFragment == null) {
            "Media-server addresses cannot include credentials, a query, or a fragment."
        }
        require(parsed.port in -1..65_535) { "The media-server port is invalid." }
        val normalizedPath = parsed.normalize().rawPath.orEmpty().trimEnd('/').let {
            if (it == "/") "" else it
        }
        require(isSafePath(normalizedPath)) { "The media-server path is unsafe." }
        val port = when {
            parsed.port == 80 && scheme == "http" -> -1
            parsed.port == 443 && scheme == "https" -> -1
            else -> parsed.port
        }
        return rawUri(scheme!!, parsed.host.lowercase(Locale.ROOT), port, normalizedPath, null)
    }

    fun requireAllowedTransport(baseUrl: URI, allowInsecureHttp: Boolean) {
        if (baseUrl.scheme.equals("http", ignoreCase = true) && !allowInsecureHttp) {
            error("Approve the unencrypted local HTTP connection before continuing.")
        }
    }

    fun safeDisplayLocation(baseUrl: URI): String {
        val host = baseUrl.host.orEmpty()
        val displayHost = if (':' in host) "[$host]" else host
        val port = if (baseUrl.port >= 0) ":${baseUrl.port}" else ""
        return "$displayHost$port${baseUrl.rawPath.orEmpty()}"
    }

    fun resolveServerPath(baseUrl: URI, rawValue: String): URI {
        val raw = rawValue.trim()
        require(raw.isNotEmpty() && raw.toByteArray().size <= MAX_PROVIDER_URL_BYTES) {
            "The media server returned an unsafe address."
        }
        require(!raw.hasControls() && '\\' !in raw) { "The media server returned an unsafe address." }
        val candidate = if (SCHEME_PATTERN.containsMatchIn(raw)) {
            URI(raw)
        } else {
            val basePath = baseUrl.rawPath.orEmpty()
            val root = rawUri(baseUrl.scheme, baseUrl.host, baseUrl.port, "/", null)
            if (raw.startsWith('/') && (
                    basePath.isEmpty() || raw == basePath || raw.startsWith("$basePath/")
                )) {
                root.resolve(raw)
            } else {
                val directory = rawUri(
                    baseUrl.scheme,
                    baseUrl.host,
                    baseUrl.port,
                    "${basePath.trimEnd('/')}/",
                    null
                )
                directory.resolve(raw.trimStart('/'))
            }
        }.normalize()
        require(candidate.userInfo == null && sameOrigin(candidate, baseUrl) && isSafePath(candidate.rawPath.orEmpty())) {
            "The media server returned an address outside the verified server."
        }
        val rootPath = baseUrl.rawPath.orEmpty()
        val candidatePath = candidate.rawPath.orEmpty()
        require(rootPath.isEmpty() || candidatePath == rootPath || candidatePath.startsWith("$rootPath/")) {
            "The media server returned an address outside the verified server path."
        }
        return rawUri(
            candidate.scheme.lowercase(Locale.ROOT),
            candidate.host.lowercase(Locale.ROOT),
            candidate.port,
            candidate.rawPath,
            safeQuery(candidate.rawQuery)
        )
    }

    fun sanitizePathForStorage(baseUrl: URI, rawValue: String): String {
        val resolved = resolveServerPath(baseUrl, rawValue)
        val basePath = baseUrl.rawPath.orEmpty()
        require(resolved.rawPath.orEmpty().startsWith(basePath)) { "The media path is unsafe." }
        var relative = resolved.rawPath.orEmpty().removePrefix(basePath)
        if (relative.isEmpty()) relative = "/"
        if (!relative.startsWith('/')) relative = "/$relative"
        return if (resolved.rawQuery.isNullOrEmpty()) relative else "$relative?${resolved.rawQuery}"
    }

    fun appendQuery(uri: URI, values: Map<String, String>): URI {
        val existing = uri.rawQuery.orEmpty().split('&').filter(String::isNotBlank).toMutableList()
        values.toSortedMap().forEach { (name, value) ->
            require(name.isNotBlank() && !name.hasControls() && !value.hasControls()) {
                "The media-server query is unsafe."
            }
            existing.removeAll { queryName(it).equals(name, ignoreCase = true) }
            existing += "${encodeQuery(name)}=${encodeQuery(value)}"
        }
        return rawUri(uri.scheme, uri.host, uri.port, uri.rawPath, existing.joinToString("&"))
    }

    fun requireIdentifier(rawValue: String, label: String): String {
        val value = rawValue.trim()
        require(value.isNotEmpty() && value.toByteArray().size <= 256 && value.all(::isIdentifierCharacter)) {
            "The $label identifier is invalid."
        }
        return value
    }

    fun credential(rawValue: String): String {
        val value = rawValue.trim()
        require(value.isNotEmpty() && value.toByteArray().size <= 8_192 && !value.hasControls()) {
            "The media-server credential is invalid."
        }
        return value
    }

    fun safeMetadata(rawValue: String?, secret: String = "", maximumLength: Int = 512): String? {
        val value = rawValue
            ?.filterNot(Char::isISOControl)
            ?.trim()
            ?.take(maximumLength)
            ?.takeIf(String::isNotEmpty)
            ?: return null
        if (secret.isNotEmpty() && value.contains(secret, ignoreCase = false)) return null
        return value
    }

    fun hash(value: String): String = MessageDigest.getInstance("SHA-256")
        .digest(value.toByteArray(StandardCharsets.UTF_8))
        .joinToString("") { "%02X".format(it) }

    fun encodeLocatorComponent(value: String): String = Base64.getUrlEncoder()
        .withoutPadding()
        .encodeToString(value.toByteArray(StandardCharsets.UTF_8))

    fun decodeLocatorComponent(value: String): String = String(
        Base64.getUrlDecoder().decode(value),
        StandardCharsets.UTF_8
    )

    private fun sameOrigin(left: URI, right: URI): Boolean =
        left.scheme.equals(right.scheme, ignoreCase = true) &&
            left.host.equals(right.host, ignoreCase = true) &&
            effectivePort(left) == effectivePort(right)

    private fun effectivePort(uri: URI): Int = when {
        uri.port >= 0 -> uri.port
        uri.scheme.equals("http", true) -> 80
        else -> 443
    }

    private fun safeQuery(rawQuery: String?): String? {
        val values = rawQuery.orEmpty().split('&').filter(String::isNotBlank).filterNot { part ->
            val normalized = queryName(part).lowercase(Locale.ROOT).filter(Char::isLetterOrDigit)
            normalized in sensitiveQueryNames || listOf("token", "password", "secret", "credential")
                .any(normalized::contains)
        }
        return values.joinToString("&").takeIf(String::isNotEmpty)
    }

    private fun queryName(part: String): String = runCatching {
        URLDecoder.decode(part.substringBefore('='), StandardCharsets.UTF_8.name())
    }.getOrElse { "" }

    private fun encodeQuery(value: String): String = java.net.URLEncoder
        .encode(value, StandardCharsets.UTF_8.name())
        .replace("+", "%20")

    private fun isSafePath(value: String): Boolean {
        val lower = value.lowercase(Locale.ROOT)
        return '\\' !in value && !value.hasControls() &&
            "%2e" !in lower && "%2f" !in lower && "%5c" !in lower
    }

    private fun isIdentifierCharacter(value: Char): Boolean =
        value.isLetterOrDigit() || value in setOf('-', '.', ':', '_')

    private fun String.hasControls(): Boolean = any(Char::isISOControl)

    private fun rawUri(scheme: String, host: String, port: Int, rawPath: String, rawQuery: String?): URI {
        val authorityHost = if (':' in host && !host.startsWith('[')) "[$host]" else host
        val authority = if (port >= 0) "$authorityHost:$port" else authorityHost
        val path = rawPath.ifEmpty { "" }.let { if (it.isNotEmpty() && !it.startsWith('/')) "/$it" else it }
        val query = rawQuery?.takeIf(String::isNotEmpty)?.let { "?$it" }.orEmpty()
        return URI("${scheme.lowercase(Locale.ROOT)}://$authority$path$query")
    }

    private val SCHEME_PATTERN = Regex("^[A-Za-z][A-Za-z0-9+.-]*://")
}

internal data class MediaCenterPlaybackLocator(
    val provider: MediaCenterProvider,
    val serverId: String,
    val itemId: String
)

internal object MediaCenterLocator {
    fun playbackUri(provider: MediaCenterProvider, serverId: String, itemId: String): String =
        "orbitalvue-media://${provider.name.lowercase(Locale.ROOT)}/" +
            "${MediaCenterUrlPolicy.encodeLocatorComponent(serverId)}/" +
            MediaCenterUrlPolicy.encodeLocatorComponent(itemId)

    fun artworkUri(provider: MediaCenterProvider, serverId: String, itemId: String): String =
        "orbitalvue-artwork://${provider.name.lowercase(Locale.ROOT)}/" +
            "${MediaCenterUrlPolicy.encodeLocatorComponent(serverId)}/" +
            MediaCenterUrlPolicy.encodeLocatorComponent(itemId)

    fun parsePlaybackUri(rawValue: String): MediaCenterPlaybackLocator {
        val uri = URI(rawValue)
        require(uri.scheme.equals("orbitalvue-media", true) && uri.rawQuery == null && uri.rawFragment == null) {
            "The protected media locator is invalid."
        }
        val provider = when (uri.host?.lowercase(Locale.ROOT)) {
            "plex" -> MediaCenterProvider.Plex
            "emby" -> MediaCenterProvider.Emby
            else -> error("The protected media locator provider is invalid.")
        }
        val parts = uri.rawPath.orEmpty().trim('/').split('/')
        require(parts.size == 2) { "The protected media locator is invalid." }
        return MediaCenterPlaybackLocator(
            provider = provider,
            serverId = MediaCenterUrlPolicy.requireIdentifier(
                MediaCenterUrlPolicy.decodeLocatorComponent(parts[0]),
                "server"
            ),
            itemId = MediaCenterUrlPolicy.requireIdentifier(
                MediaCenterUrlPolicy.decodeLocatorComponent(parts[1]),
                "item"
            )
        )
    }
}

internal object MediaCenterHeaderPolicy {
    private val reservedProviderHeaders = setOf(
        "authorization", "connection", "content-length", "cookie", "host",
        "proxy-authorization", "proxy-connection", "set-cookie", "te", "trailer",
        "transfer-encoding", "upgrade", "x-emby-authorization", "x-emby-token", "x-plex-token"
    )

    fun providerHeaders(values: Map<String, String>): Map<String, String> = values.mapNotNull { (name, value) ->
        val normalizedName = name.trim()
        val normalizedValue = value.trim()
        if (normalizedName.isEmpty() || normalizedName.lowercase(Locale.ROOT) in reservedProviderHeaders ||
            normalizedName.hasControls() || normalizedValue.hasControls() || normalizedValue.length > 8_192) {
            null
        } else {
            normalizedName to normalizedValue
        }
    }.toMap()

    private fun String.hasControls(): Boolean = any(Char::isISOControl)
}
