package com.streamvue.player.data

import com.google.gson.Gson
import com.google.gson.JsonObject
import com.google.gson.annotations.SerializedName
import java.net.URI
import java.net.URLEncoder
import java.nio.charset.StandardCharsets
import java.time.Instant
import java.util.Locale
import java.util.UUID
import java.util.concurrent.CancellationException

internal class MediaCenterService(
    private val transport: MediaCenterTransport,
    private val credentialVault: MediaCenterCredentialVault,
    private val device: MediaCenterDeviceIdentity,
    private val gson: Gson = Gson(),
    private val plexAccountClient: PlexAccountClient? = null
) {
    private val plexDiscoveryLock = Any()
    private val plexDiscoverySessions = HashMap<String, PlexAccountDiscoverySecret>()
    private val plexDiscoveryConnectionsInFlight = HashSet<String>()
    private val cancelledPlexDiscoverySessions = HashSet<String>()

    fun connectPlex(
        serverAddress: String,
        rawToken: String,
        displayName: String? = null,
        allowInsecureHttp: Boolean = false,
        expectedServerId: String? = null
    ): MediaCenterConnection {
        val baseUrl = MediaCenterUrlPolicy.normalizeBaseUrl(serverAddress)
        MediaCenterUrlPolicy.requireAllowedTransport(baseUrl, allowInsecureHttp)
        val token = MediaCenterUrlPolicy.credential(rawToken)
        val identity = discoverPlexIdentity(baseUrl)
        expectedServerId?.let {
            require(identity.serverId == MediaCenterUrlPolicy.requireIdentifier(it, "Plex server")) {
                "The selected Plex server identity changed before connection."
            }
        }
        val name = MediaCenterUrlPolicy.safeMetadata(displayName)
            ?: MediaCenterUrlPolicy.safeMetadata(identity.name, token)
            ?: "Plex"
        val credentialId = credentialReference(
            MediaCenterProvider.Plex,
            identity.serverId,
            baseUrl,
            null
        )
        val connection = MediaCenterConnection(
            provider = MediaCenterProvider.Plex,
            serverId = identity.serverId,
            displayName = name,
            baseUrl = baseUrl.toASCIIString(),
            displayLocation = MediaCenterUrlPolicy.safeDisplayLocation(baseUrl),
            credentialId = credentialId
        )
        saveCredential(connection, token, allowInsecureHttp)
        return connection
    }

    fun createPlexSignInChallenge(): PlexPinChallenge = accountClient().createPin()

    fun completePlexSignIn(challenge: PlexPinChallenge): PlexServerDiscovery? {
        val client = accountClient()
        val accountToken = client.claimPin(challenge) ?: return null
        client.verifyAccountToken(accountToken.value)
        val servers = client.discoverServers(accountToken.value)
        require(servers.isNotEmpty()) { "Plex did not provide a usable personal media server." }
        val maximumExpiry = Instant.now().plusSeconds(PLEX_DISCOVERY_LIFETIME_SECONDS)
        val expiresAt = accountToken.expiresAt?.takeIf { it.isBefore(maximumExpiry) } ?: maximumExpiry
        require(expiresAt.isAfter(Instant.now())) { "The Plex account session expired." }
        val sessionId = "plex-discovery-${UUID.randomUUID().toString().lowercase(Locale.ROOT)}"
        synchronized(plexDiscoveryLock) {
            purgeExpiredPlexDiscoverySessions()
            plexDiscoverySessions[sessionId] = PlexAccountDiscoverySecret(servers, expiresAt)
        }
        return PlexServerDiscovery(
            sessionId = sessionId,
            servers = servers.map(PlexAccountServerSecret::server),
            expiresAt = expiresAt
        )
    }

    fun connectDiscoveredPlexServer(
        sessionId: String,
        serverId: String,
        connectionUrl: String,
        allowInsecureHttp: Boolean = false
    ): MediaCenterConnection {
        val normalizedSessionId = MediaCenterUrlPolicy.requireIdentifier(sessionId, "Plex discovery session")
        val normalizedServerId = MediaCenterUrlPolicy.requireIdentifier(serverId, "Plex server")
        val selection = synchronized(plexDiscoveryLock) {
            purgeExpiredPlexDiscoverySessions()
            val session = plexDiscoverySessions[normalizedSessionId]
                ?: error("The Plex server discovery session expired. Sign in again.")
            require(normalizedSessionId !in plexDiscoveryConnectionsInFlight) {
                "This Plex server is already connecting."
            }
            val secret = session.servers.firstOrNull { it.server.serverId == normalizedServerId }
                ?: error("The selected Plex server was not part of this discovery session.")
            val connection = secret.server.connections.firstOrNull { it.url == connectionUrl }
                ?: error("The selected Plex connection was not part of this discovery session.")
            plexDiscoveryConnectionsInFlight += normalizedSessionId
            secret to connection
        }

        try {
            val (secret, selected) = selection
            require(selected.isSecure || allowInsecureHttp) {
                "Approve the unencrypted local HTTP connection before continuing."
            }
            val connection = connectPlex(
                serverAddress = selected.url,
                rawToken = secret.accessToken,
                displayName = secret.server.name,
                allowInsecureHttp = !selected.isSecure && allowInsecureHttp,
                expectedServerId = secret.server.serverId
            )
            val wasCancelled = synchronized(plexDiscoveryLock) {
                normalizedSessionId in cancelledPlexDiscoverySessions ||
                    normalizedSessionId !in plexDiscoverySessions
            }
            if (wasCancelled) {
                disconnect(connection)
                throw CancellationException("Plex server connection was cancelled.")
            }
            synchronized(plexDiscoveryLock) {
                plexDiscoverySessions.remove(normalizedSessionId)
            }
            return connection
        } finally {
            synchronized(plexDiscoveryLock) {
                plexDiscoveryConnectionsInFlight.remove(normalizedSessionId)
                cancelledPlexDiscoverySessions.remove(normalizedSessionId)
            }
        }
    }

    fun cancelPlexDiscovery(sessionId: String) {
        val normalized = runCatching {
            MediaCenterUrlPolicy.requireIdentifier(sessionId, "Plex discovery session")
        }.getOrNull() ?: return
        synchronized(plexDiscoveryLock) {
            plexDiscoverySessions.remove(normalized)
            if (normalized in plexDiscoveryConnectionsInFlight) {
                cancelledPlexDiscoverySessions += normalized
            } else {
                cancelledPlexDiscoverySessions.remove(normalized)
            }
        }
    }

    fun connectEmby(
        serverAddress: String,
        rawUsername: String,
        password: String,
        displayName: String? = null,
        allowInsecureHttp: Boolean = false
    ): MediaCenterConnection {
        val baseUrl = MediaCenterUrlPolicy.normalizeBaseUrl(serverAddress)
        MediaCenterUrlPolicy.requireAllowedTransport(baseUrl, allowInsecureHttp)
        val username = rawUsername.trim()
        require(username.isNotEmpty() && username.toByteArray().size <= 256 &&
            password.isNotEmpty() && password.toByteArray().size <= 16_384) {
            "Enter a valid Emby username and password."
        }
        val apiBase = embyApiBase(baseUrl)
        val publicIdentity = discoverEmbyIdentity(apiBase)
        val body = JsonObject().apply {
            addProperty("Username", username)
            addProperty("Pw", password)
        }
        val payload = MediaCenterApi.json(
            MediaHttpRequest(
                method = MediaHttpMethod.POST,
                url = MediaCenterUrlPolicy.resolveServerPath(apiBase, "/Users/AuthenticateByName"),
                headers = mapOf(
                    "Accept" to "application/json",
                    "Content-Type" to "application/json",
                    "X-Emby-Authorization" to embyAuthorization()
                ),
                body = gson.toJson(body).toByteArray(Charsets.UTF_8)
            ),
            transport
        ).objectValue()
        val token = MediaCenterUrlPolicy.credential(
            payload.text("AccessToken") ?: error("Emby did not return an access token.")
        )
        val serverId = MediaCenterUrlPolicy.requireIdentifier(
            payload.text("ServerId") ?: error("Emby did not identify the authenticated server."),
            "Emby server"
        )
        require(serverId == publicIdentity.serverId) {
            "The Emby sign-in response came from a different server."
        }
        val userObject = payload.get("User")?.objectValue() ?: JsonObject()
        val userId = MediaCenterUrlPolicy.requireIdentifier(
            userObject.text("Id") ?: error("Emby did not identify the signed-in user."),
            "Emby user"
        )
        val name = MediaCenterUrlPolicy.safeMetadata(displayName)
            ?: MediaCenterUrlPolicy.safeMetadata(publicIdentity.name, token)
            ?: "Emby"
        val credentialId = credentialReference(
            MediaCenterProvider.Emby,
            serverId,
            baseUrl,
            userId
        )
        val connection = MediaCenterConnection(
            provider = MediaCenterProvider.Emby,
            serverId = serverId,
            displayName = name,
            baseUrl = baseUrl.toASCIIString(),
            displayLocation = MediaCenterUrlPolicy.safeDisplayLocation(baseUrl),
            credentialId = credentialId,
            userId = userId
        )
        saveCredential(connection, token, allowInsecureHttp)
        return connection
    }

    fun snapshot(connection: MediaCenterConnection): MediaCenterSnapshot {
        validateConnection(connection)
        val token = credential(connection)
        val libraries = when (connection.provider) {
            MediaCenterProvider.Plex -> plexLibraries(connection, token)
            MediaCenterProvider.Emby -> embyLibraries(connection, token)
        }
        val items = ArrayList<MediaCenterItem>()
        var truncated = false
        for (library in libraries) {
            var start = 0
            while (items.size < MAX_TOTAL_ITEMS) {
                val remaining = MAX_TOTAL_ITEMS - items.size
                val pageSize = minOf(PAGE_SIZE, remaining)
                val page = when (connection.provider) {
                    MediaCenterProvider.Plex -> plexItems(connection, token, library, start, pageSize)
                    MediaCenterProvider.Emby -> embyItems(connection, token, library, start, pageSize)
                }
                items += page.items
                start += page.returnedCount
                if (page.returnedCount == 0 || start >= page.total) break
            }
            if (items.size >= MAX_TOTAL_ITEMS) {
                truncated = true
                break
            }
        }
        return MediaCenterSnapshot(
            loadedAt = Instant.now().toString(),
            connection = connection,
            libraries = libraries,
            items = items,
            truncated = truncated
        )
    }

    fun playbackPlan(
        connection: MediaCenterConnection,
        item: MediaCenterItem,
        requestedMediaSourceId: String? = null
    ): MediaCenterPlaybackPlan {
        require(item.provider == connection.provider && item.serverId == connection.serverId) {
            "The selected media item does not belong to this server."
        }
        val token = credential(connection)
        return when (connection.provider) {
            MediaCenterProvider.Plex -> plexPlaybackPlan(connection, token, item, requestedMediaSourceId)
            MediaCenterProvider.Emby -> embyPlaybackPlan(connection, token, item, requestedMediaSourceId)
        }
    }

    fun disconnect(connection: MediaCenterConnection) {
        credentialVault.remove(connection.credentialId)
    }

    private fun accountClient(): PlexAccountClient = plexAccountClient
        ?: error("Secure Plex account sign-in is unavailable on this device.")

    private fun purgeExpiredPlexDiscoverySessions() {
        val current = Instant.now()
        plexDiscoverySessions.entries.removeAll { (sessionId, value) ->
            val expired = !value.expiresAt.isAfter(current)
            if (expired && sessionId !in plexDiscoveryConnectionsInFlight) {
                cancelledPlexDiscoverySessions.remove(sessionId)
            }
            expired
        }
    }

    private fun plexLibraries(
        connection: MediaCenterConnection,
        token: String
    ): List<MediaCenterLibrary> {
        val container = plexGet(connection, token, "/library/sections").objectAt("MediaContainer")
        return container.arrayAt("Directory").mapNotNull { element ->
            val raw = element.objectValue()
            val id = raw.text("key")?.let { runCatching {
                MediaCenterUrlPolicy.requireIdentifier(it, "Plex library")
            }.getOrNull() } ?: return@mapNotNull null
            val title = MediaCenterUrlPolicy.safeMetadata(raw.text("title"), token) ?: return@mapNotNull null
            MediaCenterLibrary(
                id = id,
                title = title,
                kind = when (raw.text("type")?.lowercase(Locale.ROOT)) {
                    "movie" -> MediaCenterLibraryKind.Movies
                    "show" -> MediaCenterLibraryKind.Shows
                    "recording" -> MediaCenterLibraryKind.Recordings
                    "livetv" -> MediaCenterLibraryKind.LiveTv
                    "artist", "music" -> MediaCenterLibraryKind.Music
                    else -> MediaCenterLibraryKind.Other
                },
                itemCount = raw.integer("totalSize") ?: raw.integer("size")
            )
        }
    }

    private fun plexItems(
        connection: MediaCenterConnection,
        token: String,
        library: MediaCenterLibrary,
        start: Int,
        size: Int
    ): MediaCenterPage {
        val payload = plexGet(
            connection,
            token,
            "/library/sections/${pathComponent(library.id)}/all",
            mapOf(
                "X-Plex-Container-Start" to start.toString(),
                "X-Plex-Container-Size" to size.toString()
            )
        ).objectAt("MediaContainer")
        val items = payload.arrayAt("Metadata").mapNotNull { element ->
            parsePlexItem(connection, token, library, element.objectValue())
        }
        return MediaCenterPage(
            items = items,
            start = payload.integer("offset") ?: start,
            returnedCount = payload.arrayAt("Metadata").size(),
            total = maxOf(items.size, payload.integer("totalSize") ?: payload.integer("size") ?: items.size)
        )
    }

    private fun parsePlexItem(
        connection: MediaCenterConnection,
        token: String,
        library: MediaCenterLibrary,
        raw: JsonObject
    ): MediaCenterItem? {
        val id = raw.text("ratingKey")?.let { runCatching {
            MediaCenterUrlPolicy.requireIdentifier(it, "Plex item")
        }.getOrNull() } ?: return null
        val title = MediaCenterUrlPolicy.safeMetadata(raw.text("title"), token) ?: return null
        val kind = when (raw.text("type")?.lowercase(Locale.ROOT)) {
            "movie" -> MediaCenterItemKind.Movie
            "episode" -> MediaCenterItemKind.Episode
            "clip", "video" -> MediaCenterItemKind.Video
            "recording" -> MediaCenterItemKind.Recording
            "channel", "livetv" -> MediaCenterItemKind.LiveTv
            "track", "audio" -> MediaCenterItemKind.Audio
            else -> return null
        }
        val sources = raw.arrayAt("Media").flatMapIndexed { mediaIndex, mediaValue ->
            val media = mediaValue.objectValue()
            media.arrayAt("Part").mapNotNull { partValue ->
                val part = partValue.objectValue()
                val path = part.text("key")?.let { runCatching {
                    MediaCenterUrlPolicy.sanitizePathForStorage(URI(connection.baseUrl), it)
                }.getOrNull() } ?: return@mapNotNull null
                val sourceId = (part.text("id") ?: media.text("id") ?: "media-$mediaIndex")
                    .let { runCatching {
                        MediaCenterUrlPolicy.requireIdentifier(it, "Plex media source")
                    }.getOrNull() } ?: return@mapNotNull null
                MediaCenterSource(
                    id = sourceId,
                    playbackPath = path,
                    container = MediaCenterUrlPolicy.safeMetadata(part.text("container") ?: media.text("container"), token, 64),
                    videoCodec = MediaCenterUrlPolicy.safeMetadata(media.text("videoCodec"), token, 64),
                    audioCodec = MediaCenterUrlPolicy.safeMetadata(media.text("audioCodec"), token, 64),
                    width = media.integer("width")?.takeIf { it > 0 },
                    height = media.integer("height")?.takeIf { it > 0 },
                    bitrate = media.integer("bitrate")?.takeIf { it >= 0 },
                    supportsDirectPlay = true,
                    supportsDirectStream = true,
                    supportsTranscode = true
                )
            }
        }
        if (sources.isEmpty()) return null
        val artworkPath = raw.text("thumb")?.let { runCatching {
            MediaCenterUrlPolicy.sanitizePathForStorage(URI(connection.baseUrl), it)
        }.getOrNull() }
        return MediaCenterItem(
            id = id,
            provider = MediaCenterProvider.Plex,
            serverId = connection.serverId,
            libraryId = library.id,
            libraryTitle = library.title,
            kind = kind,
            title = title,
            sortTitle = MediaCenterUrlPolicy.safeMetadata(raw.text("titleSort"), token),
            seriesTitle = MediaCenterUrlPolicy.safeMetadata(raw.text("grandparentTitle"), token),
            seasonNumber = raw.integer("parentIndex")?.takeIf { it >= 0 },
            episodeNumber = raw.integer("index")?.takeIf { it >= 0 },
            year = raw.integer("year")?.takeIf { it in 1888..3000 },
            durationMs = raw.number("duration")?.takeIf { it >= 0 },
            resumePositionMs = raw.number("viewOffset")?.takeIf { it >= 0 },
            played = (raw.integer("viewCount") ?: 0) > 0,
            addedAt = epochSecondsInstant(raw.number("addedAt")),
            lastPlayedAt = epochSecondsInstant(raw.number("lastViewedAt")),
            artworkPath = artworkPath,
            mediaSources = sources
        )
    }

    private fun plexPlaybackPlan(
        connection: MediaCenterConnection,
        token: String,
        item: MediaCenterItem,
        requestedMediaSourceId: String?
    ): MediaCenterPlaybackPlan {
        val source = requestedMediaSourceId?.let { requested ->
            item.mediaSources.firstOrNull { it.id == requested }
        } ?: item.mediaSources.firstOrNull()
            ?: error("Plex did not provide a playable media source.")
        val path = source.playbackPath ?: error("Plex did not provide a direct-play path.")
        val baseUrl = URI(connection.baseUrl)
        require(discoverPlexIdentity(baseUrl).serverId == connection.serverId) {
            "The Plex server identity changed before playback."
        }
        return MediaCenterPlaybackPlan(
            itemId = item.id,
            mediaSourceId = source.id,
            url = MediaCenterUrlPolicy.resolveServerPath(baseUrl, path).toASCIIString(),
            requestHeaders = plexHeaders(token),
            resumePositionMs = item.resumePositionMs ?: 0
        )
    }

    private fun embyLibraries(
        connection: MediaCenterConnection,
        token: String
    ): List<MediaCenterLibrary> {
        val userId = MediaCenterUrlPolicy.requireIdentifier(
            connection.userId ?: error("The protected Emby user is missing."),
            "Emby user"
        )
        return embyGet(connection, token, "/Users/${pathComponent(userId)}/Views")
            .objectValue()
            .arrayAt("Items")
            .mapNotNull { element ->
                val raw = element.objectValue()
                val id = raw.text("Id")?.let { runCatching {
                    MediaCenterUrlPolicy.requireIdentifier(it, "Emby library")
                }.getOrNull() } ?: return@mapNotNull null
                val title = MediaCenterUrlPolicy.safeMetadata(raw.text("Name"), token) ?: return@mapNotNull null
                MediaCenterLibrary(
                    id = id,
                    title = title,
                    kind = when ((raw.text("CollectionType") ?: raw.text("Type"))?.lowercase(Locale.ROOT)) {
                        "movies" -> MediaCenterLibraryKind.Movies
                        "tvshows" -> MediaCenterLibraryKind.Shows
                        "recordings" -> MediaCenterLibraryKind.Recordings
                        "livetv" -> MediaCenterLibraryKind.LiveTv
                        "music" -> MediaCenterLibraryKind.Music
                        else -> MediaCenterLibraryKind.Other
                    },
                    itemCount = raw.integer("ChildCount")?.takeIf { it >= 0 }
                )
            }
    }

    private fun embyItems(
        connection: MediaCenterConnection,
        token: String,
        library: MediaCenterLibrary,
        start: Int,
        size: Int
    ): MediaCenterPage {
        val userId = MediaCenterUrlPolicy.requireIdentifier(connection.userId.orEmpty(), "Emby user")
        val endpoint = MediaCenterUrlPolicy.resolveServerPath(
            embyApiBase(URI(connection.baseUrl)),
            "/Users/${pathComponent(userId)}/Items"
        )
        val url = MediaCenterUrlPolicy.appendQuery(
            endpoint,
            mapOf(
                "ParentId" to library.id,
                "Recursive" to "true",
                "IncludeItemTypes" to "Movie,Episode,Video,MusicVideo,Recording,LiveTvChannel,Audio",
                "Fields" to "MediaSources,MediaStreams,SortName,DateCreated",
                "EnableImages" to "true",
                "EnableUserData" to "true",
                "StartIndex" to start.toString(),
                "Limit" to size.toString()
            )
        )
        val payload = embyGet(connection, token, url).objectValue()
        val items = payload.arrayAt("Items").mapNotNull { element ->
            parseEmbyItem(connection, token, library, element.objectValue())
        }
        return MediaCenterPage(
            items = items,
            start = start,
            returnedCount = payload.arrayAt("Items").size(),
            total = maxOf(items.size, payload.integer("TotalRecordCount") ?: items.size)
        )
    }

    private fun parseEmbyItem(
        connection: MediaCenterConnection,
        token: String,
        library: MediaCenterLibrary,
        raw: JsonObject
    ): MediaCenterItem? {
        val id = raw.text("Id")?.let { runCatching {
            MediaCenterUrlPolicy.requireIdentifier(it, "Emby item")
        }.getOrNull() } ?: return null
        val title = MediaCenterUrlPolicy.safeMetadata(raw.text("Name"), token) ?: return null
        val kind = when (raw.text("Type")?.lowercase(Locale.ROOT)) {
            "movie" -> MediaCenterItemKind.Movie
            "episode" -> MediaCenterItemKind.Episode
            "video", "musicvideo" -> MediaCenterItemKind.Video
            "recording" -> MediaCenterItemKind.Recording
            "livetvchannel" -> MediaCenterItemKind.LiveTv
            "audio" -> MediaCenterItemKind.Audio
            else -> return null
        }
        val userData = raw.objectAt("UserData")
        val imageTag = raw.objectAt("ImageTags").text("Primary") ?: raw.text("PrimaryImageTag")
        val artworkPath = imageTag?.let {
            "/Items/${pathComponent(id)}/Images/Primary?Tag=${pathComponent(it)}"
        }
        val sources = raw.arrayAt("MediaSources").mapIndexedNotNull { index, element ->
            val source = element.objectValue()
            val sourceId = (source.text("Id") ?: "source-$index").let { runCatching {
                MediaCenterUrlPolicy.requireIdentifier(it, "Emby media source")
            }.getOrNull() } ?: return@mapIndexedNotNull null
            val streams = source.arrayAt("MediaStreams").map { it.objectValue() }
            val video = streams.firstOrNull { it.text("Type")?.equals("video", true) == true }
            val audio = streams.firstOrNull { it.text("Type")?.equals("audio", true) == true }
            val playbackPath = source.text("DirectStreamUrl")?.let { runCatching {
                MediaCenterUrlPolicy.sanitizePathForStorage(embyApiBase(URI(connection.baseUrl)), it)
            }.getOrNull() }
            MediaCenterSource(
                id = sourceId,
                playbackPath = playbackPath,
                container = MediaCenterUrlPolicy.safeMetadata(source.text("Container"), token, 64),
                videoCodec = MediaCenterUrlPolicy.safeMetadata(video?.text("Codec"), token, 64),
                audioCodec = MediaCenterUrlPolicy.safeMetadata(audio?.text("Codec"), token, 64),
                width = video?.integer("Width")?.takeIf { it > 0 },
                height = video?.integer("Height")?.takeIf { it > 0 },
                bitrate = source.integer("Bitrate")?.takeIf { it >= 0 },
                supportsDirectPlay = source.boolean("SupportsDirectPlay"),
                supportsDirectStream = source.boolean("SupportsDirectStream"),
                supportsTranscode = source.boolean("SupportsTranscoding")
            )
        }
        return MediaCenterItem(
            id = id,
            provider = MediaCenterProvider.Emby,
            serverId = connection.serverId,
            libraryId = library.id,
            libraryTitle = library.title,
            kind = kind,
            title = title,
            sortTitle = MediaCenterUrlPolicy.safeMetadata(raw.text("SortName"), token),
            seriesTitle = MediaCenterUrlPolicy.safeMetadata(raw.text("SeriesName"), token),
            seasonNumber = raw.integer("ParentIndexNumber")?.takeIf { it >= 0 },
            episodeNumber = raw.integer("IndexNumber")?.takeIf { it >= 0 },
            year = raw.integer("ProductionYear")?.takeIf { it in 1888..3000 },
            durationMs = raw.number("RunTimeTicks")?.takeIf { it >= 0 }?.div(10_000),
            resumePositionMs = userData.number("PlaybackPositionTicks")?.takeIf { it >= 0 }?.div(10_000),
            played = userData.boolean("Played"),
            addedAt = normalizedInstant(raw.text("DateCreated")),
            lastPlayedAt = normalizedInstant(userData.text("LastPlayedDate")),
            artworkPath = artworkPath,
            mediaSources = sources
        )
    }

    private fun epochSecondsInstant(value: Long?): String? = value
        ?.takeIf { it >= 0 }
        ?.let { runCatching { Instant.ofEpochSecond(it).takeIf { parsed -> parsed < MAX_MEDIA_DATE }?.toString() }.getOrNull() }

    private fun normalizedInstant(value: String?): String? = value?.let {
        runCatching { Instant.parse(it).takeIf { parsed -> parsed < MAX_MEDIA_DATE }?.toString() }.getOrNull()
    }

    private fun embyPlaybackPlan(
        connection: MediaCenterConnection,
        token: String,
        item: MediaCenterItem,
        requestedMediaSourceId: String?
    ): MediaCenterPlaybackPlan {
        val userId = MediaCenterUrlPolicy.requireIdentifier(connection.userId.orEmpty(), "Emby user")
        val apiBase = embyApiBase(URI(connection.baseUrl))
        val endpoint = MediaCenterUrlPolicy.resolveServerPath(
            apiBase,
            "/Items/${pathComponent(item.id)}/PlaybackInfo"
        )
        val infoUrl = MediaCenterUrlPolicy.appendQuery(
            endpoint,
            mapOf(
                "UserId" to userId,
                "StartTimeTicks" to ((item.resumePositionMs ?: 0).coerceAtLeast(0) * 10_000).toString()
            )
        )
        val payload = embyGet(connection, token, infoUrl).objectValue()
        val candidates = payload.arrayAt("MediaSources").map { it.objectValue() }
        val source = requestedMediaSourceId?.let { id -> candidates.firstOrNull { it.text("Id") == id } }
            ?: candidates.firstOrNull()
            ?: error("Emby did not provide a playable media source.")
        val sourceId = MediaCenterUrlPolicy.requireIdentifier(
            source.text("Id") ?: item.mediaSources.firstOrNull()?.id ?: "default",
            "Emby media source"
        )
        val playSessionId = MediaCenterUrlPolicy.requireIdentifier(
            payload.text("PlaySessionId") ?: MediaCenterUrlPolicy.hash("${item.id}|${Instant.now()}").take(48),
            "Emby play session"
        )
        val direct = source.boolean("SupportsDirectPlay") || source.boolean("SupportsDirectStream")
        val targetUrl = when {
            direct && source.text("DirectStreamUrl") != null ->
                MediaCenterUrlPolicy.resolveServerPath(apiBase, source.text("DirectStreamUrl")!!)
            direct -> MediaCenterUrlPolicy.appendQuery(
                MediaCenterUrlPolicy.resolveServerPath(
                    apiBase,
                    "/Videos/${pathComponent(item.id)}/stream.${safeContainer(source.text("Container"))}"
                ),
                mapOf(
                    "MediaSourceId" to sourceId,
                    "PlaySessionId" to playSessionId,
                    "Static" to "true"
                )
            )
            source.boolean("SupportsTranscoding") && source.text("TranscodingUrl") != null ->
                MediaCenterUrlPolicy.resolveServerPath(apiBase, source.text("TranscodingUrl")!!)
            else -> error("Emby did not provide a supported direct-play or transcode path.")
        }
        val requiredHeaders = source.objectAt("RequiredHttpHeaders").entrySet().associate { entry ->
            entry.key to runCatching { entry.value.asString }.getOrDefault("")
        }
        return MediaCenterPlaybackPlan(
            itemId = item.id,
            mediaSourceId = sourceId,
            url = targetUrl.toASCIIString(),
            requestHeaders = MediaCenterHeaderPolicy.providerHeaders(requiredHeaders) + embyHeaders(token, userId),
            resumePositionMs = item.resumePositionMs ?: 0
        )
    }

    private fun discoverPlexIdentity(baseUrl: URI): ServerIdentity {
        val payload = MediaCenterApi.json(
            MediaHttpRequest(
                MediaHttpMethod.GET,
                MediaCenterUrlPolicy.resolveServerPath(baseUrl, "/identity"),
                plexClientHeaders()
            ),
            transport
        ).objectValue().objectAt("MediaContainer")
        return ServerIdentity(
            serverId = MediaCenterUrlPolicy.requireIdentifier(
                payload.text("machineIdentifier") ?: error("Plex did not identify this server."),
                "Plex server"
            ),
            name = MediaCenterUrlPolicy.safeMetadata(payload.text("friendlyName")) ?: "Plex"
        )
    }

    private fun discoverEmbyIdentity(apiBase: URI): ServerIdentity {
        val payload = MediaCenterApi.json(
            MediaHttpRequest(
                MediaHttpMethod.GET,
                MediaCenterUrlPolicy.resolveServerPath(apiBase, "/System/Info/Public"),
                mapOf("Accept" to "application/json")
            ),
            transport
        ).objectValue()
        return ServerIdentity(
            serverId = MediaCenterUrlPolicy.requireIdentifier(
                payload.text("Id") ?: error("Emby did not identify this server."),
                "Emby server"
            ),
            name = MediaCenterUrlPolicy.safeMetadata(payload.text("ServerName")) ?: "Emby"
        )
    }

    private fun plexGet(
        connection: MediaCenterConnection,
        token: String,
        path: String,
        additionalHeaders: Map<String, String> = emptyMap()
    ): JsonObject {
        val baseUrl = URI(connection.baseUrl)
        require(discoverPlexIdentity(baseUrl).serverId == connection.serverId) {
            "The Plex server identity changed before a protected request."
        }
        return MediaCenterApi.json(
            MediaHttpRequest(
                MediaHttpMethod.GET,
                MediaCenterUrlPolicy.resolveServerPath(baseUrl, path),
                plexHeaders(token) + additionalHeaders
            ),
            transport
        ).objectValue()
    }

    private fun embyGet(connection: MediaCenterConnection, token: String, path: String): JsonObject =
        embyGet(
            connection,
            token,
            MediaCenterUrlPolicy.resolveServerPath(embyApiBase(URI(connection.baseUrl)), path)
        )

    private fun embyGet(connection: MediaCenterConnection, token: String, url: URI): JsonObject {
        val apiBase = embyApiBase(URI(connection.baseUrl))
        require(discoverEmbyIdentity(apiBase).serverId == connection.serverId) {
            "The Emby server identity changed before a protected request."
        }
        val userId = MediaCenterUrlPolicy.requireIdentifier(connection.userId.orEmpty(), "Emby user")
        return MediaCenterApi.json(
            MediaHttpRequest(MediaHttpMethod.GET, url, embyHeaders(token, userId)),
            transport
        ).objectValue()
    }

    private fun saveCredential(
        connection: MediaCenterConnection,
        token: String,
        allowInsecureHttp: Boolean
    ) {
        val record = MediaCenterCredentialRecord(
            provider = connection.provider,
            serverId = connection.serverId,
            userId = connection.userId,
            baseUrl = MediaCenterUrlPolicy.normalizeBaseUrl(connection.baseUrl).toASCIIString(),
            allowInsecureHttp = allowInsecureHttp,
            value = MediaCenterUrlPolicy.credential(token)
        )
        credentialVault.save(connection.credentialId, gson.toJson(record))
    }

    private fun credential(connection: MediaCenterConnection): String {
        validateConnection(connection)
        val raw = credentialVault.read(connection.credentialId)
            ?: error("The protected media-center credential is missing. Connect the server again.")
        val record = runCatching { gson.fromJson(raw, MediaCenterCredentialRecord::class.java) }
            .getOrElse { error("The protected media-center credential is invalid.") }
        val expectedBase = MediaCenterUrlPolicy.normalizeBaseUrl(connection.baseUrl).toASCIIString()
        require(record.contractVersion == MEDIA_CENTER_CONTRACT_VERSION &&
            record.provider == connection.provider && record.serverId == connection.serverId &&
            record.userId == connection.userId && record.baseUrl == expectedBase) {
            "The protected media-center credential does not belong to this server."
        }
        MediaCenterUrlPolicy.requireAllowedTransport(URI(record.baseUrl), record.allowInsecureHttp)
        return MediaCenterUrlPolicy.credential(record.value)
    }

    private fun validateConnection(connection: MediaCenterConnection) {
        require(connection.contractVersion == MEDIA_CENTER_CONTRACT_VERSION) {
            "This saved media-center source uses an unsupported format."
        }
        MediaCenterUrlPolicy.requireIdentifier(connection.serverId, "server")
        MediaCenterUrlPolicy.requireIdentifier(connection.credentialId, "credential reference")
        connection.userId?.let { MediaCenterUrlPolicy.requireIdentifier(it, "user") }
        val normalized = MediaCenterUrlPolicy.normalizeBaseUrl(connection.baseUrl)
        require(normalized.toASCIIString() == connection.baseUrl) {
            "The saved media-center address is invalid."
        }
        require(connection.displayName.isNotBlank() && connection.displayName.length <= 256) {
            "The saved media-center name is invalid."
        }
    }

    private fun plexClientHeaders(): Map<String, String> = mapOf(
        "Accept" to "application/json",
        "X-Plex-Client-Identifier" to safeApplicationValue(device.deviceId, "streamvue-android"),
        "X-Plex-Product" to safeApplicationValue(device.client, "OrbitalVue"),
        "X-Plex-Version" to safeApplicationValue(device.version, "5.6.0"),
        "X-Plex-Device" to safeApplicationValue(device.device, "Android")
    )

    private fun plexHeaders(token: String): Map<String, String> =
        plexClientHeaders() + ("X-Plex-Token" to MediaCenterUrlPolicy.credential(token))

    private fun embyHeaders(token: String, userId: String): Map<String, String> = mapOf(
        "Accept" to "application/json",
        "X-Emby-Token" to MediaCenterUrlPolicy.credential(token),
        "X-Emby-Authorization" to embyAuthorization(userId)
    )

    private fun embyAuthorization(userId: String? = null): String {
        val values = linkedMapOf(
            "Client" to safeApplicationValue(device.client, "OrbitalVue"),
            "Device" to safeApplicationValue(device.device, "Android"),
            "DeviceId" to safeApplicationValue(device.deviceId, "streamvue-android"),
            "Version" to safeApplicationValue(device.version, "5.6.0")
        )
        userId?.let { values["UserId"] = MediaCenterUrlPolicy.requireIdentifier(it, "Emby user") }
        return "MediaBrowser " + values.entries.joinToString(", ") { (name, value) -> "$name=\"$value\"" }
    }

    private fun embyApiBase(baseUrl: URI): URI {
        if (baseUrl.rawPath.orEmpty().trimEnd('/').endsWith("/emby", ignoreCase = true)) return baseUrl
        return URI(
            baseUrl.scheme,
            null,
            baseUrl.host,
            baseUrl.port,
            "${baseUrl.rawPath.orEmpty().trimEnd('/')}/emby",
            null,
            null
        )
    }

    private fun credentialReference(
        provider: MediaCenterProvider,
        serverId: String,
        baseUrl: URI,
        userId: String?
    ): String = "mc-${provider.name.lowercase(Locale.ROOT)}-" +
        MediaCenterUrlPolicy.hash("${provider.name}|$serverId|${baseUrl.toASCIIString()}|${userId ?: "server"}")
            .take(48)

    private fun pathComponent(value: String): String = URLEncoder
        .encode(value, StandardCharsets.UTF_8.name())
        .replace("+", "%20")

    private fun safeContainer(value: String?): String = value
        ?.lowercase(Locale.ROOT)
        ?.takeIf { it.matches(Regex("[a-z0-9]{1,16}")) }
        ?: "mkv"

    private fun safeApplicationValue(value: String, fallback: String): String = value
        .filterNot(Char::isISOControl)
        .replace("\"", "")
        .trim()
        .take(256)
        .ifEmpty { fallback }

    private data class ServerIdentity(val serverId: String, val name: String)

    private data class MediaCenterCredentialRecord(
        @SerializedName("contractVersion") val contractVersion: String = MEDIA_CENTER_CONTRACT_VERSION,
        @SerializedName("provider") val provider: MediaCenterProvider,
        @SerializedName("serverId") val serverId: String,
        @SerializedName("userId") val userId: String?,
        @SerializedName("baseUrl") val baseUrl: String,
        @SerializedName("allowInsecureHttp") val allowInsecureHttp: Boolean,
        @SerializedName("value") val value: String
    )

    private companion object {
        val MAX_MEDIA_DATE: Instant = Instant.parse("3001-01-01T00:00:00Z")
        const val PAGE_SIZE = 200
        const val MAX_TOTAL_ITEMS = 20_000
        const val PLEX_DISCOVERY_LIFETIME_SECONDS = 10 * 60L
    }
}
