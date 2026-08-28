package com.streamvue.player.data

import com.google.gson.JsonArray
import com.google.gson.JsonElement
import com.google.gson.JsonObject
import com.google.gson.JsonParser
import java.io.ByteArrayOutputStream
import java.net.HttpURLConnection
import java.net.URI
import java.net.URL

internal enum class MediaHttpMethod { GET, POST }

internal data class MediaHttpRequest(
    val method: MediaHttpMethod,
    val url: URI,
    val headers: Map<String, String> = emptyMap(),
    val body: ByteArray? = null
)

internal data class MediaHttpResponse(
    val status: Int,
    val body: ByteArray
)

internal fun interface MediaCenterTransport {
    fun execute(request: MediaHttpRequest): MediaHttpResponse
}

internal class UrlConnectionMediaCenterTransport : MediaCenterTransport {
    override fun execute(request: MediaHttpRequest): MediaHttpResponse {
        require(request.url.scheme?.lowercase() in setOf("http", "https") && request.url.userInfo == null) {
            "The media request address is unsafe."
        }
        val connection = URL(request.url.toASCIIString()).openConnection() as HttpURLConnection
        connection.requestMethod = request.method.name
        connection.connectTimeout = CONNECT_TIMEOUT_MS
        connection.readTimeout = READ_TIMEOUT_MS
        connection.instanceFollowRedirects = false
        connection.useCaches = false
        request.headers.forEach { (name, value) ->
            require(name.isNotBlank() && !name.hasControls() && !value.hasControls() && value.length <= 8_192) {
                "The media request contained an unsafe header."
            }
            connection.setRequestProperty(name, value)
        }
        request.body?.let { body ->
            require(body.size <= MAX_REQUEST_BYTES) { "The media request is too large." }
            connection.doOutput = true
            connection.setFixedLengthStreamingMode(body.size)
            connection.outputStream.use { it.write(body) }
        }

        try {
            val status = connection.responseCode
            require(status !in REDIRECT_STATUS_CODES) {
                "The media server redirected a protected request unexpectedly."
            }
            val stream = if (status in 200..299) connection.inputStream else connection.errorStream
            val body = stream?.use(::readLimited) ?: ByteArray(0)
            return MediaHttpResponse(status = status, body = body)
        } finally {
            connection.disconnect()
        }
    }

    private fun readLimited(input: java.io.InputStream): ByteArray {
        val output = ByteArrayOutputStream()
        val buffer = ByteArray(64 * 1_024)
        var total = 0
        while (true) {
            val count = input.read(buffer)
            if (count < 0) break
            total += count
            require(total <= MAX_RESPONSE_BYTES) { "The media server response exceeded the safety limit." }
            output.write(buffer, 0, count)
        }
        return output.toByteArray()
    }

    private fun String.hasControls(): Boolean = any(Char::isISOControl)

    private companion object {
        const val CONNECT_TIMEOUT_MS = 15_000
        const val READ_TIMEOUT_MS = 30_000
        const val MAX_REQUEST_BYTES = 1 * 1_024 * 1_024
        const val MAX_RESPONSE_BYTES = 16 * 1_024 * 1_024
        val REDIRECT_STATUS_CODES = setOf(301, 302, 303, 307, 308)
    }
}

internal object MediaCenterApi {
    fun json(request: MediaHttpRequest, transport: MediaCenterTransport): JsonElement {
        val response = transport.execute(request)
        require(response.status in 200..299) { "The media server returned HTTP ${response.status}." }
        require(response.body.isNotEmpty()) { "The media server returned an empty response." }
        return runCatching { JsonParser.parseString(response.body.toString(Charsets.UTF_8)) }
            .getOrElse { error("The media server returned invalid JSON.") }
    }
}

internal fun JsonElement.objectValue(): JsonObject = if (isJsonObject) asJsonObject else JsonObject()
internal fun JsonElement.arrayValue(): JsonArray = if (isJsonArray) asJsonArray else JsonArray()

internal fun JsonObject.text(name: String): String? = get(name)?.let { value ->
    if (!value.isJsonPrimitive) return@let null
    runCatching { value.asString.trim().takeIf(String::isNotEmpty) }.getOrNull()
}

internal fun JsonObject.number(name: String): Long? = get(name)?.let { value ->
    if (!value.isJsonPrimitive) return@let null
    runCatching { value.asLong }.getOrNull()
}

internal fun JsonObject.integer(name: String): Int? = number(name)?.takeIf { it in Int.MIN_VALUE..Int.MAX_VALUE }?.toInt()

internal fun JsonObject.boolean(name: String): Boolean = get(name)?.let { value ->
    if (!value.isJsonPrimitive) return@let false
    runCatching {
        if (value.asJsonPrimitive.isBoolean) value.asBoolean
        else value.asString.lowercase() in setOf("true", "yes", "1")
    }.getOrDefault(false)
} ?: false

internal fun JsonObject.objectAt(name: String): JsonObject = get(name)?.objectValue() ?: JsonObject()
internal fun JsonObject.arrayAt(name: String): JsonArray = get(name)?.arrayValue() ?: JsonArray()
