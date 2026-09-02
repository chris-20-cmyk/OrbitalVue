package com.streamvue.player

import android.app.Application
import android.content.Context
import android.net.Uri
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.streamvue.player.data.Catalog
import com.streamvue.player.data.Channel
import com.streamvue.player.data.LoadedCatalog
import com.streamvue.player.data.MediaCenterRepository
import com.streamvue.player.data.MediaCenterPlaybackReport
import com.streamvue.player.data.MediaCenterPlaybackReportKind
import com.streamvue.player.data.MediaLibraryBrowseMode
import com.streamvue.player.data.MediaLibraryBrowsePolicy
import com.streamvue.player.data.MediaLibraryBrowseSummary
import com.streamvue.player.data.PlaylistRepository
import com.streamvue.player.data.PlexPinChallenge
import com.streamvue.player.data.PlexServerDiscovery
import com.streamvue.player.premium.PremiumBillingState
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Job
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.net.URI
import java.time.Instant
import java.util.Locale

data class GroupSummary(val name: String, val count: Int)
data class ChannelSection(val name: String, val channels: List<Channel>)

enum class PlexSignInPhase { Idle, Preparing, Waiting, Ready, Connecting, Failed }

data class PlexSignInUiState(
    val phase: PlexSignInPhase = PlexSignInPhase.Idle,
    val challenge: PlexPinChallenge? = null,
    val discovery: PlexServerDiscovery? = null,
    val error: String? = null
)

data class AppUiState(
    val isLoading: Boolean = true,
    val loadingLabel: String = "Preparing your library…",
    val catalog: Catalog? = null,
    val groups: List<GroupSummary> = emptyList(),
    val selectedGroup: String? = null,
    val browseMode: MediaLibraryBrowseMode = MediaLibraryBrowseMode.All,
    val browseSummary: MediaLibraryBrowseSummary = MediaLibraryBrowseSummary(),
    val query: String = "",
    val visibleChannels: List<Channel> = emptyList(),
    val sections: List<ChannelSection> = emptyList(),
    val selectedChannel: Channel? = null,
    val playbackChannel: Channel? = null,
    val isResolvingPlayback: Boolean = false,
    val premiumBilling: PremiumBillingState = PremiumBillingState.initial(),
    val plexSignIn: PlexSignInUiState = PlexSignInUiState(),
    val notice: String? = null,
    val error: String? = null
) {
    val isMediaCenterSource: Boolean
        get() = catalog?.source?.type?.isMediaCenter == true
}

class MainViewModel(application: Application) : AndroidViewModel(application) {
    private val mutableState = MutableStateFlow(AppUiState())
    private val repository = PlaylistRepository(application)
    private val mediaCenterRepository = MediaCenterRepository(
        context = application,
        premiumAccessProvider = { mutableState.value.premiumBilling.access }
    )
    private val sourcePreferences = application.getSharedPreferences(
        "streamvue-active-source-v1",
        Context.MODE_PRIVATE
    )
    private var playbackResolutionJob: Job? = null
    private var playbackReportingJob: Job? = null
    private var mediaLibraryProgressRefreshJob: Job? = null
    private var plexSignInJob: Job? = null
    val state: StateFlow<AppUiState> = mutableState.asStateFlow()

    private val premiumAccess
        get() = mutableState.value.premiumBilling.access

    init {
        viewModelScope.launch {
            runCatching { loadPreferredSource() }
                .onSuccess { loaded ->
                    if (loaded == null) {
                        mutableState.update { it.copy(isLoading = false, loadingLabel = "") }
                    } else {
                        applyLoaded(loaded)
                    }
                }
                .onFailure(::showFailure)
        }
    }

    fun importDocument(uri: Uri) {
        launchLoad("Reading playlist…", ActiveSource.Playlist) { repository.importDocument(uri) }
    }

    fun importUrl(value: String) {
        launchLoad("Connecting securely…", ActiveSource.Playlist) { repository.importUrl(value) }
    }

    fun connectPlex(
        serverAddress: String,
        token: String,
        displayName: String?,
        allowInsecureHttp: Boolean
    ) {
        if (!requireMediaCenterAccess()) return
        launchLoad("Connecting to Plex…", ActiveSource.MediaCenter) {
            mediaCenterRepository.connectPlex(
                serverAddress = serverAddress,
                token = token,
                displayName = displayName,
                allowInsecureHttp = allowInsecureHttp
            )
        }
    }

    fun startPlexAccountSignIn() {
        if (!requireMediaCenterAccess()) return
        val previousSession = mutableState.value.plexSignIn.discovery?.sessionId
        plexSignInJob?.cancel()
        plexSignInJob = viewModelScope.launch {
            previousSession?.let { mediaCenterRepository.cancelPlexDiscovery(it) }
            mutableState.update {
                it.copy(plexSignIn = PlexSignInUiState(PlexSignInPhase.Preparing))
            }
            try {
                val challenge = mediaCenterRepository.createPlexSignInChallenge()
                mutableState.update {
                    it.copy(
                        plexSignIn = PlexSignInUiState(
                            phase = PlexSignInPhase.Waiting,
                            challenge = challenge
                        )
                    )
                }
                pollPlexSignIn(challenge)
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (_: Throwable) {
                showPlexSignInFailure()
            }
        }
    }

    fun cancelPlexAccountSignIn() {
        val sessionId = mutableState.value.plexSignIn.discovery?.sessionId
        plexSignInJob?.cancel()
        plexSignInJob = null
        mutableState.update { it.copy(plexSignIn = PlexSignInUiState()) }
        sessionId?.let { id ->
            viewModelScope.launch { mediaCenterRepository.cancelPlexDiscovery(id) }
        }
    }

    fun connectDiscoveredPlexServer(
        sessionId: String,
        serverId: String,
        connectionUrl: String,
        allowInsecureHttp: Boolean
    ) {
        if (!requireMediaCenterAccess()) return
        val discovery = mutableState.value.plexSignIn.discovery
        if (discovery?.sessionId != sessionId) {
            showPlexSignInFailure()
            return
        }
        plexSignInJob?.cancel()
        plexSignInJob = viewModelScope.launch {
            mutableState.update { current ->
                current.copy(
                    isLoading = true,
                    loadingLabel = "Connecting your Plex server…",
                    error = null,
                    plexSignIn = current.plexSignIn.copy(phase = PlexSignInPhase.Connecting, error = null)
                )
            }
            try {
                val loaded = mediaCenterRepository.connectDiscoveredPlexServer(
                    sessionId = sessionId,
                    serverId = serverId,
                    connectionUrl = connectionUrl,
                    allowInsecureHttp = allowInsecureHttp
                )
                activeSource = ActiveSource.MediaCenter
                applyLoaded(loaded)
            } catch (cancelled: CancellationException) {
                mutableState.update {
                    it.copy(isLoading = false, loadingLabel = "", plexSignIn = PlexSignInUiState())
                }
                throw cancelled
            } catch (error: Throwable) {
                showFailure(error)
                showPlexSignInFailure()
            }
        }
    }

    fun connectEmby(
        serverAddress: String,
        username: String,
        password: String,
        displayName: String?,
        allowInsecureHttp: Boolean
    ) {
        if (!requireMediaCenterAccess()) return
        launchLoad("Connecting to Emby…", ActiveSource.MediaCenter) {
            mediaCenterRepository.connectEmby(
                serverAddress = serverAddress,
                username = username,
                password = password,
                displayName = displayName,
                allowInsecureHttp = allowInsecureHttp
            )
        }
    }

    fun refresh() {
        val source = if (mutableState.value.catalog?.source?.type?.isMediaCenter == true) {
            ActiveSource.MediaCenter
        } else {
            ActiveSource.Playlist
        }
        launchLoad("Refreshing library…", source) {
            when (source) {
                ActiveSource.Playlist -> repository.refreshCurrent()
                ActiveSource.MediaCenter -> {
                    premiumAccess.requireMediaCenters()
                    mediaCenterRepository.refreshCurrent()
                }
            } ?: error("Connect a source before refreshing.")
        }
    }

    fun selectGroup(group: String?) {
        mutableState.update { current ->
            val browse = buildBrowse(current.catalog, group, current.query, current.browseMode)
            current.copy(
                selectedGroup = group,
                visibleChannels = browse.channels,
                sections = browse.sections
            )
        }
    }

    fun updateQuery(value: String) {
        mutableState.update { current ->
            val browse = buildBrowse(current.catalog, current.selectedGroup, value, current.browseMode)
            current.copy(
                query = value,
                visibleChannels = browse.channels,
                sections = browse.sections
            )
        }
    }

    fun selectBrowseMode(mode: MediaLibraryBrowseMode) {
        mutableState.update { current ->
            val effectiveMode = if (current.isMediaCenterSource) mode else MediaLibraryBrowseMode.All
            val browse = buildBrowse(current.catalog, current.selectedGroup, current.query, effectiveMode)
            current.copy(
                browseMode = effectiveMode,
                visibleChannels = browse.channels,
                sections = browse.sections
            )
        }
    }

    fun selectChannel(channel: Channel) {
        playbackResolutionJob?.cancel()
        if (!channel.isProtectedMediaLocator) {
            mutableState.update {
                it.copy(
                    selectedChannel = channel,
                    playbackChannel = channel,
                    isResolvingPlayback = false,
                    error = null
                )
            }
            return
        }
        if (!requireMediaCenterAccess()) return
        mutableState.update {
            it.copy(
                selectedChannel = channel,
                playbackChannel = null,
                isResolvingPlayback = true,
                error = null
            )
        }
        playbackResolutionJob = viewModelScope.launch {
            runCatching { mediaCenterRepository.resolvePlayback(channel) }
                .onSuccess { playable ->
                    mutableState.update { current ->
                        if (current.selectedChannel?.id != channel.id) current
                        else current.copy(playbackChannel = playable, isResolvingPlayback = false)
                    }
                }
                .onFailure { error ->
                    if (mutableState.value.selectedChannel?.id == channel.id) showFailure(error)
                }
        }
    }

    fun reportPlayback(sessionId: String, report: MediaCenterPlaybackReport) {
        val previous = playbackReportingJob
        playbackReportingJob = viewModelScope.launch {
            previous?.join()
            val reported = runCatching { mediaCenterRepository.reportPlayback(sessionId, report) }
            if (reported.isSuccess && report.kind == MediaCenterPlaybackReportKind.Stopped) {
                scheduleMediaLibraryProgressRefresh()
            }
        }
    }

    fun dismissNotice() {
        mutableState.update { it.copy(notice = null) }
    }

    fun dismissError() {
        mutableState.update { it.copy(error = null) }
    }

    fun updatePremiumBilling(billing: PremiumBillingState) {
        val previousAccess = mutableState.value.premiumBilling.access
        val revokedPlexSession = if (previousAccess.canUseMediaCenters &&
            !billing.access.canUseMediaCenters) {
            plexSignInJob?.cancel()
            mutableState.value.plexSignIn.discovery?.sessionId
        } else {
            null
        }
        mutableState.update { current ->
            if (current.premiumBilling == billing) return@update current
            if (current.premiumBilling.access.canUseMediaCenters &&
                !billing.access.canUseMediaCenters && current.isMediaCenterSource) {
                playbackResolutionJob?.cancel()
                current.copy(
                    catalog = null,
                    groups = emptyList(),
                    selectedGroup = null,
                    browseMode = MediaLibraryBrowseMode.All,
                    browseSummary = MediaLibraryBrowseSummary(),
                    visibleChannels = emptyList(),
                    sections = emptyList(),
                    selectedChannel = null,
                    playbackChannel = null,
                    isResolvingPlayback = false,
                    premiumBilling = billing,
                    plexSignIn = PlexSignInUiState(),
                    notice = null,
                    error = "Premium media-center access is no longer verified. Playlist sources remain available."
                )
            } else if (previousAccess.canUseMediaCenters && !billing.access.canUseMediaCenters) {
                current.copy(premiumBilling = billing, plexSignIn = PlexSignInUiState())
            } else {
                current.copy(premiumBilling = billing)
            }
        }
        revokedPlexSession?.let { sessionId ->
            viewModelScope.launch { mediaCenterRepository.cancelPlexDiscovery(sessionId) }
        }
        if (!previousAccess.canUseMediaCenters && billing.access.canUseMediaCenters &&
            activeSource == ActiveSource.MediaCenter && mutableState.value.catalog == null) {
            viewModelScope.launch {
                mutableState.update {
                    it.copy(
                        isLoading = true,
                        loadingLabel = "Opening your verified media center…",
                        error = null
                    )
                }
                runCatching { mediaCenterRepository.loadSaved() }
                    .onSuccess { loaded ->
                        if (loaded != null) applyLoaded(loaded)
                        else mutableState.update { it.copy(isLoading = false, loadingLabel = "") }
                    }
                    .onFailure(::showFailure)
            }
        }
    }

    private fun launchLoad(
        label: String,
        source: ActiveSource,
        operation: suspend () -> LoadedCatalog
    ) {
        viewModelScope.launch {
            mutableState.update { it.copy(isLoading = true, loadingLabel = label, error = null) }
            runCatching { operation() }
                .onSuccess { loaded ->
                    activeSource = source
                    applyLoaded(loaded)
                }
                .onFailure(::showFailure)
        }
    }

    private fun applyLoaded(loaded: LoadedCatalog) {
        val catalog = loaded.catalog
        val groupCounts = LinkedHashMap<String, Int>()
        catalog.channels.forEach { channel ->
            groupCounts[channel.group] = (groupCounts[channel.group] ?: 0) + 1
        }
        val groups = groupCounts.map { GroupSummary(it.key, it.value) }
        val current = mutableState.value
        val selectedGroup = current.selectedGroup?.takeIf(groupCounts::containsKey)
        val selectedChannel = current.selectedChannel?.id?.let { selectedId ->
            catalog.channels.firstOrNull { it.id == selectedId }
        }
        val browseMode = if (catalog.source.type.isMediaCenter) {
            current.browseMode
        } else {
            MediaLibraryBrowseMode.All
        }
        val browseSummary = MediaLibraryBrowsePolicy.summarize(catalog.channels)
        val browse = buildBrowse(catalog, selectedGroup, current.query, browseMode)

        mutableState.value = current.copy(
            isLoading = false,
            loadingLabel = "",
            catalog = catalog,
            groups = groups,
            selectedGroup = selectedGroup,
            browseMode = browseMode,
            browseSummary = browseSummary,
            visibleChannels = browse.channels,
            sections = browse.sections,
            selectedChannel = selectedChannel,
            playbackChannel = selectedChannel?.takeUnless { it.isProtectedMediaLocator },
            isResolvingPlayback = false,
            plexSignIn = PlexSignInUiState(),
            notice = loaded.notice,
            error = null
        )
        selectedChannel?.takeIf { it.isProtectedMediaLocator }?.let(::selectChannel)
    }

    private fun scheduleMediaLibraryProgressRefresh() {
        mediaLibraryProgressRefreshJob?.cancel()
        mediaLibraryProgressRefreshJob = viewModelScope.launch {
            val loaded = runCatching { mediaCenterRepository.refreshCurrent() }.getOrNull() ?: return@launch
            if (!mutableState.value.isMediaCenterSource) return@launch
            applyMediaLibraryProgressRefresh(loaded)
        }
    }

    private fun applyMediaLibraryProgressRefresh(loaded: LoadedCatalog) {
        val catalog = loaded.catalog
        val groupCounts = LinkedHashMap<String, Int>()
        catalog.channels.forEach { channel ->
            groupCounts[channel.group] = (groupCounts[channel.group] ?: 0) + 1
        }
        val current = mutableState.value
        val selectedGroup = current.selectedGroup?.takeIf(groupCounts::containsKey)
        val selectedChannel = current.selectedChannel?.id?.let { selectedId ->
            catalog.channels.firstOrNull { it.id == selectedId }
        }
        val browse = buildBrowse(catalog, selectedGroup, current.query, current.browseMode)

        mutableState.value = current.copy(
            catalog = catalog,
            groups = groupCounts.map { GroupSummary(it.key, it.value) },
            selectedGroup = selectedGroup,
            browseSummary = MediaLibraryBrowsePolicy.summarize(catalog.channels),
            visibleChannels = browse.channels,
            sections = browse.sections,
            selectedChannel = selectedChannel,
            playbackChannel = current.playbackChannel.takeIf { selectedChannel != null }
        )
    }

    private fun showFailure(error: Throwable) {
        mutableState.update {
            it.copy(
                isLoading = false,
                loadingLabel = "",
                isResolvingPlayback = false,
                error = error.message ?: "OrbitalVue could not load that source."
            )
        }
    }

    private suspend fun pollPlexSignIn(challenge: PlexPinChallenge) {
        var consecutiveFailures = 0
        while (currentCoroutineContext().isActive && Instant.now().isBefore(challenge.expiresAt)) {
            try {
                val discovery = mediaCenterRepository.completePlexSignIn(challenge)
                if (discovery != null) {
                    if (!premiumAccess.canUseMediaCenters) {
                        mediaCenterRepository.cancelPlexDiscovery(discovery.sessionId)
                        premiumAccess.requireMediaCenters()
                    }
                    mutableState.update {
                        it.copy(
                            plexSignIn = PlexSignInUiState(
                                phase = PlexSignInPhase.Ready,
                                discovery = discovery
                            )
                        )
                    }
                    return
                }
                consecutiveFailures = 0
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (_: Throwable) {
                consecutiveFailures += 1
                if (consecutiveFailures >= 3) {
                    showPlexSignInFailure()
                    return
                }
            }
            delay(PLEX_PIN_POLL_INTERVAL_MS)
        }
        if (currentCoroutineContext().isActive) {
            mutableState.update {
                it.copy(
                    plexSignIn = PlexSignInUiState(
                        phase = PlexSignInPhase.Failed,
                        error = "The Plex sign-in request expired. Start a new sign-in."
                    )
                )
            }
        }
    }

    private fun showPlexSignInFailure() {
        mutableState.update {
            it.copy(
                isLoading = false,
                loadingLabel = "",
                plexSignIn = PlexSignInUiState(
                    phase = PlexSignInPhase.Failed,
                    error = "Plex sign-in could not be completed. Check the connection and try again."
                )
            )
        }
    }

    private fun buildBrowse(
        catalog: Catalog?,
        group: String?,
        query: String,
        mode: MediaLibraryBrowseMode
    ): BrowseResult {
        if (catalog == null) return BrowseResult(emptyList(), emptyList())
        val search = query.trim().uppercase(Locale.ROOT)
        val matching = catalog.channels.filter { channel ->
            (group == null || channel.group == group) &&
                (search.isEmpty() || search in channel.searchText) &&
                MediaLibraryBrowsePolicy.matches(channel, mode)
        }
        val channels = MediaLibraryBrowsePolicy.order(matching, mode)
        if (mode.isEditorial) {
            return BrowseResult(
                channels = channels,
                sections = if (channels.isEmpty()) emptyList()
                else listOf(ChannelSection(mode.sectionTitle, channels))
            )
        }
        val sectionMap = LinkedHashMap<String, MutableList<Channel>>()
        channels.forEach { channel -> sectionMap.getOrPut(channel.group) { ArrayList() }.add(channel) }
        return BrowseResult(
            channels = channels,
            sections = sectionMap.map { ChannelSection(it.key, it.value) }
        )
    }

    private data class BrowseResult(
        val channels: List<Channel>,
        val sections: List<ChannelSection>
    )

    private suspend fun loadPreferredSource(): LoadedCatalog? {
        val preferred = activeSource
        loadSource(preferred)?.let { return it }
        val alternate = if (preferred == ActiveSource.Playlist) {
            ActiveSource.MediaCenter
        } else {
            ActiveSource.Playlist
        }
        return loadSource(alternate)?.also { activeSource = alternate }
    }

    private suspend fun loadSource(source: ActiveSource): LoadedCatalog? = when (source) {
        ActiveSource.Playlist -> repository.loadSaved()
        ActiveSource.MediaCenter -> if (premiumAccess.canUseMediaCenters) {
            mediaCenterRepository.loadSaved()
        } else {
            null
        }
    }

    private fun requireMediaCenterAccess(): Boolean {
        if (premiumAccess.canUseMediaCenters) return true
        showFailure(IllegalStateException(premiumAccess.explanation))
        return false
    }

    private var activeSource: ActiveSource
        get() = ActiveSource.fromStored(sourcePreferences.getString(KEY_ACTIVE_SOURCE, null))
        set(value) {
            sourcePreferences.edit().putString(KEY_ACTIVE_SOURCE, value.storedValue).apply()
        }

    private enum class ActiveSource(val storedValue: String) {
        Playlist("playlist"),
        MediaCenter("media-center");

        companion object {
            fun fromStored(value: String?): ActiveSource = entries.firstOrNull {
                it.storedValue == value
            } ?: Playlist
        }
    }

    private val Channel.isProtectedMediaLocator: Boolean
        get() = runCatching {
            URI(streamUri).scheme?.equals("streamvue-media", true) == true
        }.getOrDefault(false)

    private companion object {
        const val KEY_ACTIVE_SOURCE = "active_source"
        const val PLEX_PIN_POLL_INTERVAL_MS = 2_000L
    }
}
