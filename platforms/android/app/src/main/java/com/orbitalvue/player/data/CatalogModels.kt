package com.orbitalvue.player.data

import java.time.Duration
import java.time.Instant

enum class ChannelKind(val wireName: String, val label: String) {
    Live("live", "LIVE"),
    Movie("movie", "MOVIE"),
    Series("series", "SERIES"),
    Recording("recording", "RECORDING"),
    Replay("replay", "REPLAY"),
    Music("music", "MUSIC")
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
    val startPositionMs: Long? = null,
    val playbackReportSessionId: String? = null,
    val kind: ChannelKind = ChannelKind.Live,
    val sourceId: String,
    val sourceName: String,
    val catchup: CatchupMetadata? = null,
    val isMediaCenterItem: Boolean = false,
    val libraryId: String? = null,
    val libraryTitle: String? = null,
    val seriesTitle: String? = null,
    val seasonNumber: Int? = null,
    val episodeNumber: Int? = null,
    val year: Int? = null,
    val durationMs: Long? = null,
    val resumePositionMs: Long? = null,
    val played: Boolean = false,
    val addedAt: String? = null,
    val lastPlayedAt: String? = null
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
        get() = listOf(
            name,
            group,
            tvgName.orEmpty(),
            sourceName,
            libraryTitle.orEmpty(),
            seriesTitle.orEmpty(),
            year?.toString().orEmpty()
        ).joinToString("\n").uppercase()

    val canResume: Boolean
        get() {
            val resume = resumePositionMs ?: 0
            val duration = durationMs ?: 0
            return isMediaCenterItem && resume >= 30_000 &&
                (duration <= 0 || resume < duration - 30_000)
        }

    val watchProgress: Float?
        get() {
            val duration = durationMs ?: return null
            val resume = resumePositionMs ?: return null
            if (!canResume || duration <= 0) return null
            return (resume.toDouble() / duration.toDouble()).coerceIn(0.0, 1.0).toFloat()
        }

    val watchProgressLabel: String?
        get() {
            val progress = watchProgress ?: return null
            val remainingMinutes = (((durationMs ?: 0) - (resumePositionMs ?: 0)) / 60_000)
                .coerceAtLeast(1)
            return "Continue • ${(progress * 100).toInt()}% • ${formatMinutes(remainingMinutes)} left"
        }

    val mediaMetadataLine: String?
        get() {
            if (!isMediaCenterItem) return null
            val values = ArrayList<String>(3)
            if (kind == ChannelKind.Series && !seriesTitle.isNullOrBlank()) values += seriesTitle
            else if (!libraryTitle.isNullOrBlank()) values += libraryTitle
            year?.takeIf { it in 1888..3000 }?.let { values += it.toString() }
            durationMs?.takeIf { it > 0 }?.let { values += formatDuration(it) }
            return values.takeIf { it.isNotEmpty() }?.joinToString(" • ") ?: group
        }

    private fun formatDuration(milliseconds: Long): String {
        val totalMinutes = (milliseconds / 60_000).coerceAtLeast(1)
        val hours = totalMinutes / 60
        val minutes = totalMinutes % 60
        return if (hours > 0) "${hours}h ${minutes}m" else "${minutes}m"
    }

    private fun formatMinutes(minutes: Long): String {
        val hours = minutes / 60
        val remainder = minutes % 60
        return if (hours > 0) "${hours}h ${remainder}m" else "${minutes}m"
    }
}

enum class MediaLibraryBrowseMode(val label: String) {
    All("All"),
    ContinueWatching("Continue"),
    RecentlyAdded("Recent"),
    Live("Live"),
    Movies("Movies"),
    Series("Series");

    val isEditorial: Boolean
        get() = this == ContinueWatching || this == RecentlyAdded

    val sectionTitle: String
        get() = when (this) {
            ContinueWatching -> "Continue Watching"
            RecentlyAdded -> "Recently Added"
            else -> label
        }
}

data class MediaLibraryBrowseSummary(
    val isMediaCenterLibrary: Boolean = false,
    val continueWatchingCount: Int = 0,
    val recentlyAddedCount: Int = 0,
    val movieCount: Int = 0,
    val seriesCount: Int = 0
)

object MediaLibraryBrowsePolicy {
    private val recentWindow: Duration = Duration.ofDays(30)
    private val futureAllowance: Duration = Duration.ofDays(1)

    fun summarize(
        channels: List<Channel>,
        now: Instant = Instant.now()
    ): MediaLibraryBrowseSummary = MediaLibraryBrowseSummary(
        isMediaCenterLibrary = channels.any(Channel::isMediaCenterItem),
        continueWatchingCount = channels.count { matches(it, MediaLibraryBrowseMode.ContinueWatching, now) },
        recentlyAddedCount = channels.count { matches(it, MediaLibraryBrowseMode.RecentlyAdded, now) },
        movieCount = channels.count { it.kind == ChannelKind.Movie },
        seriesCount = channels.count { it.kind == ChannelKind.Series }
    )

    fun matches(
        channel: Channel,
        mode: MediaLibraryBrowseMode,
        now: Instant = Instant.now()
    ): Boolean = when (mode) {
        MediaLibraryBrowseMode.All -> true
        MediaLibraryBrowseMode.ContinueWatching -> channel.canResume
        MediaLibraryBrowseMode.RecentlyAdded -> channel.isMediaCenterItem &&
            parseInstant(channel.addedAt)?.let { added ->
                !added.isAfter(now.plus(futureAllowance)) && !added.isBefore(now.minus(recentWindow))
            } == true
        MediaLibraryBrowseMode.Live -> channel.kind == ChannelKind.Live
        MediaLibraryBrowseMode.Movies -> channel.kind == ChannelKind.Movie
        MediaLibraryBrowseMode.Series -> channel.kind == ChannelKind.Series
    }

    fun order(channels: List<Channel>, mode: MediaLibraryBrowseMode): List<Channel> = when (mode) {
        MediaLibraryBrowseMode.ContinueWatching -> channels.sortedWith(
            compareByDescending<Channel> { parseInstant(it.lastPlayedAt) ?: Instant.MIN }
                .thenByDescending { it.resumePositionMs ?: 0 }
                .thenBy(Channel::number)
        )
        MediaLibraryBrowseMode.RecentlyAdded -> channels.sortedWith(
            compareByDescending<Channel> { parseInstant(it.addedAt) ?: Instant.MIN }
                .thenBy(Channel::number)
        )
        else -> channels
    }

    private fun parseInstant(value: String?): Instant? = value?.let {
        runCatching { Instant.parse(it) }.getOrNull()
    }
}

data class ParsedPlaylist(
    val channels: List<Channel>,
    val guideSources: List<String>
)

enum class SourceType(val storedValue: String) {
    File("file"),
    Url("url"),
    Plex("plex"),
    Emby("emby");

    val isMediaCenter: Boolean
        get() = this == Plex || this == Emby

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
