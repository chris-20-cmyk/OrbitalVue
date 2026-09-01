package com.streamvue.player.data

import android.content.Context
import androidx.core.content.edit
import com.google.gson.Gson
import com.google.gson.GsonBuilder
import com.streamvue.player.premium.PremiumAccessPolicy
import com.streamvue.player.premium.PremiumAccessSnapshot
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.net.URI
import java.time.Instant
import java.util.Locale
import java.util.UUID

class MediaCenterRepository internal constructor(
    private val context: Context,
    private val gson: Gson = GsonBuilder().disableHtmlEscaping().create(),
    private val service: MediaCenterService = defaultService(context, gson),
    private val premiumAccess: PremiumAccessSnapshot = PremiumAccessPolicy.current(),
    private val premiumAccessProvider: (() -> PremiumAccessSnapshot)? = null
) {
    private val snapshotFile = File(context.filesDir, "catalog/media-center-source.json")
    private var cachedSnapshot: MediaCenterSnapshot? = null

    private fun currentPremiumAccess(): PremiumAccessSnapshot =
        premiumAccessProvider?.invoke() ?: premiumAccess

    suspend fun loadSaved(): LoadedCatalog? = withContext(Dispatchers.IO) {
        if (!currentPremiumAccess().canUseMediaCenters) return@withContext null
        if (!snapshotFile.exists()) return@withContext null
        val saved = readSnapshot()
        runCatching {
            service.snapshot(saved.connection).also(::persist)
        }.fold(
            onSuccess = { refreshed ->
                cachedSnapshot = refreshed
                loadedCatalog(
                    refreshed,
                    "${refreshed.connection.provider.displayName} refreshed at launch",
                    usedCachedFallback = false
                )
            },
            onFailure = {
                cachedSnapshot = saved
                loadedCatalog(
                    saved,
                    "The media server could not be refreshed. OrbitalVue opened the last protected library snapshot.",
                    usedCachedFallback = true
                )
            }
        )
    }

    suspend fun connectPlex(
        serverAddress: String,
        token: String,
        displayName: String? = null,
        allowInsecureHttp: Boolean = false
    ): LoadedCatalog = withContext(Dispatchers.IO) {
        currentPremiumAccess().requireMediaCenters()
        val previous = runCatching { currentSnapshot()?.connection }.getOrNull()
        val connection = service.connectPlex(serverAddress, token, displayName, allowInsecureHttp)
        activate(connection, previous, "Plex library connected")
    }

    suspend fun createPlexSignInChallenge(): PlexPinChallenge = withContext(Dispatchers.IO) {
        currentPremiumAccess().requireMediaCenters()
        service.createPlexSignInChallenge().also {
            currentPremiumAccess().requireMediaCenters()
        }
    }

    suspend fun completePlexSignIn(challenge: PlexPinChallenge): PlexServerDiscovery? =
        withContext(Dispatchers.IO) {
            currentPremiumAccess().requireMediaCenters()
            val discovery = service.completePlexSignIn(challenge)
            if (!currentPremiumAccess().canUseMediaCenters) {
                discovery?.let { service.cancelPlexDiscovery(it.sessionId) }
                currentPremiumAccess().requireMediaCenters()
            }
            discovery
        }

    suspend fun connectDiscoveredPlexServer(
        sessionId: String,
        serverId: String,
        connectionUrl: String,
        allowInsecureHttp: Boolean = false
    ): LoadedCatalog = withContext(Dispatchers.IO) {
        currentPremiumAccess().requireMediaCenters()
        val previous = runCatching { currentSnapshot()?.connection }.getOrNull()
        val connection = service.connectDiscoveredPlexServer(
            sessionId = sessionId,
            serverId = serverId,
            connectionUrl = connectionUrl,
            allowInsecureHttp = allowInsecureHttp
        )
        try {
            currentPremiumAccess().requireMediaCenters()
            activate(connection, previous, "Plex account server connected")
        } catch (error: Throwable) {
            runCatching { service.disconnect(connection) }
            throw error
        }
    }

    suspend fun cancelPlexDiscovery(sessionId: String) = withContext(Dispatchers.IO) {
        service.cancelPlexDiscovery(sessionId)
    }

    suspend fun connectEmby(
        serverAddress: String,
        username: String,
        password: String,
        displayName: String? = null,
        allowInsecureHttp: Boolean = false
    ): LoadedCatalog = withContext(Dispatchers.IO) {
        currentPremiumAccess().requireMediaCenters()
        val previous = runCatching { currentSnapshot()?.connection }.getOrNull()
        val connection = service.connectEmby(
            serverAddress,
            username,
            password,
            displayName,
            allowInsecureHttp
        )
        activate(connection, previous, "Emby library connected")
    }

    suspend fun refreshCurrent(): LoadedCatalog? = withContext(Dispatchers.IO) {
        currentPremiumAccess().requireMediaCenters()
        val saved = currentSnapshot() ?: return@withContext null
        runCatching { service.snapshot(saved.connection).also(::persist) }.fold(
            onSuccess = { refreshed ->
                cachedSnapshot = refreshed
                loadedCatalog(
                    refreshed,
                    "${saved.connection.provider.displayName} library refreshed",
                    usedCachedFallback = false
                )
            },
            onFailure = {
                loadedCatalog(
                    saved,
                    "The media server could not be refreshed. OrbitalVue kept the last protected library snapshot.",
                    usedCachedFallback = true
                )
            }
        )
    }

    suspend fun resolvePlayback(channel: Channel): Channel = withContext(Dispatchers.IO) {
        currentPremiumAccess().requireMediaCenters()
        val locator = MediaCenterLocator.parsePlaybackUri(channel.streamUri)
        val snapshot = currentSnapshot() ?: error("Connect the media server again before playing this item.")
        require(locator.provider == snapshot.connection.provider &&
            locator.serverId == snapshot.connection.serverId) {
            "The protected media locator does not belong to the active server."
        }
        val item = snapshot.items.firstOrNull { it.id == locator.itemId }
            ?: error("This media item is no longer available in the protected library snapshot.")
        val plan = service.playbackPlan(snapshot.connection, item)
        channel.copy(
            streamUri = plan.url,
            requestHeaders = plan.requestHeaders,
            startPositionMs = plan.resumePositionMs.takeIf { it > 0 }
        )
    }

    suspend fun removeSource() = withContext(Dispatchers.IO) {
        val connection = runCatching { currentSnapshot()?.connection }.getOrNull()
        connection?.let(service::disconnect)
        cachedSnapshot = null
        if (snapshotFile.exists() && !snapshotFile.delete()) {
            error("Android could not remove the protected media-center snapshot.")
        }
    }

    private fun activate(
        connection: MediaCenterConnection,
        previous: MediaCenterConnection?,
        notice: String
    ): LoadedCatalog {
        return runCatching {
            val snapshot = service.snapshot(connection)
            currentPremiumAccess().requireMediaCenters()
            persist(snapshot)
            snapshot
        }.fold(
            onSuccess = { snapshot ->
                cachedSnapshot = snapshot
                if (previous != null && previous.credentialId != connection.credentialId) {
                    runCatching { service.disconnect(previous) }
                }
                loadedCatalog(snapshot, notice, usedCachedFallback = false)
            },
            onFailure = { error ->
                runCatching { service.disconnect(connection) }
                throw error
            }
        )
    }

    private fun currentSnapshot(): MediaCenterSnapshot? {
        cachedSnapshot?.let { return it }
        if (!snapshotFile.exists()) return null
        return readSnapshot().also { cachedSnapshot = it }
    }

    private fun readSnapshot(): MediaCenterSnapshot {
        require(snapshotFile.length() in 1..MAX_SNAPSHOT_BYTES.toLong()) {
            "The protected media-center snapshot is invalid or too large."
        }
        val json = snapshotFile.readText(Charsets.UTF_8)
        val snapshot = runCatching { gson.fromJson(json, MediaCenterSnapshot::class.java) }
            .getOrElse { error("The protected media-center snapshot is damaged.") }
        validateSnapshot(snapshot)
        return snapshot
    }

    private fun persist(snapshot: MediaCenterSnapshot) {
        validateSnapshot(snapshot)
        val json = gson.toJson(snapshot)
        require(json.toByteArray(Charsets.UTF_8).size <= MAX_SNAPSHOT_BYTES) {
            "The media-center library snapshot exceeds the 64 MB safety limit."
        }
        require(!SENSITIVE_JSON_KEY.containsMatchIn(json)) {
            "A protected credential was blocked from the media-center snapshot."
        }
        snapshotFile.parentFile?.mkdirs()
        val temporary = File(snapshotFile.parentFile, "${snapshotFile.name}.new")
        temporary.writeText(json, Charsets.UTF_8)
        if (!temporary.renameTo(snapshotFile)) {
            temporary.copyTo(snapshotFile, overwrite = true)
            require(temporary.delete()) { "Android could not finalize the protected snapshot." }
        }
    }

    private fun validateSnapshot(snapshot: MediaCenterSnapshot) {
        require(snapshot.contractVersion == MEDIA_CENTER_CONTRACT_VERSION &&
            snapshot.connection.contractVersion == MEDIA_CENTER_CONTRACT_VERSION) {
            "This saved media-center source uses an unsupported format."
        }
        val connection = snapshot.connection
        MediaCenterUrlPolicy.requireIdentifier(connection.serverId, "server")
        MediaCenterUrlPolicy.requireIdentifier(connection.credentialId, "credential reference")
        connection.userId?.let { MediaCenterUrlPolicy.requireIdentifier(it, "user") }
        require(MediaCenterUrlPolicy.normalizeBaseUrl(connection.baseUrl).toASCIIString() == connection.baseUrl) {
            "The saved media-center address is unsafe."
        }
        val libraryIds = snapshot.libraries.mapTo(HashSet(), MediaCenterLibrary::id)
        snapshot.libraries.forEach { library ->
            MediaCenterUrlPolicy.requireIdentifier(library.id, "library")
            require(library.title.isNotBlank() && library.title.length <= 512) {
                "The saved media-center library is invalid."
            }
        }
        snapshot.items.forEach { item ->
            require(item.provider == connection.provider && item.serverId == connection.serverId &&
                item.libraryId in libraryIds && item.title.isNotBlank()) {
                "The saved media-center item does not belong to this source."
            }
            MediaCenterUrlPolicy.requireIdentifier(item.id, "item")
            item.mediaSources.forEach { source ->
                MediaCenterUrlPolicy.requireIdentifier(source.id, "media source")
                require(source.playbackPath == null ||
                    (!source.playbackPath.contains('\n') && !source.playbackPath.contains('\r') &&
                        source.playbackPath.length <= 2_048)) {
                    "The saved media playback path is unsafe."
                }
            }
        }
    }

    private fun loadedCatalog(
        snapshot: MediaCenterSnapshot,
        notice: String,
        usedCachedFallback: Boolean
    ): LoadedCatalog {
        val connection = snapshot.connection
        val sourceId = "MC-${MediaCenterUrlPolicy.hash("${connection.provider}|${connection.serverId}").take(48)}"
        val channels = snapshot.items.mapIndexed { index, item ->
            Channel(
                id = MediaCenterUrlPolicy.hash(
                    "media-center|${item.provider}|${item.serverId}|${item.id}"
                ),
                number = index + 1,
                name = displayTitle(item),
                streamUri = MediaCenterLocator.playbackUri(item.provider, item.serverId, item.id),
                group = item.libraryTitle,
                logoUri = item.artworkPath?.let {
                    MediaCenterLocator.artworkUri(item.provider, item.serverId, item.id)
                },
                kind = when (item.kind) {
                    MediaCenterItemKind.Movie, MediaCenterItemKind.Video -> ChannelKind.Movie
                    MediaCenterItemKind.Episode -> ChannelKind.Series
                    MediaCenterItemKind.Recording -> ChannelKind.Recording
                    MediaCenterItemKind.LiveTv -> ChannelKind.Live
                    MediaCenterItemKind.Audio -> ChannelKind.Replay
                },
                sourceId = sourceId,
                sourceName = connection.displayName
            )
        }
        val truncationNotice = if (snapshot.truncated) {
            "$notice. The first ${channels.size.toStringWithCommas()} items are indexed on this device."
        } else {
            notice
        }
        return LoadedCatalog(
            Catalog(
                id = "MC-${MediaCenterUrlPolicy.hash("${connection.provider}|${connection.serverId}|catalog").take(48)}",
                displayName = "${connection.displayName} • ${connection.provider.displayName}",
                loadedAt = snapshot.loadedAt.ifBlank { Instant.now().toString() },
                source = SourceDescriptor(
                    id = sourceId,
                    name = connection.displayName,
                    type = connection.provider.sourceType,
                    displayLocation = connection.displayLocation,
                    refreshOnLaunch = true,
                    usedCachedFallback = usedCachedFallback
                ),
                guideSources = emptyList(),
                channels = channels
            ),
            truncationNotice
        )
    }

    private fun displayTitle(item: MediaCenterItem): String {
        if (item.kind != MediaCenterItemKind.Episode || item.seriesTitle.isNullOrBlank()) return item.title
        val episode = when {
            item.seasonNumber != null && item.episodeNumber != null ->
                "S${item.seasonNumber.toString().padStart(2, '0')}E${item.episodeNumber.toString().padStart(2, '0')}"
            item.episodeNumber != null -> "E${item.episodeNumber}"
            else -> null
        }
        return listOfNotNull(item.seriesTitle, episode, item.title).joinToString(" • ")
    }

    private fun Int.toStringWithCommas(): String = String.format(Locale.getDefault(), "%,d", this)

    private companion object {
        const val MAX_SNAPSHOT_BYTES = 64 * 1_024 * 1_024
        val SENSITIVE_JSON_KEY = Regex(
            "\\\"(?:accessToken|authToken|password|secret|x-emby-token|x-plex-token)\\\"\\s*:",
            RegexOption.IGNORE_CASE
        )

        fun deviceIdentity(context: Context): MediaCenterDeviceIdentity {
            val preferences = context.getSharedPreferences("streamvue-device-v1", Context.MODE_PRIVATE)
            val id = preferences.getString("device_id", null)
                ?.takeIf(String::isNotBlank)
                ?: UUID.randomUUID().toString().also { generated ->
                    preferences.edit(commit = true) { putString("device_id", generated) }
                }
            return MediaCenterDeviceIdentity(
                client = "OrbitalVue",
                device = "Android",
                deviceId = id,
                version = "5.6.0"
            )
        }

        fun defaultService(context: Context, gson: Gson): MediaCenterService {
            val device = deviceIdentity(context)
            return MediaCenterService(
                transport = UrlConnectionMediaCenterTransport(),
                credentialVault = AndroidKeystoreCredentialVault(context, gson),
                device = device,
                gson = gson,
                plexAccountClient = PlexAccountClient(
                    transport = UrlConnectionMediaCenterTransport(),
                    signer = AndroidPlexDeviceSigner(context, gson),
                    device = device,
                    gson = gson
                )
            )
        }
    }
}
