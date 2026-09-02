package com.orbitalvue.player.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.AspectRatio
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material.icons.rounded.FileOpen
import androidx.compose.material.icons.rounded.Fullscreen
import androidx.compose.material.icons.rounded.FullscreenExit
import androidx.compose.material.icons.rounded.Link
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material.icons.rounded.Tv
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.AssistChip
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import java.util.Locale
import androidx.media3.exoplayer.ExoPlayer
import com.orbitalvue.player.AppUiState
import com.orbitalvue.player.ChannelSection
import com.orbitalvue.player.GroupSummary
import com.orbitalvue.player.PlexSignInPhase
import com.orbitalvue.player.PlexSignInUiState
import com.orbitalvue.player.data.Channel
import com.orbitalvue.player.data.MediaLibraryBrowseMode
import com.orbitalvue.player.playback.PlaybackSignal
import com.orbitalvue.player.playback.StreamPlayerSurface
import com.orbitalvue.player.playback.VideoScaleMode
import com.orbitalvue.player.playback.rememberStreamPlayer
import com.orbitalvue.player.data.MediaCenterPlaybackReport
import com.orbitalvue.player.premium.PremiumBillingState
import com.orbitalvue.player.ui.theme.OrbitalVueBackground
import com.orbitalvue.player.ui.theme.OrbitalVueBorder
import com.orbitalvue.player.ui.theme.OrbitalVueError
import com.orbitalvue.player.ui.theme.OrbitalVueMuted
import com.orbitalvue.player.ui.theme.OrbitalVueSurface
import com.orbitalvue.player.ui.theme.OrbitalVueSurfaceRaised
import com.orbitalvue.player.ui.theme.OrbitalVueTeal
import com.orbitalvue.player.ui.theme.OrbitalVueTealDim
import com.orbitalvue.player.ui.theme.OrbitalVueText

@OptIn(ExperimentalFoundationApi::class, ExperimentalMaterial3Api::class)
@Composable
fun OrbitalVueApp(
    state: AppUiState,
    isTelevision: Boolean,
    onChooseFile: () -> Unit,
    onImportUrl: (String) -> Unit,
    onConnectPlex: (String, String, String?, Boolean) -> Unit,
    onStartPlexAccountSignIn: () -> Unit,
    onCancelPlexAccountSignIn: () -> Unit,
    onConnectDiscoveredPlexServer: (String, String, String, Boolean) -> Unit,
    onConnectEmby: (String, String, String, String?, Boolean) -> Unit,
    onPurchasePremium: () -> Unit,
    onRestorePremium: () -> Unit,
    onRefresh: () -> Unit,
    onSelectGroup: (String?) -> Unit,
    onSelectBrowseMode: (MediaLibraryBrowseMode) -> Unit,
    onQueryChanged: (String) -> Unit,
    onSelectChannel: (Channel) -> Unit,
    onPlaybackReport: (String, MediaCenterPlaybackReport) -> Unit,
    onDismissNotice: () -> Unit,
    onDismissError: () -> Unit,
    onFullscreenChanged: (Boolean) -> Unit
) {
    var showImport by remember { mutableStateOf(false) }
    var isFullscreen by remember { mutableStateOf(false) }
    var scaleMode by remember { mutableStateOf(VideoScaleMode.Auto) }
    var playbackSignal by remember(state.selectedChannel?.id) { mutableStateOf(PlaybackSignal()) }
    val player = rememberStreamPlayer(
        state.playbackChannel,
        onSignal = { playbackSignal = it },
        onPlaybackReport = onPlaybackReport
    )

    LaunchedEffect(isFullscreen) { onFullscreenChanged(isFullscreen) }
    DisposableEffect(Unit) {
        onDispose { onFullscreenChanged(false) }
    }
    BackHandler(enabled = isFullscreen) { isFullscreen = false }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(
                Brush.linearGradient(
                    listOf(OrbitalVueBackground, Color(0xFF07111C), OrbitalVueBackground)
                )
            )
    ) {
        if (isFullscreen && player != null && state.selectedChannel != null) {
            FullscreenPlayer(
                player = player,
                channel = state.selectedChannel,
                signal = playbackSignal,
                scaleMode = scaleMode,
                onScaleModeChanged = { scaleMode = it },
                onExit = { isFullscreen = false }
            )
        } else {
            BoxWithConstraints(
                modifier = Modifier
                    .fillMaxSize()
                    .safeDrawingPadding()
            ) {
                val useWideLayout = isTelevision || maxWidth >= 920.dp
                Column(modifier = Modifier.fillMaxSize()) {
                    AppHeader(
                        state = state,
                        compact = !useWideLayout,
                        onRefresh = onRefresh,
                        onAddSource = { showImport = true }
                    )
                    if (state.isLoading) {
                        LinearProgressIndicator(
                            modifier = Modifier.fillMaxWidth(),
                            color = OrbitalVueTeal,
                            trackColor = OrbitalVueTealDim
                        )
                    } else {
                        Spacer(modifier = Modifier.height(3.dp))
                    }

                    when {
                        state.catalog == null && state.isLoading -> LoadingLibrary(state.loadingLabel)
                        state.catalog == null -> Onboarding(
                            isTelevision = isTelevision,
                            onChooseFile = onChooseFile,
                            onAddSource = { showImport = true }
                        )
                        useWideLayout -> WideLibrary(
                            state = state,
                            player = player,
                            signal = playbackSignal,
                            scaleMode = scaleMode,
                            onScaleModeChanged = { scaleMode = it },
                            onSelectGroup = onSelectGroup,
                            onSelectBrowseMode = onSelectBrowseMode,
                            onQueryChanged = onQueryChanged,
                            onSelectChannel = onSelectChannel,
                            onFullscreen = { isFullscreen = true }
                        )
                        else -> CompactLibrary(
                            state = state,
                            player = player,
                            signal = playbackSignal,
                            scaleMode = scaleMode,
                            onScaleModeChanged = { scaleMode = it },
                            onSelectGroup = onSelectGroup,
                            onSelectBrowseMode = onSelectBrowseMode,
                            onQueryChanged = onQueryChanged,
                            onSelectChannel = onSelectChannel,
                            onFullscreen = { isFullscreen = true }
                        )
                    }
                }
            }

            state.notice?.let { notice ->
                NoticeBanner(
                    message = notice,
                    onDismiss = onDismissNotice,
                    modifier = Modifier
                        .align(Alignment.BottomCenter)
                        .safeDrawingPadding()
                        .padding(18.dp)
                )
            }
        }
    }

    if (showImport) {
        ImportSourceDialog(
            premiumBilling = state.premiumBilling,
            onDismiss = { showImport = false },
            onChooseFile = {
                showImport = false
                onChooseFile()
            },
            onImportUrl = { value ->
                showImport = false
                onImportUrl(value)
            },
            onConnectPlex = { address, token, name, allowHttp ->
                showImport = false
                onConnectPlex(address, token, name, allowHttp)
            },
            plexSignIn = state.plexSignIn,
            onStartPlexAccountSignIn = onStartPlexAccountSignIn,
            onCancelPlexAccountSignIn = onCancelPlexAccountSignIn,
            onConnectDiscoveredPlexServer = onConnectDiscoveredPlexServer,
            onConnectEmby = { address, username, password, name, allowHttp ->
                showImport = false
                onConnectEmby(address, username, password, name, allowHttp)
            },
            onPurchasePremium = onPurchasePremium,
            onRestorePremium = onRestorePremium
        )
    }

    state.error?.let { message ->
        AlertDialog(
            onDismissRequest = onDismissError,
            title = { Text("OrbitalVue needs attention") },
            text = { Text(message) },
            confirmButton = {
                Button(onClick = onDismissError) { Text("Got it") }
            }
        )
    }
}

@Composable
private fun AppHeader(
    state: AppUiState,
    compact: Boolean,
    onRefresh: () -> Unit,
    onAddSource: () -> Unit
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .height(if (compact) 68.dp else 78.dp)
            .padding(horizontal = if (compact) 14.dp else 24.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Surface(
            modifier = Modifier.size(if (compact) 42.dp else 48.dp),
            shape = RoundedCornerShape(13.dp),
            color = OrbitalVueTealDim,
            border = androidx.compose.foundation.BorderStroke(1.dp, OrbitalVueTeal.copy(alpha = 0.35f))
        ) {
            Box(contentAlignment = Alignment.Center) {
                Icon(
                    Icons.Rounded.PlayArrow,
                    contentDescription = null,
                    tint = OrbitalVueTeal,
                    modifier = Modifier.size(28.dp)
                )
            }
        }
        Spacer(modifier = Modifier.width(12.dp))
        Column {
            Text(
                text = "ORBITALVUE",
                color = OrbitalVueText,
                fontWeight = FontWeight.Black,
                letterSpacing = 2.sp,
                fontSize = if (compact) 16.sp else 19.sp
            )
            Text(
                text = if (state.catalog == null) "YOUR SIGNAL. BEAUTIFULLY ORGANIZED."
                else if (state.isMediaCenterSource) {
                    "${state.catalog.channels.size.toStringWithCommas()} ITEMS  •  ${state.groups.size.toStringWithCommas()} LIBRARIES"
                } else {
                    "${state.catalog.channels.size.toStringWithCommas()} CHANNELS  •  ${state.groups.size.toStringWithCommas()} GROUPS"
                },
                color = OrbitalVueMuted,
                fontSize = if (compact) 8.sp else 10.sp,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
        }
        Spacer(modifier = Modifier.weight(1f))

        if (!compact && state.catalog != null) {
            SourceStatus(state)
            Spacer(modifier = Modifier.width(10.dp))
        }
        IconButton(onClick = onRefresh, enabled = state.catalog != null && !state.isLoading) {
            Icon(Icons.Rounded.Refresh, contentDescription = "Refresh source")
        }
        if (compact) {
            IconButton(onClick = onAddSource) {
                Icon(Icons.Rounded.Add, contentDescription = "Add source")
            }
        } else {
            Button(onClick = onAddSource) {
                Icon(Icons.Rounded.Add, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(modifier = Modifier.width(7.dp))
                Text("Add source")
            }
        }
    }
}

@Composable
private fun SourceStatus(state: AppUiState) {
    val source = state.catalog?.source ?: return
    Surface(
        shape = RoundedCornerShape(12.dp),
        color = OrbitalVueSurfaceRaised,
        border = androidx.compose.foundation.BorderStroke(1.dp, OrbitalVueBorder)
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 13.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Box(
                modifier = Modifier
                    .size(7.dp)
                    .background(
                        if (source.usedCachedFallback) Color(0xFFFFB454) else OrbitalVueTeal,
                        CircleShape
                    )
            )
            Spacer(modifier = Modifier.width(8.dp))
            Column {
                Text(
                    text = if (source.usedCachedFallback) "OFFLINE COPY" else "SOURCE READY",
                    color = if (source.usedCachedFallback) Color(0xFFFFC47A) else OrbitalVueTeal,
                    fontSize = 8.sp,
                    fontWeight = FontWeight.Bold
                )
                Text(
                    text = source.displayLocation,
                    color = OrbitalVueMuted,
                    fontSize = 9.sp,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.widthIn(max = 190.dp)
                )
            }
        }
    }
}

@Composable
private fun WideLibrary(
    state: AppUiState,
    player: ExoPlayer?,
    signal: PlaybackSignal,
    scaleMode: VideoScaleMode,
    onScaleModeChanged: (VideoScaleMode) -> Unit,
    onSelectGroup: (String?) -> Unit,
    onSelectBrowseMode: (MediaLibraryBrowseMode) -> Unit,
    onQueryChanged: (String) -> Unit,
    onSelectChannel: (Channel) -> Unit,
    onFullscreen: () -> Unit
) {
    Row(
        modifier = Modifier
            .fillMaxSize()
            .padding(start = 20.dp, end = 20.dp, bottom = 20.dp),
        horizontalArrangement = Arrangement.spacedBy(14.dp)
    ) {
        GroupsPanel(
            groups = state.groups,
            totalChannels = state.catalog?.channels?.size ?: 0,
            isMediaCenter = state.isMediaCenterSource,
            selectedGroup = state.selectedGroup,
            onSelectGroup = onSelectGroup,
            modifier = Modifier.width(220.dp).fillMaxHeight()
        )
        ChannelBrowser(
            state = state,
            onSelectBrowseMode = onSelectBrowseMode,
            onQueryChanged = onQueryChanged,
            onSelectChannel = onSelectChannel,
            modifier = Modifier.width(360.dp).fillMaxHeight()
        )
        PlayerPane(
            channel = state.selectedChannel,
            player = player,
            signal = signal,
            scaleMode = scaleMode,
            onScaleModeChanged = onScaleModeChanged,
            onFullscreen = onFullscreen,
            modifier = Modifier.weight(1f).fillMaxHeight()
        )
    }
}

@Composable
private fun CompactLibrary(
    state: AppUiState,
    player: ExoPlayer?,
    signal: PlaybackSignal,
    scaleMode: VideoScaleMode,
    onScaleModeChanged: (VideoScaleMode) -> Unit,
    onSelectGroup: (String?) -> Unit,
    onSelectBrowseMode: (MediaLibraryBrowseMode) -> Unit,
    onQueryChanged: (String) -> Unit,
    onSelectChannel: (Channel) -> Unit,
    onFullscreen: () -> Unit
) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(horizontal = 12.dp, vertical = 8.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        PlayerPane(
            channel = state.selectedChannel,
            player = player,
            signal = signal,
            scaleMode = scaleMode,
            onScaleModeChanged = onScaleModeChanged,
            onFullscreen = onFullscreen,
            modifier = Modifier.fillMaxWidth().height(250.dp)
        )
        GroupChips(
            groups = state.groups,
            selectedGroup = state.selectedGroup,
            onSelectGroup = onSelectGroup
        )
        ChannelBrowser(
            state = state,
            onSelectBrowseMode = onSelectBrowseMode,
            onQueryChanged = onQueryChanged,
            onSelectChannel = onSelectChannel,
            modifier = Modifier.fillMaxWidth().weight(1f)
        )
    }
}

@Composable
private fun GroupsPanel(
    groups: List<GroupSummary>,
    totalChannels: Int,
    isMediaCenter: Boolean,
    selectedGroup: String?,
    onSelectGroup: (String?) -> Unit,
    modifier: Modifier = Modifier
) {
    Surface(
        modifier = modifier,
        color = OrbitalVueSurface.copy(alpha = 0.94f),
        shape = RoundedCornerShape(18.dp),
        border = androidx.compose.foundation.BorderStroke(1.dp, OrbitalVueBorder)
    ) {
        Column(modifier = Modifier.padding(12.dp)) {
            Text(
                if (isMediaCenter) "MEDIA LIBRARIES" else "CHANNEL GROUPS",
                color = OrbitalVueMuted,
                fontSize = 9.sp,
                fontWeight = FontWeight.Bold,
                letterSpacing = 1.5.sp,
                modifier = Modifier
                    .padding(8.dp)
                    .semantics { heading() }
            )
            LazyColumn(
                verticalArrangement = Arrangement.spacedBy(5.dp),
                modifier = Modifier.fillMaxSize()
            ) {
                item(key = "all") {
                    GroupRow(
                        name = if (isMediaCenter) "All media" else "All channels",
                        count = totalChannels,
                        selected = selectedGroup == null,
                        onClick = { onSelectGroup(null) }
                    )
                }
                items(groups, key = GroupSummary::name) { group ->
                    GroupRow(
                        name = group.name,
                        count = group.count,
                        selected = selectedGroup == group.name,
                        onClick = { onSelectGroup(group.name) }
                    )
                }
            }
        }
    }
}

@Composable
private fun GroupRow(
    name: String,
    count: Int,
    selected: Boolean,
    onClick: () -> Unit
) {
    var focused by remember { mutableStateOf(false) }
    Surface(
        onClick = onClick,
        modifier = Modifier
            .fillMaxWidth()
            .onFocusChanged { focused = it.isFocused }
            .semantics {
                stateDescription = if (selected) "Selected" else "Not selected"
            },
        color = if (selected) OrbitalVueTealDim else Color.Transparent,
        shape = RoundedCornerShape(11.dp),
        border = when {
            focused -> androidx.compose.foundation.BorderStroke(2.dp, OrbitalVueTeal)
            selected -> androidx.compose.foundation.BorderStroke(1.dp, OrbitalVueTeal.copy(alpha = 0.45f))
            else -> null
        }
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 12.dp, vertical = 11.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Text(
                name,
                modifier = Modifier.weight(1f),
                color = if (selected) OrbitalVueText else OrbitalVueMuted,
                fontSize = 11.sp,
                fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Normal,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis
            )
            Text(count.toStringWithCommas(), color = OrbitalVueMuted, fontSize = 9.sp)
        }
    }
}

@Composable
private fun GroupChips(
    groups: List<GroupSummary>,
    selectedGroup: String?,
    onSelectGroup: (String?) -> Unit
) {
    LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        item(key = "all") {
            FilterChip(
                selected = selectedGroup == null,
                onClick = { onSelectGroup(null) },
                label = { Text("All") }
            )
        }
        items(groups, key = GroupSummary::name) { group ->
            FilterChip(
                selected = selectedGroup == group.name,
                onClick = { onSelectGroup(group.name) },
                label = { Text(group.name, maxLines = 1) }
            )
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun ChannelBrowser(
    state: AppUiState,
    onSelectBrowseMode: (MediaLibraryBrowseMode) -> Unit,
    onQueryChanged: (String) -> Unit,
    onSelectChannel: (Channel) -> Unit,
    modifier: Modifier = Modifier
) {
    Surface(
        modifier = modifier,
        color = OrbitalVueSurface.copy(alpha = 0.94f),
        shape = RoundedCornerShape(18.dp),
        border = androidx.compose.foundation.BorderStroke(1.dp, OrbitalVueBorder)
    ) {
        Column(modifier = Modifier.fillMaxSize()) {
            Column(modifier = Modifier.padding(14.dp)) {
                if (state.isMediaCenterSource) {
                    MediaBrowseChips(
                        state = state,
                        onSelectBrowseMode = onSelectBrowseMode
                    )
                    Spacer(modifier = Modifier.height(10.dp))
                }
                OutlinedTextField(
                    value = state.query,
                    onValueChange = onQueryChanged,
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                    shape = RoundedCornerShape(12.dp),
                    placeholder = {
                        Text(
                            if (state.isMediaCenterSource) "Search media or libraries"
                            else "Search channels or groups"
                        )
                    },
                    leadingIcon = { Icon(Icons.Rounded.Search, contentDescription = null) }
                )
                Spacer(modifier = Modifier.height(8.dp))
                Text(
                    "${state.visibleChannels.size.toStringWithCommas()} RESULTS  •  ${state.sections.size.toStringWithCommas()} SECTIONS",
                    color = OrbitalVueMuted,
                    fontSize = 8.sp,
                    fontWeight = FontWeight.Bold,
                    letterSpacing = 1.sp
                )
            }
            HorizontalDivider(color = OrbitalVueBorder)
            if (state.visibleChannels.isEmpty()) {
                Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text(
                        if (state.isMediaCenterSource) "No media matches this view"
                        else "No channels match this view",
                        color = OrbitalVueMuted
                    )
                }
            } else {
                ChannelSections(
                    sections = state.sections,
                    selectedChannel = state.selectedChannel,
                    onSelectChannel = onSelectChannel,
                    modifier = Modifier.fillMaxSize()
                )
            }
        }
    }
}

@Composable
private fun MediaBrowseChips(
    state: AppUiState,
    onSelectBrowseMode: (MediaLibraryBrowseMode) -> Unit
) {
    val order = listOf(
        MediaLibraryBrowseMode.All,
        MediaLibraryBrowseMode.ContinueWatching,
        MediaLibraryBrowseMode.RecentlyAdded,
        MediaLibraryBrowseMode.Live,
        MediaLibraryBrowseMode.Movies,
        MediaLibraryBrowseMode.Series
    )
    LazyRow(horizontalArrangement = Arrangement.spacedBy(7.dp)) {
        items(order, key = MediaLibraryBrowseMode::name) { mode ->
            val count = when (mode) {
                MediaLibraryBrowseMode.All -> state.catalog?.channels?.size ?: 0
                MediaLibraryBrowseMode.ContinueWatching -> state.browseSummary.continueWatchingCount
                MediaLibraryBrowseMode.RecentlyAdded -> state.browseSummary.recentlyAddedCount
                MediaLibraryBrowseMode.Live -> state.catalog?.channels?.count { it.kind.label == "LIVE" } ?: 0
                MediaLibraryBrowseMode.Movies -> state.browseSummary.movieCount
                MediaLibraryBrowseMode.Series -> state.browseSummary.seriesCount
            }
            FilterChip(
                selected = state.browseMode == mode,
                onClick = { onSelectBrowseMode(mode) },
                label = { Text("${mode.label} $count", maxLines = 1) }
            )
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun ChannelSections(
    sections: List<ChannelSection>,
    selectedChannel: Channel?,
    onSelectChannel: (Channel) -> Unit,
    modifier: Modifier = Modifier
) {
    LazyColumn(modifier = modifier) {
        sections.forEach { section ->
            stickyHeader(key = "header:${section.name}") {
                Surface(color = Color(0xFF0D1725)) {
                    Row(
                        modifier = Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 9.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Box(modifier = Modifier.size(4.dp, 18.dp).background(OrbitalVueTeal, CircleShape))
                        Spacer(modifier = Modifier.width(8.dp))
                        Text(
                            section.name.uppercase(),
                            modifier = Modifier.weight(1f),
                            color = OrbitalVueText,
                            fontSize = 9.sp,
                            fontWeight = FontWeight.Bold,
                            letterSpacing = 1.sp,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis
                        )
                        Text(section.channels.size.toStringWithCommas(), color = OrbitalVueMuted, fontSize = 8.sp)
                    }
                }
            }
            items(
                items = section.channels,
                key = { channel -> "${channel.id}:${channel.number}" }
            ) { channel ->
                ChannelRow(
                    channel = channel,
                    selected = selectedChannel?.id == channel.id && selectedChannel.number == channel.number,
                    onClick = { onSelectChannel(channel) }
                )
            }
        }
    }
}

@Composable
private fun ChannelRow(channel: Channel, selected: Boolean, onClick: () -> Unit) {
    var focused by remember { mutableStateOf(false) }
    Surface(
        onClick = onClick,
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 8.dp, vertical = 3.dp)
            .onFocusChanged { focused = it.isFocused },
        color = if (selected) OrbitalVueTealDim else Color.Transparent,
        shape = RoundedCornerShape(12.dp),
        border = when {
            focused -> androidx.compose.foundation.BorderStroke(2.dp, OrbitalVueTeal)
            selected -> androidx.compose.foundation.BorderStroke(1.dp, OrbitalVueTeal.copy(alpha = 0.5f))
            else -> null
        }
    ) {
        Row(
            modifier = Modifier.padding(10.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Surface(
                modifier = Modifier.size(42.dp),
                shape = RoundedCornerShape(11.dp),
                color = OrbitalVueSurfaceRaised
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Text(channel.initials, color = OrbitalVueTeal, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                }
            }
            Spacer(modifier = Modifier.width(11.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    channel.name,
                    color = OrbitalVueText,
                    fontSize = 12.sp,
                    fontWeight = FontWeight.SemiBold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
                Spacer(modifier = Modifier.height(3.dp))
                Text(
                    channel.mediaMetadataLine ?: channel.group,
                    color = OrbitalVueMuted,
                    fontSize = 9.sp,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
                channel.watchProgress?.let { progress ->
                    Spacer(modifier = Modifier.height(5.dp))
                    LinearProgressIndicator(
                        progress = { progress },
                        modifier = Modifier.fillMaxWidth().height(3.dp),
                        color = OrbitalVueTeal,
                        trackColor = OrbitalVueBorder
                    )
                    channel.watchProgressLabel?.let { label ->
                        Spacer(modifier = Modifier.height(3.dp))
                        Text(label, color = OrbitalVueTeal, fontSize = 8.sp, maxLines = 1)
                    }
                }
            }
            Text(
                if (channel.canResume) "RESUME" else channel.kind.label,
                color = OrbitalVueTeal,
                fontSize = 7.sp,
                fontWeight = FontWeight.Bold
            )
        }
    }
}

@Composable
private fun PlayerPane(
    channel: Channel?,
    player: ExoPlayer?,
    signal: PlaybackSignal,
    scaleMode: VideoScaleMode,
    onScaleModeChanged: (VideoScaleMode) -> Unit,
    onFullscreen: () -> Unit,
    modifier: Modifier = Modifier
) {
    Surface(
        modifier = modifier,
        shape = RoundedCornerShape(18.dp),
        color = OrbitalVueSurface.copy(alpha = 0.94f),
        border = androidx.compose.foundation.BorderStroke(1.dp, OrbitalVueBorder)
    ) {
        Column(modifier = Modifier.fillMaxSize()) {
            Row(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 11.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        channel?.name ?: "Ready when you are",
                        color = OrbitalVueText,
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Bold,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                    Text(
                        channel?.group ?: "Select a channel to tune the native player",
                        color = OrbitalVueMuted,
                        fontSize = 9.sp,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                }
                if (channel != null) {
                    PlaybackStatus(signal)
                }
            }
            Box(modifier = Modifier.fillMaxWidth().weight(1f).background(Color.Black)) {
                if (player != null && channel != null) {
                    StreamPlayerSurface(player, scaleMode, Modifier.fillMaxSize())
                    signal.error?.let { error ->
                        Surface(
                            color = Color(0xD9151A25),
                            shape = RoundedCornerShape(12.dp),
                            modifier = Modifier.align(Alignment.Center).padding(24.dp)
                        ) {
                            Column(
                                modifier = Modifier.padding(18.dp),
                                horizontalAlignment = Alignment.CenterHorizontally
                            ) {
                                Text("Playback could not start", color = OrbitalVueText, fontWeight = FontWeight.Bold)
                                Spacer(modifier = Modifier.height(5.dp))
                                Text(error, color = OrbitalVueError, fontSize = 10.sp)
                            }
                        }
                    }
                } else {
                    EmptyPlayerStage()
                }
            }
            Row(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 8.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    "MEDIA3 NATIVE  •  HARDWARE AUTO  •  FRAME MATCH",
                    modifier = Modifier.weight(1f),
                    color = OrbitalVueMuted,
                    fontSize = 7.sp,
                    fontWeight = FontWeight.Bold,
                    letterSpacing = 0.7.sp,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
                AspectModeMenu(scaleMode, onScaleModeChanged, enabled = player != null)
                IconButton(onClick = onFullscreen, enabled = player != null) {
                    Icon(Icons.Rounded.Fullscreen, contentDescription = "Enter full screen")
                }
            }
        }
    }
}

@Composable
private fun EmptyPlayerStage() {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(
                Brush.radialGradient(
                    listOf(Color(0xFF102737), Color.Black),
                    radius = 700f
                )
            ),
        contentAlignment = Alignment.Center
    ) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Surface(modifier = Modifier.size(68.dp), shape = CircleShape, color = OrbitalVueTealDim) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(Icons.Rounded.PlayArrow, null, tint = OrbitalVueTeal, modifier = Modifier.size(42.dp))
                }
            }
            Spacer(modifier = Modifier.height(13.dp))
            Text("A better signal path", color = OrbitalVueText, fontWeight = FontWeight.Bold)
            Text("Native playback with no browser transcoding", color = OrbitalVueMuted, fontSize = 10.sp)
        }
    }
}

@Composable
private fun PlaybackStatus(signal: PlaybackSignal) {
    val label = when {
        signal.error != null -> "SIGNAL ERROR"
        signal.isBuffering -> "BUFFERING"
        signal.isPlaying -> "PLAYING"
        else -> "READY"
    }
    val color = when {
        signal.error != null -> OrbitalVueError
        signal.isBuffering -> Color(0xFFFFC36A)
        else -> OrbitalVueTeal
    }
    Surface(
        modifier = Modifier.semantics { liveRegion = LiveRegionMode.Polite },
        shape = RoundedCornerShape(30.dp),
        color = color.copy(alpha = 0.13f)
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 10.dp, vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            if (signal.isBuffering && signal.error == null) {
                CircularProgressIndicator(
                    modifier = Modifier.size(11.dp),
                    strokeWidth = 1.5.dp,
                    color = color
                )
            } else {
                Box(modifier = Modifier.size(6.dp).background(color, CircleShape))
            }
            Spacer(modifier = Modifier.width(6.dp))
            Text(label, color = color, fontSize = 7.sp, fontWeight = FontWeight.Bold)
        }
    }
}

@Composable
private fun AspectModeMenu(
    mode: VideoScaleMode,
    onModeChanged: (VideoScaleMode) -> Unit,
    enabled: Boolean
) {
    var expanded by remember { mutableStateOf(false) }
    Box {
        IconButton(
            onClick = { expanded = true },
            enabled = enabled,
            modifier = Modifier.semantics { stateDescription = "Current ratio ${mode.label}" }
        ) {
            Icon(Icons.Rounded.AspectRatio, contentDescription = "Change screen ratio")
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            VideoScaleMode.entries.forEach { option ->
                DropdownMenuItem(
                    text = {
                        Text(
                            option.label,
                            color = if (option == mode) OrbitalVueTeal else OrbitalVueText
                        )
                    },
                    onClick = {
                        expanded = false
                        onModeChanged(option)
                    }
                )
            }
        }
    }
}

@Composable
private fun FullscreenPlayer(
    player: ExoPlayer,
    channel: Channel,
    signal: PlaybackSignal,
    scaleMode: VideoScaleMode,
    onScaleModeChanged: (VideoScaleMode) -> Unit,
    onExit: () -> Unit
) {
    Box(modifier = Modifier.fillMaxSize().background(Color.Black)) {
        StreamPlayerSurface(player, scaleMode, Modifier.fillMaxSize())
        Surface(
            color = Color(0xB8070C14),
            shape = RoundedCornerShape(bottomStart = 18.dp, bottomEnd = 18.dp),
            modifier = Modifier.align(Alignment.TopCenter).fillMaxWidth()
        ) {
            Row(
                modifier = Modifier.safeDrawingPadding().padding(horizontal = 22.dp, vertical = 12.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(channel.name, color = OrbitalVueText, fontWeight = FontWeight.Bold, fontSize = 17.sp)
                    Text(channel.group, color = OrbitalVueMuted, fontSize = 10.sp)
                }
                PlaybackStatus(signal)
                Spacer(modifier = Modifier.width(6.dp))
                AspectModeMenu(scaleMode, onScaleModeChanged, enabled = true)
                IconButton(onClick = onExit) {
                    Icon(Icons.Rounded.FullscreenExit, contentDescription = "Exit full screen")
                }
            }
        }
    }
}

@Composable
private fun LoadingLibrary(label: String) {
    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            CircularProgressIndicator(color = OrbitalVueTeal)
            Spacer(modifier = Modifier.height(16.dp))
            Text(label.ifBlank { "Preparing your library…" }, color = OrbitalVueMuted)
        }
    }
}

@Composable
private fun Onboarding(
    isTelevision: Boolean,
    onChooseFile: () -> Unit,
    onAddSource: () -> Unit
) {
    Box(modifier = Modifier.fillMaxSize().padding(20.dp), contentAlignment = Alignment.Center) {
        Surface(
            modifier = Modifier.widthIn(max = 760.dp).fillMaxWidth(),
            color = OrbitalVueSurface.copy(alpha = 0.96f),
            shape = RoundedCornerShape(28.dp),
            border = androidx.compose.foundation.BorderStroke(1.dp, OrbitalVueBorder)
        ) {
            Column(
                modifier = Modifier.padding(if (isTelevision) 48.dp else 28.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Surface(modifier = Modifier.size(82.dp), shape = RoundedCornerShape(24.dp), color = OrbitalVueTealDim) {
                    Box(contentAlignment = Alignment.Center) {
                        Icon(Icons.Rounded.Tv, contentDescription = null, tint = OrbitalVueTeal, modifier = Modifier.size(45.dp))
                    }
                }
                Spacer(modifier = Modifier.height(22.dp))
                Text(
                    "Bring your content into focus",
                    color = OrbitalVueText,
                    fontWeight = FontWeight.Black,
                    fontSize = if (isTelevision) 30.sp else 24.sp
                )
                Spacer(modifier = Modifier.height(9.dp))
                Text(
                    "Connect an M3U playlist or your personal Plex or Emby server. OrbitalVue protects credentials on this device and refreshes the active library when the app opens.",
                    color = OrbitalVueMuted,
                    fontSize = if (isTelevision) 15.sp else 12.sp,
                    lineHeight = if (isTelevision) 23.sp else 19.sp
                )
                Spacer(modifier = Modifier.height(25.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    OutlinedButton(onClick = onChooseFile) {
                        Icon(Icons.Rounded.FileOpen, contentDescription = null)
                        Spacer(modifier = Modifier.width(8.dp))
                        Text("Choose M3U file")
                    }
                    Button(onClick = onAddSource) {
                        Icon(Icons.Rounded.Link, contentDescription = null)
                        Spacer(modifier = Modifier.width(8.dp))
                        Text("Add a source")
                    }
                }
                Spacer(modifier = Modifier.height(17.dp))
                Text(
                    "OrbitalVue is a player. It never bundles or sells content.",
                    color = OrbitalVueMuted,
                    fontSize = 9.sp
                )
            }
        }
    }
}

@Composable
private fun ImportSourceDialog(
    premiumBilling: PremiumBillingState,
    onDismiss: () -> Unit,
    onChooseFile: () -> Unit,
    onImportUrl: (String) -> Unit,
    onConnectPlex: (String, String, String?, Boolean) -> Unit,
    plexSignIn: PlexSignInUiState,
    onStartPlexAccountSignIn: () -> Unit,
    onCancelPlexAccountSignIn: () -> Unit,
    onConnectDiscoveredPlexServer: (String, String, String, Boolean) -> Unit,
    onConnectEmby: (String, String, String, String?, Boolean) -> Unit,
    onPurchasePremium: () -> Unit,
    onRestorePremium: () -> Unit
) {
    val premiumAccess = premiumBilling.access
    var mode by remember { mutableStateOf(SourceImportMode.Playlist) }
    var playlistUrl by remember { mutableStateOf("") }
    var serverAddress by remember { mutableStateOf("") }
    var displayName by remember { mutableStateOf("") }
    var plexToken by remember { mutableStateOf("") }
    var username by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var allowInsecureHttp by remember { mutableStateOf(false) }
    var showManualPlex by remember { mutableStateOf(false) }
    var accountConnectSubmitted by remember { mutableStateOf(false) }
    val usesCleartextHttp = serverAddress.trim().startsWith("http://", ignoreCase = true)
    val canConnect = when (mode) {
        SourceImportMode.Playlist -> playlistUrl.isNotBlank()
        SourceImportMode.Plex -> showManualPlex && premiumAccess.canUseMediaCenters &&
            serverAddress.isNotBlank() && plexToken.isNotBlank() &&
            (!usesCleartextHttp || allowInsecureHttp)
        SourceImportMode.Emby -> premiumAccess.canUseMediaCenters && serverAddress.isNotBlank() && username.isNotBlank() &&
            password.isNotEmpty() && (!usesCleartextHttp || allowInsecureHttp)
    }

    LaunchedEffect(mode) {
        if (mode != SourceImportMode.Plex) onCancelPlexAccountSignIn()
    }
    LaunchedEffect(plexSignIn.phase) {
        if (accountConnectSubmitted && plexSignIn.phase == PlexSignInPhase.Idle) onDismiss()
    }
    DisposableEffect(Unit) {
        onDispose { onCancelPlexAccountSignIn() }
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Add a source") },
        text = {
            Column(
                modifier = Modifier
                    .heightIn(max = 540.dp)
                    .verticalScroll(rememberScrollState())
            ) {
                Text(
                    "Choose a private playlist or connect a premium personal media server.",
                    color = OrbitalVueMuted,
                    fontSize = 12.sp
                )
                Spacer(modifier = Modifier.height(14.dp))
                LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    items(SourceImportMode.entries, key = SourceImportMode::name) { option ->
                        FilterChip(
                            selected = mode == option,
                            onClick = {
                                mode = option
                                if (!usesCleartextHttp) allowInsecureHttp = false
                            },
                            label = { Text(option.label) }
                        )
                    }
                }
                Spacer(modifier = Modifier.height(16.dp))

                if (mode == SourceImportMode.Playlist) {
                    OutlinedTextField(
                        value = playlistUrl,
                        onValueChange = { playlistUrl = it },
                        modifier = Modifier.fillMaxWidth(),
                        label = { Text("Playlist URL") },
                        placeholder = { Text("https://provider.example/list.m3u") },
                        leadingIcon = { Icon(Icons.Rounded.Link, contentDescription = null) },
                        singleLine = true
                    )
                    Spacer(modifier = Modifier.height(12.dp))
                    FilledTonalButton(onClick = onChooseFile, modifier = Modifier.fillMaxWidth()) {
                        Icon(Icons.Rounded.FileOpen, contentDescription = null)
                        Spacer(modifier = Modifier.width(8.dp))
                        Text("Choose M3U file")
                    }
                    Spacer(modifier = Modifier.height(12.dp))
                    Text(
                        "Online playlists refresh at launch. OrbitalVue never uploads your list.",
                        color = OrbitalVueMuted,
                        fontSize = 10.sp
                    )
                } else {
                    Text(
                        "${mode.label.uppercase()}  •  ${premiumAccess.badgeText}",
                        color = OrbitalVueTeal,
                        fontWeight = FontWeight.Bold,
                        fontSize = 10.sp,
                        letterSpacing = 1.sp
                    )
                    Spacer(modifier = Modifier.height(10.dp))
                    Text(
                        if (premiumAccess.canUseMediaCenters) premiumAccess.explanation else premiumBilling.message,
                        color = if (premiumAccess.canUseMediaCenters) OrbitalVueMuted else Color(0xFFFFC36A),
                        fontSize = 10.sp
                    )
                    if (!premiumAccess.canUseMediaCenters &&
                        (premiumBilling.isBusy || premiumBilling.canPurchase || premiumBilling.canRestore)) {
                        Spacer(modifier = Modifier.height(12.dp))
                        if (premiumBilling.isBusy) {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                CircularProgressIndicator(
                                    modifier = Modifier.size(18.dp),
                                    strokeWidth = 2.dp,
                                    color = OrbitalVueTeal
                                )
                                Spacer(modifier = Modifier.width(9.dp))
                                Text("Checking purchase status…", color = OrbitalVueMuted, fontSize = 10.sp)
                            }
                            Spacer(modifier = Modifier.height(10.dp))
                        }
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.spacedBy(10.dp)
                        ) {
                            if (premiumBilling.canPurchase) {
                                Button(
                                    onClick = onPurchasePremium,
                                    enabled = !premiumBilling.isBusy,
                                    modifier = Modifier.weight(1f)
                                ) {
                                    Text(
                                        premiumBilling.localizedPrice?.let { "Buy once — $it" }
                                            ?: "Buy once"
                                    )
                                }
                            }
                            if (premiumBilling.canRestore) {
                                OutlinedButton(
                                    onClick = onRestorePremium,
                                    enabled = !premiumBilling.isBusy,
                                    modifier = Modifier.weight(1f)
                                ) {
                                    Text("Restore purchase")
                                }
                            }
                        }
                    }
                    if (mode == SourceImportMode.Plex && !showManualPlex) {
                        Spacer(modifier = Modifier.height(14.dp))
                        PlexAccountConnectPanel(
                            state = plexSignIn,
                            enabled = premiumAccess.canUseMediaCenters,
                            onStart = {
                                accountConnectSubmitted = false
                                onStartPlexAccountSignIn()
                            },
                            onCancel = onCancelPlexAccountSignIn,
                            onConnect = { sessionId, serverId, connectionUrl, allowHttp ->
                                accountConnectSubmitted = true
                                onConnectDiscoveredPlexServer(sessionId, serverId, connectionUrl, allowHttp)
                            }
                        )
                        Spacer(modifier = Modifier.height(10.dp))
                        OutlinedButton(
                            onClick = {
                                onCancelPlexAccountSignIn()
                                showManualPlex = true
                            },
                            enabled = premiumAccess.canUseMediaCenters,
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            Text("Advanced: connect with server token")
                        }
                    } else {
                        Spacer(modifier = Modifier.height(10.dp))
                        OutlinedTextField(
                        value = serverAddress,
                        onValueChange = {
                            serverAddress = it
                            if (!it.trim().startsWith("http://", true)) allowInsecureHttp = false
                        },
                        modifier = Modifier.fillMaxWidth(),
                        label = { Text("Server address") },
                        placeholder = { Text("https://media-server.example:port") },
                        leadingIcon = { Icon(Icons.Rounded.Tv, contentDescription = null) },
                        enabled = premiumAccess.canUseMediaCenters,
                        singleLine = true
                        )
                        Spacer(modifier = Modifier.height(10.dp))
                        OutlinedTextField(
                        value = displayName,
                        onValueChange = { displayName = it },
                        modifier = Modifier.fillMaxWidth(),
                        label = { Text("Server nickname (optional)") },
                        enabled = premiumAccess.canUseMediaCenters,
                        singleLine = true
                        )
                        Spacer(modifier = Modifier.height(10.dp))
                        if (mode == SourceImportMode.Plex) {
                            OutlinedTextField(
                            value = plexToken,
                            onValueChange = { plexToken = it },
                            modifier = Modifier.fillMaxWidth(),
                            label = { Text("Plex server token") },
                            visualTransformation = PasswordVisualTransformation(),
                            enabled = premiumAccess.canUseMediaCenters,
                            singleLine = true
                            )
                            Spacer(modifier = Modifier.height(8.dp))
                            Text(
                            "Manual connection stores only this server-scoped token. Plex account sign-in remains the recommended option.",
                            color = OrbitalVueMuted,
                            fontSize = 9.sp
                            )
                        } else {
                            OutlinedTextField(
                            value = username,
                            onValueChange = { username = it },
                            modifier = Modifier.fillMaxWidth(),
                            label = { Text("Emby username") },
                            enabled = premiumAccess.canUseMediaCenters,
                            singleLine = true
                            )
                            Spacer(modifier = Modifier.height(10.dp))
                            OutlinedTextField(
                            value = password,
                            onValueChange = { password = it },
                            modifier = Modifier.fillMaxWidth(),
                            label = { Text("Emby password") },
                            visualTransformation = PasswordVisualTransformation(),
                            enabled = premiumAccess.canUseMediaCenters,
                            singleLine = true
                            )
                        }

                        if (usesCleartextHttp) {
                        Spacer(modifier = Modifier.height(14.dp))
                        HorizontalDivider(color = OrbitalVueBorder)
                        Row(
                            modifier = Modifier.fillMaxWidth().padding(top = 10.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    "Allow unencrypted local connection",
                                    color = Color(0xFFFFC36A),
                                    fontWeight = FontWeight.SemiBold,
                                    fontSize = 11.sp
                                )
                                Text(
                                    "HTTP can expose sign-in and viewing activity on this network.",
                                    color = OrbitalVueMuted,
                                    fontSize = 9.sp
                                )
                            }
                            Switch(
                                checked = allowInsecureHttp,
                                onCheckedChange = { allowInsecureHttp = it },
                                enabled = premiumAccess.canUseMediaCenters
                            )
                        }
                        }

                        Spacer(modifier = Modifier.height(14.dp))
                        Text(
                        "The server is verified before credentials are sent. Tokens are encrypted by Android Keystore and never written into the saved library.",
                        color = OrbitalVueMuted,
                        fontSize = 9.sp
                        )
                        if (mode == SourceImportMode.Plex) {
                            Spacer(modifier = Modifier.height(10.dp))
                            TextButton(
                                onClick = {
                                    showManualPlex = false
                                    accountConnectSubmitted = false
                                },
                                modifier = Modifier.fillMaxWidth()
                            ) {
                                Text("Use Plex account sign-in")
                            }
                        }
                    }
                }
            }
        },
        confirmButton = {
            if (mode != SourceImportMode.Plex || showManualPlex) Button(
                onClick = {
                    val name = displayName.trim().takeIf(String::isNotEmpty)
                    when (mode) {
                        SourceImportMode.Playlist -> onImportUrl(playlistUrl.trim())
                        SourceImportMode.Plex -> onConnectPlex(
                            serverAddress.trim(),
                            plexToken.trim(),
                            name,
                            usesCleartextHttp && allowInsecureHttp
                        )
                        SourceImportMode.Emby -> onConnectEmby(
                            serverAddress.trim(),
                            username.trim(),
                            password,
                            name,
                            usesCleartextHttp && allowInsecureHttp
                        )
                    }
                },
                enabled = canConnect
            ) {
                Text(if (mode == SourceImportMode.Playlist) "Connect" else "Connect ${mode.label}")
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text("Cancel") }
        }
    )
}

private enum class SourceImportMode(val label: String) {
    Playlist("Playlist"),
    Plex("Plex"),
    Emby("Emby")
}

@Composable
private fun NoticeBanner(message: String, onDismiss: () -> Unit, modifier: Modifier = Modifier) {
    Surface(
        modifier = modifier
            .widthIn(max = 680.dp)
            .semantics { liveRegion = LiveRegionMode.Polite },
        color = Color(0xF2142630),
        shape = RoundedCornerShape(14.dp),
        border = androidx.compose.foundation.BorderStroke(1.dp, OrbitalVueTeal.copy(alpha = 0.3f)),
        shadowElevation = 10.dp
    ) {
        Row(
            modifier = Modifier.padding(start = 15.dp, top = 10.dp, bottom = 10.dp, end = 7.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Box(modifier = Modifier.size(7.dp).background(OrbitalVueTeal, CircleShape))
            Spacer(modifier = Modifier.width(10.dp))
            Text(message, color = OrbitalVueText, fontSize = 11.sp, modifier = Modifier.weight(1f))
            IconButton(onClick = onDismiss, modifier = Modifier.size(34.dp)) {
                Icon(Icons.Rounded.Close, contentDescription = "Dismiss", modifier = Modifier.size(17.dp))
            }
        }
    }
}

private fun Int.toStringWithCommas(): String = String.format(Locale.getDefault(), "%,d", this)
