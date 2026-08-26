package com.streamvue.player

import android.app.Application
import android.net.Uri
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.streamvue.player.data.Catalog
import com.streamvue.player.data.Channel
import com.streamvue.player.data.LoadedCatalog
import com.streamvue.player.data.PlaylistRepository
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
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
    val notice: String? = null,
    val error: String? = null
)

class MainViewModel(application: Application) : AndroidViewModel(application) {
    private val repository = PlaylistRepository(application)
    private val mutableState = MutableStateFlow(AppUiState())
    val state: StateFlow<AppUiState> = mutableState.asStateFlow()

    init {
        viewModelScope.launch {
            runCatching { repository.loadSaved() }
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
        launchLoad("Reading playlist…") { repository.importDocument(uri) }
    }

    fun importUrl(value: String) {
        launchLoad("Connecting securely…") { repository.importUrl(value) }
    }

    fun refresh() {
        launchLoad("Refreshing channels…") {
            repository.refreshCurrent() ?: error("Connect a playlist before refreshing.")
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
        mutableState.update { it.copy(selectedChannel = channel, error = null) }
    }

    fun dismissNotice() {
        mutableState.update { it.copy(notice = null) }
    }

    fun dismissError() {
        mutableState.update { it.copy(error = null) }
    }

    private fun launchLoad(label: String, operation: suspend () -> LoadedCatalog) {
        viewModelScope.launch {
            mutableState.update { it.copy(isLoading = true, loadingLabel = label, error = null) }
            runCatching { operation() }
                .onSuccess(::applyLoaded)
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
            notice = loaded.notice,
            error = null
        )
    }

    private fun showFailure(error: Throwable) {
        mutableState.update {
            it.copy(
                isLoading = false,
                loadingLabel = "",
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
}
