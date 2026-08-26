package com.streamvue.player.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

val StreamVueTeal = Color(0xFF35E7D3)
val StreamVueTealDim = Color(0xFF173D41)
val StreamVueBackground = Color(0xFF05080F)
val StreamVueSurface = Color(0xFF0B1220)
val StreamVueSurfaceRaised = Color(0xFF111B2B)
val StreamVueBorder = Color(0xFF26354A)
val StreamVueText = Color(0xFFEAF0F7)
val StreamVueMuted = Color(0xFF8B9AAF)
val StreamVueError = Color(0xFFFF6B81)

private val StreamVueColors = darkColorScheme(
    primary = StreamVueTeal,
    onPrimary = Color(0xFF03100F),
    primaryContainer = StreamVueTealDim,
    onPrimaryContainer = Color(0xFFB8FFF7),
    secondary = Color(0xFF8FB9FF),
    background = StreamVueBackground,
    onBackground = StreamVueText,
    surface = StreamVueSurface,
    onSurface = StreamVueText,
    surfaceVariant = StreamVueSurfaceRaised,
    onSurfaceVariant = StreamVueMuted,
    outline = StreamVueBorder,
    error = StreamVueError
)

@Composable
fun StreamVueTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = StreamVueColors,
        content = content
    )
}
