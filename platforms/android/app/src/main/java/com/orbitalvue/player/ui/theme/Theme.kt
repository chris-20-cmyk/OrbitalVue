package com.orbitalvue.player.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

val OrbitalVueTeal = Color(0xFF35E7D3)
val OrbitalVueTealDim = Color(0xFF173D41)
val OrbitalVueBackground = Color(0xFF05080F)
val OrbitalVueSurface = Color(0xFF0B1220)
val OrbitalVueSurfaceRaised = Color(0xFF111B2B)
val OrbitalVueBorder = Color(0xFF26354A)
val OrbitalVueText = Color(0xFFEAF0F7)
val OrbitalVueMuted = Color(0xFF8B9AAF)
val OrbitalVueError = Color(0xFFFF6B81)

private val OrbitalVueColors = darkColorScheme(
    primary = OrbitalVueTeal,
    onPrimary = Color(0xFF03100F),
    primaryContainer = OrbitalVueTealDim,
    onPrimaryContainer = Color(0xFFB8FFF7),
    secondary = Color(0xFF8FB9FF),
    background = OrbitalVueBackground,
    onBackground = OrbitalVueText,
    surface = OrbitalVueSurface,
    onSurface = OrbitalVueText,
    surfaceVariant = OrbitalVueSurfaceRaised,
    onSurfaceVariant = OrbitalVueMuted,
    outline = OrbitalVueBorder,
    error = OrbitalVueError
)

@Composable
fun OrbitalVueTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = OrbitalVueColors,
        content = content
    )
}
