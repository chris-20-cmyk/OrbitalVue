package com.streamvue.player.data

import java.net.URI
import java.security.MessageDigest
import java.util.Locale

object M3uParser {
    private const val DEFAULT_MAX_CHANNELS = 250_000
    private val attributePattern = Regex(
        """([A-Za-z0-9_-]+)=(?:\"([^\"]*)\"|'([^']*)'|([^\s,]+))"""
    )
    private val referrerPattern = Regex(
        """[\"]Referer[\"]\s*:\s*[\"]([^\"]+)""",
        RegexOption.IGNORE_CASE
    )
    private val playableSchemes = setOf("http", "https", "rtsp", "rtmp", "udp", "file")

    fun parse(
        text: String,
        sourceId: String,
        sourceName: String,
        maximumChannels: Int = DEFAULT_MAX_CHANNELS
    ): ParsedPlaylist {
        require(sourceId.isNotBlank() && sourceName.isNotBlank()) { "A source ID and source name are required." }
        require(maximumChannels > 0) { "The channel safety limit must be positive." }
        val channels = ArrayList<Channel>(minOf(16_384, maximumChannels))
        var pending: PendingChannel? = null
        var guideSources = emptyList<String>()

        text.lineSequence().forEach { rawLine ->
            val line = rawLine.trim().trimStart('\uFEFF')
            if (line.isEmpty()) return@forEach

            when {
                line.startsWith("#EXTM3U", ignoreCase = true) -> {
                    if (guideSources.isEmpty()) guideSources = parseGuideSources(line)
                }

                line.startsWith("#EXTINF", ignoreCase = true) -> {
                    pending = parseMetadata(line)
                }

                pending != null && line.startsWith("#EXTVLCOPT:http-user-agent=", ignoreCase = true) -> {
                    pending.userAgent = line.substringAfter('=').trim()
                }

                pending != null && (
                    line.startsWith("#EXTVLCOPT:http-referrer=", ignoreCase = true) ||
                        line.startsWith("#EXTHTTP:", ignoreCase = true)
                    ) -> {
                    pending.referrer = extractReferrer(line)
                }

                line.startsWith('#') -> Unit
                looksPlayable(line) -> {
                    require(channels.size < maximumChannels) {
                        "The playlist exceeds the ${"%,d".format(maximumChannels)} channel safety limit."
                    }
                    val metadata = pending ?: PendingChannel(name = "Channel ${channels.size + 1}")
                    val name = metadata.name.trim().ifEmpty { "Channel ${channels.size + 1}" }
                    val group = metadata.group?.trim().orEmpty().ifEmpty { "Uncategorized" }
                    val headers = buildMap {
                        metadata.userAgent.clean()?.let { put("User-Agent", it) }
                        metadata.referrer.clean()?.let { put("Referer", it) }
                    }
                    val catchup = metadata.catchupSource.clean()?.let { source ->
                        CatchupMetadata(
                            mode = metadata.catchupMode.clean() ?: "default",
                            source = source,
                            days = metadata.catchupDays.coerceIn(0, 365),
                            correctionMinutes = metadata.catchupCorrectionMinutes.coerceIn(-1_440, 1_440)
                        )
                    }

                    channels += Channel(
                        id = stableChannelId(metadata.tvgId, name, group, line),
                        number = channels.size + 1,
                        name = name,
                        streamUri = line,
                        group = group,
                        logoUri = metadata.logoUri.clean(),
                        tvgId = metadata.tvgId.clean(),
                        tvgName = metadata.tvgName.clean(),
                        requestHeaders = headers,
                        kind = inferKind(group, line),
                        sourceId = sourceId,
                        sourceName = sourceName,
                        catchup = catchup
                    )
                    pending = null
                }
            }
        }

        require(channels.isNotEmpty()) {
            "No playable entries were found. Choose an M3U or M3U8 playlist that contains stream URLs."
        }

        return ParsedPlaylist(channels = channels, guideSources = guideSources)
    }

    fun stableChannelId(tvgId: String?, name: String, group: String, streamUri: String): String {
        val endpoint = streamUri.trim().substringBefore('?').substringBefore('#')
        val identity = if (!tvgId.isNullOrBlank()) {
            "tvg:${tvgId.trim().uppercase(Locale.ROOT)}|name:${name.trim().uppercase(Locale.ROOT)}|" +
                "group:${group.trim().uppercase(Locale.ROOT)}|endpoint:$endpoint"
        } else {
            "name:${name.trim().uppercase(Locale.ROOT)}|group:${group.trim().uppercase(Locale.ROOT)}|" +
                "endpoint:$endpoint"
        }
        return MessageDigest.getInstance("SHA-256")
            .digest(identity.toByteArray(Charsets.UTF_8))
            .joinToString("") { "%02X".format(it) }
    }

    private fun parseGuideSources(line: String): List<String> {
        val attributes = parseAttributes(line)
        sequenceOf("url-tvg", "x-tvg-url", "tvg-url").forEach { key ->
            val sources = attributes[key]
                ?.split(',')
                ?.map(String::trim)
                ?.filter(::isGuideUri)
                .orEmpty()
            if (sources.isNotEmpty()) return sources
        }
        return emptyList()
    }

    private fun parseMetadata(line: String): PendingChannel {
        val separator = findNameSeparator(line)
        val metadata = if (separator >= 0) line.substring(0, separator) else line
        val listedName = if (separator in 0 until line.lastIndex) line.substring(separator + 1).trim() else ""
        val attributes = parseAttributes(metadata)
        val tvgName = attributes["tvg-name"]
        val catchupDays = (attributes["catchup-days"] ?: attributes["timeshift"])
            ?.toIntOrNull()
            ?.coerceIn(0, 365)
            ?: 0
        val correctionMinutes = attributes["catchup-correction"]
            ?.toDoubleOrNull()
            ?.times(60.0)
            ?.toInt()
            ?.coerceIn(-1_440, 1_440)
            ?: 0

        return PendingChannel(
            name = listedName.ifBlank { tvgName.orEmpty() },
            group = attributes["group-title"],
            logoUri = attributes["tvg-logo"],
            tvgId = attributes["tvg-id"],
            tvgName = tvgName,
            userAgent = attributes["http-user-agent"],
            referrer = attributes["http-referrer"],
            catchupMode = attributes["catchup"],
            catchupSource = attributes["catchup-source"],
            catchupDays = catchupDays,
            catchupCorrectionMinutes = correctionMinutes
        )
    }

    private fun parseAttributes(value: String): Map<String, String> = buildMap {
        attributePattern.findAll(value).forEach { match ->
            val parsed = match.groups[2]?.value
                ?: match.groups[3]?.value
                ?: match.groups[4]?.value
                ?: ""
            put(match.groupValues[1].lowercase(Locale.ROOT), parsed)
        }
    }

    private fun findNameSeparator(line: String): Int {
        var quote: Char? = null
        line.forEachIndexed { index, character ->
            when {
                character == '\"' || character == '\'' -> {
                    quote = when (quote) {
                        null -> character
                        character -> null
                        else -> quote
                    }
                }

                character == ',' && quote == null -> return index
            }
        }
        return -1
    }

    private fun extractReferrer(line: String): String? {
        if ('=' in line) return line.substringAfter('=').trim().clean()
        return referrerPattern.find(line)?.groupValues?.getOrNull(1)?.clean()
    }

    private fun looksPlayable(value: String): Boolean = runCatching {
        val uri = URI(value)
        val scheme = uri.scheme?.lowercase(Locale.ROOT)
        scheme in playableSchemes && (scheme !in setOf("http", "https") || !uri.host.isNullOrBlank())
    }.getOrDefault(false)

    private fun isGuideUri(value: String): Boolean = runCatching {
        URI(value).scheme?.lowercase(Locale.ROOT) in setOf("http", "https", "file")
    }.getOrDefault(false)

    private fun inferKind(group: String, streamUri: String): ChannelKind {
        val value = "$group $streamUri".lowercase(Locale.ROOT)
        return when {
            "/series/" in value || "series" in value || "shows" in value -> ChannelKind.Series
            "/movie/" in value || "movie" in value || "vod" in value || "cinema" in value -> ChannelKind.Movie
            else -> ChannelKind.Live
        }
    }

    private fun String?.clean(): String? = this?.trim()?.takeIf(String::isNotEmpty)

    private data class PendingChannel(
        var name: String = "",
        var group: String? = null,
        var logoUri: String? = null,
        var tvgId: String? = null,
        var tvgName: String? = null,
        var userAgent: String? = null,
        var referrer: String? = null,
        var catchupMode: String? = null,
        var catchupSource: String? = null,
        var catchupDays: Int = 0,
        var catchupCorrectionMinutes: Int = 0
    )
}
