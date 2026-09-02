@file:Suppress("UnstableApiUsage")

package com.streamvue.player.playback

import android.view.ViewGroup
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.viewinterop.AndroidView
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.datasource.DefaultDataSource
import androidx.media3.datasource.DefaultHttpDataSource
import androidx.media3.exoplayer.DefaultLoadControl
import androidx.media3.exoplayer.DefaultRenderersFactory
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import androidx.media3.ui.AspectRatioFrameLayout
import androidx.media3.ui.PlayerView
import com.streamvue.player.BuildConfig
import com.streamvue.player.data.Channel
import com.streamvue.player.data.MediaCenterPlaybackEvent
import com.streamvue.player.data.MediaCenterPlaybackReport
import com.streamvue.player.data.MediaCenterPlaybackReportKind
import com.streamvue.player.data.MediaCenterPlaybackState
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive

enum class VideoScaleMode(
    val label: String,
    val resizeMode: Int,
    val forcedAspectRatio: Float? = null
) {
    Auto("Auto / fit", AspectRatioFrameLayout.RESIZE_MODE_FIT),
    Fill("Fill screen", AspectRatioFrameLayout.RESIZE_MODE_FILL),
    Zoom("Zoom / crop", AspectRatioFrameLayout.RESIZE_MODE_ZOOM),
    Ratio16x9("16:9", AspectRatioFrameLayout.RESIZE_MODE_FIT, 16f / 9f),
    Ratio4x3("4:3", AspectRatioFrameLayout.RESIZE_MODE_FIT, 4f / 3f),
    Ratio21x9("21:9", AspectRatioFrameLayout.RESIZE_MODE_FIT, 21f / 9f)
}

data class PlaybackSignal(
    val isBuffering: Boolean = true,
    val isPlaying: Boolean = false,
    val error: String? = null
)

@Composable
fun rememberStreamPlayer(
    channel: Channel?,
    onSignal: (PlaybackSignal) -> Unit,
    onPlaybackReport: (String, MediaCenterPlaybackReport) -> Unit
): ExoPlayer? {
    val context = LocalContext.current
    val signalCallback = rememberUpdatedState(onSignal)
    val reportCallback = rememberUpdatedState(onPlaybackReport)
    if (channel == null) return null

    val player = remember(channel.id, channel.streamUri, channel.requestHeaders, channel.startPositionMs) {
        val userAgent = channel.requestHeaders["User-Agent"]
            ?: "OrbitalVue Android/${BuildConfig.VERSION_NAME}"
        val otherHeaders = channel.requestHeaders.filterKeys { !it.equals("User-Agent", ignoreCase = true) }
        val httpFactory = DefaultHttpDataSource.Factory()
            .setUserAgent(userAgent)
            .setAllowCrossProtocolRedirects(true)
            .setConnectTimeoutMs(15_000)
            .setReadTimeoutMs(30_000)
            .setDefaultRequestProperties(otherHeaders)
        val dataSourceFactory = DefaultDataSource.Factory(context, httpFactory)
        val mediaSourceFactory = DefaultMediaSourceFactory(context)
            .setDataSourceFactory(dataSourceFactory)
        val renderersFactory = DefaultRenderersFactory(context)
            .setEnableDecoderFallback(true)
        val loadControl = DefaultLoadControl.Builder()
            .setBufferDurationsMs(
                10_000,
                50_000,
                1_500,
                3_000
            )
            .setPrioritizeTimeOverSizeThresholds(true)
            .build()

        ExoPlayer.Builder(context, renderersFactory, mediaSourceFactory)
            .setLoadControl(loadControl)
            .setHandleAudioBecomingNoisy(true)
            .setVideoChangeFrameRateStrategy(C.VIDEO_CHANGE_FRAME_RATE_STRATEGY_ONLY_IF_SEAMLESS)
            .build()
            .apply {
                setAudioAttributes(
                    AudioAttributes.Builder()
                        .setUsage(C.USAGE_MEDIA)
                        .setContentType(C.AUDIO_CONTENT_TYPE_MOVIE)
                        .build(),
                    true
                )
                setMediaItem(
                    MediaItem.Builder()
                        .setUri(channel.streamUri)
                        .setMediaMetadata(
                            MediaMetadata.Builder()
                                .setTitle(channel.name)
                                .setArtist(channel.group)
                                .build()
                        )
                        .build()
                )
                channel.startPositionMs?.takeIf { it > 0 }?.let { position -> seekTo(position) }
                playWhenReady = true
                prepare()
            }
    }
    val reportSession = remember(player, channel.playbackReportSessionId) {
        AndroidPlaybackReportSession(channel.playbackReportSessionId) { sessionId, report ->
            reportCallback.value(sessionId, report)
        }
    }

    LaunchedEffect(player, reportSession) {
        while (isActive) {
            delay(PLAYBACK_REPORT_INTERVAL_MS)
            reportSession.progress(player)
        }
    }

    DisposableEffect(player) {
        val listener = object : Player.Listener {
            override fun onPlaybackStateChanged(playbackState: Int) {
                signalCallback.value(
                    PlaybackSignal(
                        isBuffering = playbackState == Player.STATE_BUFFERING,
                        isPlaying = player.isPlaying,
                        error = null
                    )
                )
                reportSession.stateChanged(player, playbackState)
            }

            override fun onIsPlayingChanged(isPlaying: Boolean) {
                signalCallback.value(
                    PlaybackSignal(
                        isBuffering = player.playbackState == Player.STATE_BUFFERING,
                        isPlaying = isPlaying,
                        error = null
                    )
                )
                reportSession.playingChanged(player, isPlaying)
            }

            override fun onPlayerError(error: PlaybackException) {
                signalCallback.value(
                    PlaybackSignal(
                        isBuffering = false,
                        isPlaying = false,
                        error = error.errorCodeName.replace('_', ' ')
                    )
                )
                reportSession.stop(player)
            }
        }
        player.addListener(listener)
        onDispose {
            reportSession.stop(player)
            player.removeListener(listener)
            player.release()
        }
    }

    return player
}

private class AndroidPlaybackReportSession(
    private val sessionId: String?,
    private val callback: (String, MediaCenterPlaybackReport) -> Unit
) {
    private var started = false
    private var paused = false
    private var stopped = false

    fun playingChanged(player: Player, isPlaying: Boolean) {
        if (sessionId == null || stopped) return
        if (isPlaying) {
            if (!started) {
                started = true
                paused = false
                send(player, MediaCenterPlaybackReportKind.Started, MediaCenterPlaybackState.Playing)
            } else if (paused) {
                paused = false
                send(
                    player,
                    MediaCenterPlaybackReportKind.Progress,
                    MediaCenterPlaybackState.Playing,
                    MediaCenterPlaybackEvent.Unpause
                )
            }
        } else if (started && player.playbackState == Player.STATE_READY && !player.playWhenReady && !paused) {
            paused = true
            send(
                player,
                MediaCenterPlaybackReportKind.Progress,
                MediaCenterPlaybackState.Paused,
                MediaCenterPlaybackEvent.Pause
            )
        }
    }

    fun stateChanged(player: Player, playbackState: Int) {
        if (sessionId == null || stopped) return
        when {
            playbackState == Player.STATE_ENDED -> stop(player)
            started && playbackState == Player.STATE_BUFFERING -> send(
                player,
                MediaCenterPlaybackReportKind.Progress,
                MediaCenterPlaybackState.Buffering,
                MediaCenterPlaybackEvent.TimeUpdate
            )
        }
    }

    fun progress(player: Player) {
        if (!started || stopped || paused) return
        send(
            player,
            MediaCenterPlaybackReportKind.Progress,
            if (player.playbackState == Player.STATE_BUFFERING) {
                MediaCenterPlaybackState.Buffering
            } else {
                MediaCenterPlaybackState.Playing
            },
            MediaCenterPlaybackEvent.TimeUpdate
        )
    }

    fun stop(player: Player) {
        if (!started || stopped) return
        stopped = true
        send(player, MediaCenterPlaybackReportKind.Stopped, MediaCenterPlaybackState.Playing)
    }

    private fun send(
        player: Player,
        kind: MediaCenterPlaybackReportKind,
        state: MediaCenterPlaybackState,
        event: MediaCenterPlaybackEvent? = null
    ) {
        val safeSessionId = sessionId ?: return
        val durationMs = player.duration.takeIf { it != C.TIME_UNSET && it > 0 }
        callback(
            safeSessionId,
            MediaCenterPlaybackReport(
                kind = kind,
                state = state,
                positionMs = player.currentPosition.coerceAtLeast(0),
                durationMs = durationMs,
                event = event,
                canSeek = player.isCurrentMediaItemSeekable,
                isMuted = player.volume <= 0f,
                volumePercent = (player.volume * 100f).toInt().coerceIn(0, 100)
            )
        )
    }
}

private const val PLAYBACK_REPORT_INTERVAL_MS = 10_000L

@Composable
fun StreamPlayerSurface(
    player: ExoPlayer,
    scaleMode: VideoScaleMode,
    modifier: Modifier = Modifier
) {
    BoxWithConstraints(
        modifier = modifier.background(Color.Black),
        contentAlignment = Alignment.Center
    ) {
        val forcedRatio = scaleMode.forcedAspectRatio
        val playerModifier = when {
            forcedRatio == null -> Modifier.fillMaxSize()
            maxWidth / maxHeight > forcedRatio -> Modifier.fillMaxHeight().aspectRatio(forcedRatio)
            else -> Modifier.fillMaxWidth().aspectRatio(forcedRatio)
        }

        Box(modifier = playerModifier, contentAlignment = Alignment.Center) {
            AndroidView(
                modifier = Modifier.fillMaxSize(),
                factory = { viewContext ->
                    PlayerView(viewContext).apply {
                        layoutParams = ViewGroup.LayoutParams(
                            ViewGroup.LayoutParams.MATCH_PARENT,
                            ViewGroup.LayoutParams.MATCH_PARENT
                        )
                        this.player = player
                        useController = true
                        controllerAutoShow = true
                        controllerShowTimeoutMs = 3_500
                        keepScreenOn = true
                        resizeMode = scaleMode.resizeMode
                        setShowBuffering(PlayerView.SHOW_BUFFERING_WHEN_PLAYING)
                    }
                },
                update = { playerView ->
                    playerView.player = player
                    playerView.resizeMode = scaleMode.resizeMode
                }
            )
        }
    }
}
