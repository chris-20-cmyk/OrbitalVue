package com.streamvue.player.data

import com.google.gson.annotations.SerializedName

const val MEDIA_CENTER_CONTRACT_VERSION = "1.0"

enum class MediaCenterProvider(val displayName: String, val sourceType: SourceType) {
    @SerializedName("plex")
    Plex("Plex", SourceType.Plex),

    @SerializedName("emby")
    Emby("Emby", SourceType.Emby)
}

data class MediaCenterConnection(
    @SerializedName("contractVersion") val contractVersion: String = MEDIA_CENTER_CONTRACT_VERSION,
    @SerializedName("provider") val provider: MediaCenterProvider,
    @SerializedName("serverId") val serverId: String,
    @SerializedName("displayName") val displayName: String,
    @SerializedName("baseUrl") val baseUrl: String,
    @SerializedName("displayLocation") val displayLocation: String,
    @SerializedName("credentialId") val credentialId: String,
    @SerializedName("userId") val userId: String? = null
)

enum class MediaCenterLibraryKind {
    @SerializedName("movies") Movies,
    @SerializedName("shows") Shows,
    @SerializedName("recordings") Recordings,
    @SerializedName("live-tv") LiveTv,
    @SerializedName("music") Music,
    @SerializedName("other") Other
}

data class MediaCenterLibrary(
    @SerializedName("id") val id: String,
    @SerializedName("title") val title: String,
    @SerializedName("kind") val kind: MediaCenterLibraryKind,
    @SerializedName("itemCount") val itemCount: Int? = null
)

enum class MediaCenterItemKind {
    @SerializedName("movie") Movie,
    @SerializedName("episode") Episode,
    @SerializedName("video") Video,
    @SerializedName("recording") Recording,
    @SerializedName("live-tv") LiveTv,
    @SerializedName("audio") Audio
}

data class MediaCenterSource(
    @SerializedName("id") val id: String,
    @SerializedName("playbackPath") val playbackPath: String? = null,
    @SerializedName("container") val container: String? = null,
    @SerializedName("videoCodec") val videoCodec: String? = null,
    @SerializedName("audioCodec") val audioCodec: String? = null,
    @SerializedName("width") val width: Int? = null,
    @SerializedName("height") val height: Int? = null,
    @SerializedName("bitrate") val bitrate: Int? = null,
    @SerializedName("supportsDirectPlay") val supportsDirectPlay: Boolean = false,
    @SerializedName("supportsDirectStream") val supportsDirectStream: Boolean = false,
    @SerializedName("supportsTranscode") val supportsTranscode: Boolean = false
)

data class MediaCenterItem(
    @SerializedName("id") val id: String,
    @SerializedName("provider") val provider: MediaCenterProvider,
    @SerializedName("serverId") val serverId: String,
    @SerializedName("libraryId") val libraryId: String,
    @SerializedName("libraryTitle") val libraryTitle: String,
    @SerializedName("kind") val kind: MediaCenterItemKind,
    @SerializedName("title") val title: String,
    @SerializedName("sortTitle") val sortTitle: String? = null,
    @SerializedName("seriesTitle") val seriesTitle: String? = null,
    @SerializedName("seasonNumber") val seasonNumber: Int? = null,
    @SerializedName("episodeNumber") val episodeNumber: Int? = null,
    @SerializedName("year") val year: Int? = null,
    @SerializedName("durationMs") val durationMs: Long? = null,
    @SerializedName("resumePositionMs") val resumePositionMs: Long? = null,
    @SerializedName("played") val played: Boolean = false,
    @SerializedName("addedAt") val addedAt: String? = null,
    @SerializedName("lastPlayedAt") val lastPlayedAt: String? = null,
    @SerializedName("artworkPath") val artworkPath: String? = null,
    @SerializedName("mediaSources") val mediaSources: List<MediaCenterSource> = emptyList()
)

data class MediaCenterSnapshot(
    @SerializedName("contractVersion") val contractVersion: String = MEDIA_CENTER_CONTRACT_VERSION,
    @SerializedName("loadedAt") val loadedAt: String,
    @SerializedName("connection") val connection: MediaCenterConnection,
    @SerializedName("libraries") val libraries: List<MediaCenterLibrary>,
    @SerializedName("items") val items: List<MediaCenterItem>,
    @SerializedName("truncated") val truncated: Boolean = false
)

data class MediaCenterPage(
    val items: List<MediaCenterItem>,
    val start: Int,
    val returnedCount: Int,
    val total: Int
)

data class MediaCenterPlaybackPlan(
    val itemId: String,
    val mediaSourceId: String,
    val url: String,
    val requestHeaders: Map<String, String>,
    val resumePositionMs: Long = 0,
    val requiresPlaybackReporting: Boolean = true
)

data class MediaCenterDeviceIdentity(
    val client: String,
    val device: String,
    val deviceId: String,
    val version: String
)
