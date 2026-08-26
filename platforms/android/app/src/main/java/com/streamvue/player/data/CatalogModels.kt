package com.streamvue.player.data

enum class ChannelKind(val wireName: String, val label: String) {
    Live("live", "LIVE"),
    Movie("movie", "MOVIE"),
    Series("series", "SERIES"),
    Recording("recording", "RECORDING"),
    Replay("replay", "REPLAY")
}

data class CatchupMetadata(
    val mode: String,
    val source: String,
    val days: Int,
    val correctionMinutes: Int
)

data class Channel(
    val id: String,
    val number: Int,
    val name: String,
    val streamUri: String,
    val group: String = "Uncategorized",
    val logoUri: String? = null,
    val tvgId: String? = null,
    val tvgName: String? = null,
    val requestHeaders: Map<String, String> = emptyMap(),
    val kind: ChannelKind = ChannelKind.Live,
    val sourceId: String,
    val sourceName: String,
    val catchup: CatchupMetadata? = null
) {
    val initials: String
        get() {
            val words = name.trim().split(Regex("\\s+")).filter(String::isNotBlank)
            return when {
                words.isEmpty() -> "TV"
                words.size == 1 -> words.first().take(2).uppercase()
                else -> words.take(2).joinToString("") { it.first().uppercase() }
            }
        }

    val searchText: String
        get() = listOf(name, group, tvgName.orEmpty(), sourceName).joinToString("\n").uppercase()
}

data class ParsedPlaylist(
    val channels: List<Channel>,
    val guideSource: String?
)

enum class SourceType(val storedValue: String) {
    File("file"),
    Url("url");

    companion object {
        fun fromStored(value: String?): SourceType? = entries.firstOrNull { it.storedValue == value }
    }
}

data class SourceDescriptor(
    val id: String,
    val name: String,
    val type: SourceType,
    val displayLocation: String,
    val refreshOnLaunch: Boolean,
    val usedCachedFallback: Boolean = false
)

data class Catalog(
    val id: String,
    val displayName: String,
    val loadedAt: String,
    val source: SourceDescriptor,
    val guideSources: List<String>,
    val channels: List<Channel>
)

data class LoadedCatalog(
    val catalog: Catalog,
    val notice: String? = null
)
