package com.streamvue.player.data

import android.content.Context
import android.net.Uri
import android.provider.OpenableColumns
import androidx.core.content.edit
import com.streamvue.player.BuildConfig
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.ByteArrayOutputStream
import java.io.File
import java.net.HttpURLConnection
import java.net.URI
import java.net.URL
import java.time.Instant
import java.util.UUID

class PlaylistRepository(private val context: Context) {
    private val preferences = context.getSharedPreferences("streamvue-source-v1", Context.MODE_PRIVATE)
    private val cacheFile = File(context.filesDir, "catalog/source.m3u")

    suspend fun loadSaved(): LoadedCatalog? = withContext(Dispatchers.IO) {
        val type = SourceType.fromStored(preferences.getString(KEY_SOURCE_TYPE, null)) ?: return@withContext null
        val sourceId = preferences.getString(KEY_SOURCE_ID, null) ?: return@withContext null
        val sourceName = preferences.getString(KEY_SOURCE_NAME, null) ?: "My channels"
        val sourceValue = preferences.getString(KEY_SOURCE_VALUE, null) ?: return@withContext null
        val displayLocation = preferences.getString(KEY_DISPLAY_LOCATION, null) ?: sourceName

        if (type == SourceType.Url) {
            try {
                val text = downloadPlaylist(sourceValue)
                val loaded = buildLoadedCatalog(
                    text = text,
                    sourceId = sourceId,
                    sourceName = sourceName,
                    type = type,
                    displayLocation = displayLocation,
                    usedCachedFallback = false,
                    notice = "Playlist refreshed at launch"
                )
                writeCache(text)
                return@withContext loaded
            } catch (error: Exception) {
                if (!cacheFile.exists()) throw error
                return@withContext buildLoadedCatalog(
                    text = cacheFile.readText(Charsets.UTF_8),
                    sourceId = sourceId,
                    sourceName = sourceName,
                    type = type,
                    displayLocation = displayLocation,
                    usedCachedFallback = true,
                    notice = "The source could not be refreshed. StreamVue protected playback with the last working copy."
                )
            }
        }

        if (!cacheFile.exists()) return@withContext null
        buildLoadedCatalog(
            text = cacheFile.readText(Charsets.UTF_8),
            sourceId = sourceId,
            sourceName = sourceName,
            type = type,
            displayLocation = displayLocation,
            usedCachedFallback = false,
            notice = null
        )
    }

    suspend fun importDocument(uri: Uri): LoadedCatalog = withContext(Dispatchers.IO) {
        val displayName = documentDisplayName(uri) ?: "Imported playlist"
        val sourceName = displayName.substringBeforeLast('.').ifBlank { "Imported playlist" }
        val text = context.contentResolver.openInputStream(uri)?.use { input ->
            decodePlaylist(readLimited(input))
        } ?: error("Android could not open that file.")
        val sourceId = UUID.randomUUID().toString()
        val loaded = buildLoadedCatalog(text, sourceId, sourceName, SourceType.File, displayName, false, null)
        writeCache(text)
        saveSource(sourceId, sourceName, SourceType.File, uri.toString(), displayName)
        loaded
    }

    suspend fun importUrl(rawValue: String): LoadedCatalog = withContext(Dispatchers.IO) {
        val normalized = normalizePlaylistUrl(rawValue)
        val displayLocation = safeDisplayLocation(normalized)
        val sourceName = URI(normalized).host?.takeIf(String::isNotBlank) ?: "Online playlist"
        val sourceId = UUID.randomUUID().toString()
        val text = downloadPlaylist(normalized)
        val loaded = buildLoadedCatalog(
            text,
            sourceId,
            sourceName,
            SourceType.Url,
            displayLocation,
            false,
            "Playlist connected and startup refresh enabled"
        )
        writeCache(text)
        saveSource(sourceId, sourceName, SourceType.Url, normalized, displayLocation)
        loaded
    }

    suspend fun refreshCurrent(): LoadedCatalog? = loadSaved()

    private fun buildLoadedCatalog(
        text: String,
        sourceId: String,
        sourceName: String,
        type: SourceType,
        displayLocation: String,
        usedCachedFallback: Boolean,
        notice: String?
    ): LoadedCatalog {
        val playlist = M3uParser.parse(text, sourceId, sourceName)
        val source = SourceDescriptor(
            id = sourceId,
            name = sourceName,
            type = type,
            displayLocation = displayLocation,
            refreshOnLaunch = type == SourceType.Url,
            usedCachedFallback = usedCachedFallback
        )
        return LoadedCatalog(
            catalog = Catalog(
                id = sourceId,
                displayName = sourceName,
                loadedAt = Instant.now().toString(),
                source = source,
                guideSources = listOfNotNull(playlist.guideSource),
                channels = playlist.channels
            ),
            notice = notice
        )
    }

    private fun downloadPlaylist(source: String): String {
        var current = URI(source)
        repeat(MAX_HTTP_REDIRECTS + 1) { redirectCount ->
            val connection = URL(current.toASCIIString()).openConnection() as HttpURLConnection
            connection.connectTimeout = 15_000
            connection.readTimeout = 30_000
            connection.instanceFollowRedirects = false
            connection.setRequestProperty("Accept", "application/x-mpegURL, audio/mpegurl, text/plain, */*")
            connection.setRequestProperty("User-Agent", "StreamVue Android/${BuildConfig.VERSION_NAME}")

            try {
                val status = connection.responseCode
                if (status in REDIRECT_STATUS_CODES) {
                    require(redirectCount < MAX_HTTP_REDIRECTS) { "Playlist server redirected too many times." }
                    val location = connection.getHeaderField("Location")
                        ?.trim()
                        ?.takeIf(String::isNotEmpty)
                        ?: error("Playlist server returned a redirect without a destination.")
                    val next = current.resolve(location)
                    require(next.scheme?.lowercase() in setOf("http", "https") && !next.host.isNullOrBlank()) {
                        "Playlist server redirected to an unsupported address."
                    }
                    require(!(current.scheme.equals("https", true) && next.scheme.equals("http", true))) {
                        "Playlist server attempted an insecure HTTPS-to-HTTP redirect."
                    }
                    current = next
                } else {
                    require(status in 200..299) { "Playlist server returned HTTP $status." }
                    return decodePlaylist(connection.inputStream.use(::readLimited))
                }
            } finally {
                connection.disconnect()
            }
        }
        error("Playlist server redirected too many times.")
    }

    private fun readLimited(input: java.io.InputStream): ByteArray {
        val output = ByteArrayOutputStream()
        val buffer = ByteArray(64 * 1024)
        var total = 0
        while (true) {
            val count = input.read(buffer)
            if (count < 0) break
            total += count
            require(total <= MAX_PLAYLIST_BYTES) { "The playlist is larger than the 64 MB safety limit." }
            output.write(buffer, 0, count)
        }
        return output.toByteArray()
    }

    private fun decodePlaylist(bytes: ByteArray): String = when {
        bytes.size >= 2 && bytes[0] == 0xFF.toByte() && bytes[1] == 0xFE.toByte() ->
            bytes.copyOfRange(2, bytes.size).toString(Charsets.UTF_16LE)
        bytes.size >= 2 && bytes[0] == 0xFE.toByte() && bytes[1] == 0xFF.toByte() ->
            bytes.copyOfRange(2, bytes.size).toString(Charsets.UTF_16BE)
        else -> bytes.toString(Charsets.UTF_8).trimStart('\uFEFF')
    }

    private fun writeCache(text: String) {
        cacheFile.parentFile?.mkdirs()
        val temporary = File(cacheFile.parentFile, "${cacheFile.name}.new")
        temporary.writeText(text, Charsets.UTF_8)
        if (!temporary.renameTo(cacheFile)) {
            temporary.copyTo(cacheFile, overwrite = true)
            temporary.delete()
        }
    }

    private fun saveSource(
        id: String,
        name: String,
        type: SourceType,
        value: String,
        displayLocation: String
    ) {
        preferences.edit(commit = true) {
            putString(KEY_SOURCE_ID, id)
            putString(KEY_SOURCE_NAME, name)
            putString(KEY_SOURCE_TYPE, type.storedValue)
            putString(KEY_SOURCE_VALUE, value)
            putString(KEY_DISPLAY_LOCATION, displayLocation)
        }
    }

    private fun documentDisplayName(uri: Uri): String? = context.contentResolver
        .query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)
        ?.use { cursor ->
            if (cursor.moveToFirst()) cursor.getString(0) else null
        }

    private fun normalizePlaylistUrl(rawValue: String): String {
        val value = rawValue.trim()
        require(value.isNotEmpty()) { "Enter a playlist URL." }
        val candidate = if ("://" in value) value else "https://$value"
        val uri = runCatching { URI(candidate) }.getOrNull()
        require(uri?.scheme?.lowercase() in setOf("http", "https") && !uri?.host.isNullOrBlank()) {
            "Enter a complete HTTP or HTTPS playlist URL."
        }
        return uri.toASCIIString()
    }

    private fun safeDisplayLocation(source: String): String {
        val uri = URI(source)
        val host = uri.host.orEmpty()
        val displayHost = if (':' in host && !host.startsWith('[')) "[$host]" else host
        val port = if (uri.port < 0) "" else ":${uri.port}"
        return "$displayHost$port"
    }

    private companion object {
        const val KEY_SOURCE_ID = "source_id"
        const val KEY_SOURCE_NAME = "source_name"
        const val KEY_SOURCE_TYPE = "source_type"
        const val KEY_SOURCE_VALUE = "source_value"
        const val KEY_DISPLAY_LOCATION = "display_location"
        const val MAX_PLAYLIST_BYTES = 64 * 1024 * 1024
        const val MAX_HTTP_REDIRECTS = 5
        val REDIRECT_STATUS_CODES = setOf(301, 302, 303, 307, 308)
    }
}
