package com.streamvue.player.premium

import com.google.gson.Gson
import com.google.gson.JsonParser
import com.google.gson.annotations.SerializedName
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.math.BigDecimal
import java.net.URI
import javax.net.ssl.HttpsURLConnection

data class PremiumVerificationRequest(
    @SerializedName("schemaVersion") val schemaVersion: Int = 1,
    @SerializedName("platform") val platform: String = "google-play",
    @SerializedName("packageName") val packageName: String,
    @SerializedName("productId") val productId: String,
    @SerializedName("purchaseToken") val purchaseToken: String
)

data class PremiumVerificationResponse(
    @SerializedName("schemaVersion") val schemaVersion: Int = 0,
    @SerializedName("verified") val verified: Boolean = false,
    @SerializedName("productId") val productId: String? = null
)

fun interface PremiumVerificationTransport {
    suspend fun send(request: PremiumVerificationRequest): PremiumVerificationResponse
}

class BackendPremiumPurchaseVerifier(
    private val packageName: String,
    private val transport: PremiumVerificationTransport
) {
    suspend fun verify(productId: String, purchaseToken: String): Boolean {
        if (purchaseToken.isBlank()) return false
        val response = transport.send(
            PremiumVerificationRequest(
                packageName = packageName,
                productId = productId,
                purchaseToken = purchaseToken
            )
        )
        return response.schemaVersion == 1 && response.verified && response.productId == productId
    }
}

class HttpsPremiumVerificationTransport(
    private val endpoint: URI,
    private val gson: Gson = Gson()
) : PremiumVerificationTransport {
    init {
        require(endpoint.scheme.equals("https", ignoreCase = true) &&
            !endpoint.host.isNullOrBlank() && endpoint.userInfo == null &&
            endpoint.query == null && endpoint.fragment == null) {
            "Premium verification requires an HTTPS endpoint without credentials, query, or fragment."
        }
    }

    override suspend fun send(request: PremiumVerificationRequest): PremiumVerificationResponse =
        withContext(Dispatchers.IO) {
            val body = gson.toJson(request).toByteArray(Charsets.UTF_8)
            require(body.size <= MAX_REQUEST_BYTES) { "Premium verification request is too large." }
            val connection = endpoint.toURL().openConnection() as HttpsURLConnection
            try {
                connection.requestMethod = "POST"
                connection.instanceFollowRedirects = false
                connection.connectTimeout = TIMEOUT_MS
                connection.readTimeout = TIMEOUT_MS
                connection.doOutput = true
                connection.setFixedLengthStreamingMode(body.size)
                connection.setRequestProperty("Accept", "application/json")
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
                connection.outputStream.use { it.write(body) }
                require(connection.responseCode in 200..299) {
                    "Premium verification service returned HTTP ${connection.responseCode}."
                }
                val announced = connection.contentLengthLong
                require(announced < 0 || announced <= MAX_RESPONSE_BYTES) {
                    "Premium verification response is too large."
                }
                val responseBytes = connection.inputStream.use { input ->
                    val buffer = ByteArray(MAX_RESPONSE_BYTES + 1)
                    var count = 0
                    while (count < buffer.size) {
                        val read = input.read(buffer, count, buffer.size - count)
                        if (read < 0) break
                        count += read
                    }
                    require(count <= MAX_RESPONSE_BYTES) { "Premium verification response is too large." }
                    buffer.copyOf(count)
                }
                parsePremiumVerificationResponse(responseBytes.toString(Charsets.UTF_8))
            } finally {
                connection.disconnect()
            }
        }

    private companion object {
        const val TIMEOUT_MS = 12_000
        const val MAX_REQUEST_BYTES = 32 * 1024
        const val MAX_RESPONSE_BYTES = 64 * 1024
    }
}

internal fun parsePremiumVerificationResponse(json: String): PremiumVerificationResponse {
    val root = JsonParser.parseString(json)
    require(root.isJsonObject) { "Premium verification response must be a JSON object." }
    val value = root.asJsonObject
    require(value.keySet() == setOf("schemaVersion", "verified", "productId")) {
        "Premium verification response fields are not exact."
    }
    val schemaVersion = value.get("schemaVersion")
    val verified = value.get("verified")
    val productId = value.get("productId")
    require(schemaVersion.isJsonPrimitive && schemaVersion.asJsonPrimitive.isNumber) {
        "Premium verification schemaVersion is invalid."
    }
    require(runCatching { schemaVersion.asBigDecimal.compareTo(BigDecimal.ONE) == 0 }.getOrDefault(false)) {
        "Premium verification schemaVersion is unsupported."
    }
    require(verified.isJsonPrimitive && verified.asJsonPrimitive.isBoolean) {
        "Premium verification verified value is invalid."
    }
    require(productId.isJsonPrimitive && productId.asJsonPrimitive.isString) {
        "Premium verification productId is invalid."
    }
    return PremiumVerificationResponse(
        schemaVersion = schemaVersion.asInt,
        verified = verified.asBoolean,
        productId = productId.asString
    )
}
