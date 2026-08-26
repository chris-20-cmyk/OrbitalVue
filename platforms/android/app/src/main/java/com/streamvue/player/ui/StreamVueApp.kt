package com.streamvue.player.ui

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
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import java.util.Locale
import androidx.media3.exoplayer.ExoPlayer
import com.streamvue.player.AppUiState
import com.streamvue.player.ChannelSection
import com.streamvue.player.GroupSummary
import com.streamvue.player.data.Channel
import com.streamvue.player.playback.PlaybackSignal
import com.streamvue.player.playback.StreamPlayerSurface
import com.streamvue.player.playback.VideoScaleMode
import com.streamvue.player.playback.rememberStreamPlayer
import com.streamvue.player.ui.theme.StreamVueBackground
import com.streamvue.player.ui.theme.StreamVueBorder
import com.streamvue.player.ui.theme.StreamVueError
import com.streamvue.player.ui.theme.StreamVueMuted
import com.streamvue.player.ui.theme.StreamVueSurface
import com.streamvue.player.ui.theme.StreamVueSurfaceRaised
import com.streamvue.player.ui.theme.StreamVueTeal
import com.streamvue.player.ui.theme.StreamVueTealDim
import com.streamvue.player.ui.theme.StreamVueText

@OptIn(ExperimentalFoundationApi::class, ExperimentalMaterial3Api::class)
@Composable
fun StreamVueApp(
    state: AppUiState,
    isTelevision: Boolean,
    onChooseFile: () -> Unit,
    onImportUrl: (String) -> Unit,
    onRefresh: () -> Unit,
    onSelectGroup: (String?) -> Unit,
    onQueryChanged: (String) -> Unit,
    onSelectChannel: (Channel) -> Unit,
    onDismissNotice: () -> Unit,
    onDismissError: () -> Unit,
    onFullscreenChanged: (Boolean) -> Unit
) {
    var showImport by remember { mutableStateOf(false) }
    var isFullscreen by remember { mutableStateOf(false) }
    var scaleMode by remember { mutableStateOf(VideoScaleMode.Auto) }
    var playbackSignal by remember(state.selectedChannel?.id) { mutableStateOf(PlaybackSignal()) }
    val player = rememberStreamPlayer(state.selectedChannel) { playbackSignal = it }

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
                    listOf(StreamVueBackground, Color(0xFF07111C), StreamVueBackground)
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
                            color = StreamVueTeal,
                            trackColor = StreamVueTealDim
                        )
                    } else {
                        Spacer(modifier = Modifier.height(3.dp))
                    }

                    when {
                        state.catalog == null && state.isLoading -> LoadingLibrary(state.loadingLabel)
                        state.catalog == null -> Onboarding(
                            isTelevision = isTelevision,
                            onChooseFile = onChooseFile,
                            onConnectUrl = { showImport = true }
                        )
                        useWideLayout -> WideLibrary(
                            state = state,
                            player = player,
                            signal = playbackSignal,
                            scaleMode = scaleMode,
                            onScaleModeChanged = { scaleMode = it },
                            onSelectGroup = onSelectGroup,
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
            onDismiss = { showImport = false },
            onChooseFile = {
                showImport = false
                onChooseFile()
            },
            onImportUrl = { value ->
                showImport = false
                onImportUrl(value)
            }
        )
    }

    state.error?.let { message ->
        AlertDialog(
            onDismissRequest = onDismissError,
            title = { Text("StreamVue needs attention") },
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
            color = StreamVueTealDim,
            border = androidx.compose.foundation.BorderStroke(1.dp, StreamVueTeal.copy(alpha = 0.35f))
        ) {
            Box(contentAlignment = Alignment.Center) {
                Icon(
                    Icons.Rounded.PlayArrow,
                    contentDescription = null,
                    tint = StreamVueTeal,
                    modifier = Modifier.size(28.dp)
                )
            }
        }
        Spacer(modifier = Modifier.width(12.dp))
        Column {
            Text(
                text = "STREAMVUE",
                color = StreamVueText,
                fontWeight = FontWeight.Black,
                letterSpacing = 2.sp,
                fontSize = if (compact) 16.sp else 19.sp
            )
            Text(
                text = if (state.catalog == null) "YOUR SIGNAL. BEAUTIFULLY ORGANIZED."
                else "${state.catalog.channels.size.toStringWithCommas()} CHANNELS  •  ${state.groups.size.toStringWithCommas()} GROUPS",
                color = StreamVueMuted,
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
            Icon(Icons.Rounded.Refresh, contentDescription = "Refresh playlist")
        }
        if (compact) {
            IconButton(onClick = onAddSource) {
                Icon(Icons.Rounded.Add, contentDescription = "Add playlist")
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
        color = StreamVueSurfaceRaised,
        border = androidx.compose.foundation.BorderStroke(1.dp, StreamVueBorder)
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 13.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Box(
                modifier = Modifier
                    .size(7.dp)
                    .background(
                        if (source.usedCachedFallback) Color(0xFFFFB454) else StreamVueTeal,
                        CircleShape
                    )
            )
            Spacer(modifier = Modifier.width(8.dp))
            Column {
                Text(
                    text = if (source.usedCachedFallback) "OFFLINE COPY" else "SOURCE READY",
                    color = if (source.usedCachedFallback) Color(0xFFFFC47A) else StreamVueTeal,
                    fontSize = 8.sp,
                    fontWeight = FontWeight.Bold
                )
                Text(
                    text = source.displayLocation,
                    color = StreamVueMuted,
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
            selectedGroup = state.selectedGroup,
            onSelectGroup = onSelectGroup,
            modifier = Modifier.width(220.dp).fillMaxHeight()
        )
        ChannelBrowser(
            state = state,
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
    selectedGroup: String?,
    onSelectGroup: (String?) -> Unit,
    modifier: Modifier = Modifier
) {
    Surface(
        modifier = modifier,
        color = StreamVueSurface.copy(alpha = 0.94f),
        shape = RoundedCornerShape(18.dp),
        border = androidx.compose.foundation.BorderStroke(1.dp, StreamVueBorder)
    ) {
        Column(modifier = Modifier.padding(12.dp)) {
            Text(
                "CHANNEL GROUPS",
                color = StreamVueMuted,
                fontSize = 9.sp,
                fontWeight = FontWeight.Bold,
                letterSpacing = 1.5.sp,
                modifier = Modifier.padding(8.dp)
            )
            LazyColumn(
                verticalArrangement = Arrangement.spacedBy(5.dp),
                modifier = Modifier.fillMaxSize()
            ) {
                item(key = "all") {
                    GroupRow(
                        name = "All channels",
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
            .onFocusChanged { focused = it.isFocused },
        color = if (selected) StreamVueTealDim else Color.Transparent,
        shape = RoundedCornerShape(11.dp),
        border = when {
            focused -> androidx.compose.foundation.BorderStroke(2.dp, StreamVueTeal)
            selected -> androidx.compose.foundation.BorderStroke(1.dp, StreamVueTeal.copy(alpha = 0.45f))
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
                color = if (selected) StreamVueText else StreamVueMuted,
                fontSize = 11.sp,
                fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Normal,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis
            )
            Text(count.toStringWithCommas(), color = StreamVueMuted, fontSize = 9.sp)
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
    onQueryChanged: (String) -> Unit,
    onSelectChannel: (Channel) -> Unit,
    modifier: Modifier = Modifier
) {
    Surface(
        modifier = modifier,
        color = StreamVueSurface.copy(alpha = 0.94f),
        shape = RoundedCornerShape(18.dp),
        border = androidx.compose.foundation.BorderStroke(1.dp, StreamVueBorder)
    ) {
        Column(modifier = Modifier.fillMaxSize()) {
            Column(modifier = Modifier.padding(14.dp)) {
                OutlinedTextField(
                    value = state.query,
                    onValueChange = onQueryChanged,
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                    shape = RoundedCornerShape(12.dp),
                    placeholder = { Text("Search channels or groups") },
                    leadingIcon = { Icon(Icons.Rounded.Search, contentDescription = null) }
                )
                Spacer(modifier = Modifier.height(8.dp))
                Text(
                    "${state.visibleChannels.size.toStringWithCommas()} RESULTS  •  ${state.sections.size.toStringWithCommas()} SECTIONS",
                    color = StreamVueMuted,
                    fontSize = 8.sp,
                    fontWeight = FontWeight.Bold,
                    letterSpacing = 1.sp
                )
            }
            HorizontalDivider(color = StreamVueBorder)
            if (state.visibleChannels.isEmpty()) {
                Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text("No channels match this view", color = StreamVueMuted)
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
                        Box(modifier = Modifier.size(4.dp, 18.dp).background(StreamVueTeal, CircleShape))
                        Spacer(modifier = Modifier.width(8.dp))
                        Text(
                            section.name.uppercase(),
                            modifier = Modifier.weight(1f),
                            color = StreamVueText,
                            fontSize = 9.sp,
                            fontWeight = FontWeight.Bold,
                            letterSpacing = 1.sp,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis
                        )
                        Text(section.channels.size.toStringWithCommas(), color = StreamVueMuted, fontSize = 8.sp)
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
        color = if (selected) StreamVueTealDim else Color.Transparent,
        shape = RoundedCornerShape(12.dp),
        border = when {
            focused -> androidx.compose.foundation.BorderStroke(2.dp, StreamVueTeal)
            selected -> androidx.compose.foundation.BorderStroke(1.dp, StreamVueTeal.copy(alpha = 0.5f))
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
                color = StreamVueSurfaceRaised
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Text(channel.initials, color = StreamVueTeal, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                }
            }
            Spacer(modifier = Modifier.width(11.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    channel.name,
                    color = StreamVueText,
                    fontSize = 12.sp,
                    fontWeight = FontWeight.SemiBold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
                Spacer(modifier = Modifier.height(3.dp))
                Text(
                    channel.group,
                    color = StreamVueMuted,
                    fontSize = 9.sp,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
            }
            Text(channel.kind.label, color = StreamVueTeal, fontSize = 7.sp, fontWeight = FontWeight.Bold)
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
        color = StreamVueSurface.copy(alpha = 0.94f),
        border = androidx.compose.foundation.BorderStroke(1.dp, StreamVueBorder)
    ) {
        Column(modifier = Modifier.fillMaxSize()) {
            Row(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 11.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        channel?.name ?: "Ready when you are",
                        color = StreamVueText,
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Bold,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                    Text(
                        channel?.group ?: "Select a channel to tune the native player",
                        color = StreamVueMuted,
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
                                Text("Playback could not start", color = StreamVueText, fontWeight = FontWeight.Bold)
                                Spacer(modifier = Modifier.height(5.dp))
                                Text(error, color = StreamVueError, fontSize = 10.sp)
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
                    color = StreamVueMuted,
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
            Surface(modifier = Modifier.size(68.dp), shape = CircleShape, color = StreamVueTealDim) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(Icons.Rounded.PlayArrow, null, tint = StreamVueTeal, modifier = Modifier.size(42.dp))
                }
            }
            Spacer(modifier = Modifier.height(13.dp))
            Text("A better signal path", color = StreamVueText, fontWeight = FontWeight.Bold)
            Text("Native playback with no browser transcoding", color = StreamVueMuted, fontSize = 10.sp)
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
        signal.error != null -> StreamVueError
        signal.isBuffering -> Color(0xFFFFC36A)
        else -> StreamVueTeal
    }
    Surface(shape = RoundedCornerShape(30.dp), color = color.copy(alpha = 0.13f)) {
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
        IconButton(onClick = { expanded = true }, enabled = enabled) {
            Icon(Icons.Rounded.AspectRatio, contentDescription = "Change screen ratio")
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            VideoScaleMode.entries.forEach { option ->
                DropdownMenuItem(
                    text = {
                        Text(
                            option.label,
                            color = if (option == mode) StreamVueTeal else StreamVueText
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
                    Text(channel.name, color = StreamVueText, fontWeight = FontWeight.Bold, fontSize = 17.sp)
                    Text(channel.group, color = StreamVueMuted, fontSize = 10.sp)
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
            CircularProgressIndicator(color = StreamVueTeal)
            Spacer(modifier = Modifier.height(16.dp))
            Text(label.ifBlank { "Preparing your library…" }, color = StreamVueMuted)
        }
    }
}

@Composable
private fun Onboarding(
    isTelevision: Boolean,
    onChooseFile: () -> Unit,
    onConnectUrl: () -> Unit
) {
    Box(modifier = Modifier.fillMaxSize().padding(20.dp), contentAlignment = Alignment.Center) {
        Surface(
            modifier = Modifier.widthIn(max = 760.dp).fillMaxWidth(),
            color = StreamVueSurface.copy(alpha = 0.96f),
            shape = RoundedCornerShape(28.dp),
            border = androidx.compose.foundation.BorderStroke(1.dp, StreamVueBorder)
        ) {
            Column(
                modifier = Modifier.padding(if (isTelevision) 48.dp else 28.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Surface(modifier = Modifier.size(82.dp), shape = RoundedCornerShape(24.dp), color = StreamVueTealDim) {
                    Box(contentAlignment = Alignment.Center) {
                        Icon(Icons.Rounded.Tv, contentDescription = null, tint = StreamVueTeal, modifier = Modifier.size(45.dp))
                    }
                }
                Spacer(modifier = Modifier.height(22.dp))
                Text(
                    "Bring your channels into focus",
                    color = StreamVueText,
                    fontWeight = FontWeight.Black,
                    fontSize = if (isTelevision) 30.sp else 24.sp
                )
                Spacer(modifier = Modifier.height(9.dp))
                Text(
                    "Connect your own M3U or M3U8 source. StreamVue keeps the playlist private, preserves every group, and refreshes online sources when the app opens.",
                    color = StreamVueMuted,
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
                    Button(onClick = onConnectUrl) {
                        Icon(Icons.Rounded.Link, contentDescription = null)
                        Spacer(modifier = Modifier.width(8.dp))
                        Text("Connect URL")
                    }
                }
                Spacer(modifier = Modifier.height(17.dp))
                Text(
                    "StreamVue is a player. It never bundles or sells channels.",
                    color = StreamVueMuted,
                    fontSize = 9.sp
                )
            }
        }
    }
}

@Composable
private fun ImportSourceDialog(
    onDismiss: () -> Unit,
    onChooseFile: () -> Unit,
    onImportUrl: (String) -> Unit
) {
    var url by remember { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Connect a playlist") },
        text = {
            Column {
                Text(
                    "Paste a direct M3U/M3U8 URL or choose a file already on this device.",
                    color = StreamVueMuted,
                    fontSize = 12.sp
                )
                Spacer(modifier = Modifier.height(16.dp))
                OutlinedTextField(
                    value = url,
                    onValueChange = { url = it },
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
            }
        },
        confirmButton = {
            Button(onClick = { onImportUrl(url) }, enabled = url.isNotBlank()) {
                Text("Connect")
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text("Cancel") }
        }
    )
}

@Composable
private fun NoticeBanner(message: String, onDismiss: () -> Unit, modifier: Modifier = Modifier) {
    Surface(
        modifier = modifier.widthIn(max = 680.dp),
        color = Color(0xF2142630),
        shape = RoundedCornerShape(14.dp),
        border = androidx.compose.foundation.BorderStroke(1.dp, StreamVueTeal.copy(alpha = 0.3f)),
        shadowElevation = 10.dp
    ) {
        Row(
            modifier = Modifier.padding(start = 15.dp, top = 10.dp, bottom = 10.dp, end = 7.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Box(modifier = Modifier.size(7.dp).background(StreamVueTeal, CircleShape))
            Spacer(modifier = Modifier.width(10.dp))
            Text(message, color = StreamVueText, fontSize = 11.sp, modifier = Modifier.weight(1f))
            IconButton(onClick = onDismiss, modifier = Modifier.size(34.dp)) {
                Icon(Icons.Rounded.Close, contentDescription = "Dismiss", modifier = Modifier.size(17.dp))
            }
        }
    }
}

private fun Int.toStringWithCommas(): String = String.format(Locale.getDefault(), "%,d", this)
