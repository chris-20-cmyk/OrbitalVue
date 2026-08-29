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
import com.streamvue.player.data.PlaylistRepository
import com.streamvue.player.premium.PremiumAccessPolicy
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import java.net.URI
import java.util.Locale

data class GroupSummary(val name: String, val count: Int)
data class ChannelSection(val name: String, val channels: List<Channel>)

data class AppUiState(
    val isLoading: Boolean = true,
    val loadingLabel: String = "Preparing your library…",
    val catalog: Catalog? = null,
    val groups: List<GroupSummary> = emptyList(),
    val selectedGroup: String? = null,
    val query: String = "",
    val visibleChannels: List<Channel> = emptyList(),
    val sections: List<ChannelSection> = emptyList(),
    val selectedChannel: Channel? = null,
    val playbackChannel: Channel? = null,
    val isResolvingPlayback: Boolean = false,
    val notice: String? = null,
    val error: String? = null
) {
    val isMediaCenterSource: Boolean
        get() = catalog?.source?.type?.isMediaCenter == true
}

class MainViewModel(application: Application) : AndroidViewModel(application) {
    private val repository = PlaylistRepository(application)
    private val mediaCenterRepository = MediaCenterRepository(application)
    private val premiumAccess = PremiumAccessPolicy.current()
    private val sourcePreferences = application.getSharedPreferences(
        "streamvue-active-source-v1",
        Context.MODE_PRIVATE
    )
    private val mutableState = MutableStateFlow(AppUiState())
    private var playbackResolutionJob: Job? = null
    val state: StateFlow<AppUiState> = mutableState.asStateFlow()

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
            val browse = buildBrowse(current.catalog, group, current.query)
            current.copy(
                selectedGroup = group,
                visibleChannels = browse.channels,
                sections = browse.sections
            )
        }
    }

    fun updateQuery(value: String) {
        mutableState.update { current ->
            val browse = buildBrowse(current.catalog, current.selectedGroup, value)
            current.copy(
                query = value,
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

    fun dismissNotice() {
        mutableState.update { it.copy(notice = null) }
    }

    fun dismissError() {
        mutableState.update { it.copy(error = null) }
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
        val browse = buildBrowse(catalog, selectedGroup, current.query)

        mutableState.value = current.copy(
            isLoading = false,
            loadingLabel = "",
            catalog = catalog,
            groups = groups,
            selectedGroup = selectedGroup,
            visibleChannels = browse.channels,
            sections = browse.sections,
            selectedChannel = selectedChannel,
            playbackChannel = selectedChannel?.takeUnless { it.isProtectedMediaLocator },
            isResolvingPlayback = false,
            notice = loaded.notice,
            error = null
        )
        selectedChannel?.takeIf { it.isProtectedMediaLocator }?.let(::selectChannel)
    }

    private fun showFailure(error: Throwable) {
        mutableState.update {
            it.copy(
                isLoading = false,
                loadingLabel = "",
                isResolvingPlayback = false,
                error = error.message ?: "StreamVue could not load that source."
            )
        }
    }

    private fun buildBrowse(catalog: Catalog?, group: String?, query: String): BrowseResult {
        if (catalog == null) return BrowseResult(emptyList(), emptyList())
        val search = query.trim().uppercase(Locale.ROOT)
        val channels = catalog.channels.filter { channel ->
            (group == null || channel.group == group) &&
                (search.isEmpty() || search in channel.searchText)
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
    }
}
