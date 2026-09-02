package com.orbitalvue.player.ui

import android.graphics.Bitmap
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.Launch
import androidx.compose.material.icons.rounded.KeyboardArrowDown
import androidx.compose.material.icons.rounded.Person
import androidx.compose.material.icons.rounded.Storage
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.google.zxing.BarcodeFormat
import com.google.zxing.EncodeHintType
import com.google.zxing.qrcode.QRCodeWriter
import com.google.zxing.qrcode.decoder.ErrorCorrectionLevel
import com.orbitalvue.player.PlexSignInPhase
import com.orbitalvue.player.PlexSignInUiState
import com.orbitalvue.player.data.MediaCenterUrlPolicy
import com.orbitalvue.player.data.PlexDiscoveredServer
import com.orbitalvue.player.data.PlexServerConnectionChoice
import com.orbitalvue.player.ui.theme.OrbitalVueMuted
import com.orbitalvue.player.ui.theme.OrbitalVueTeal
import com.orbitalvue.player.ui.theme.OrbitalVueText
import java.net.URI
import java.time.Duration
import java.time.Instant

@Composable
internal fun PlexAccountConnectPanel(
    state: PlexSignInUiState,
    enabled: Boolean,
    onStart: () -> Unit,
    onCancel: () -> Unit,
    onConnect: (sessionId: String, serverId: String, connectionUrl: String, allowHttp: Boolean) -> Unit
) {
    when (state.phase) {
        PlexSignInPhase.Preparing -> PreparingPlexSignIn()
        PlexSignInPhase.Waiting -> state.challenge?.let { challenge ->
            PlexSignInChallenge(
                authorizationUrl = challenge.authorizationUrl,
                expiresAt = challenge.expiresAt,
                onCancel = onCancel
            )
        }
        PlexSignInPhase.Ready, PlexSignInPhase.Connecting -> state.discovery?.let { discovery ->
            PlexServerPicker(
                discovery = discovery,
                isConnecting = state.phase == PlexSignInPhase.Connecting,
                onConnect = onConnect,
                onUseAnotherAccount = onStart
            )
        }
        PlexSignInPhase.Failed, PlexSignInPhase.Idle -> {
            state.error?.let {
                Text(it, color = Color(0xFFFFC36A), fontSize = 10.sp)
                Spacer(modifier = Modifier.height(10.dp))
            }
            Button(onClick = onStart, enabled = enabled, modifier = Modifier.fillMaxWidth()) {
                Icon(Icons.Rounded.Person, contentDescription = null)
                Spacer(modifier = Modifier.width(8.dp))
                Text("Sign in with Plex")
            }
            Spacer(modifier = Modifier.height(8.dp))
            Text(
                "Approve OrbitalVue in Plex, then choose one of the servers Plex discovers for your account. OrbitalVue never receives your Plex password.",
                color = OrbitalVueMuted,
                fontSize = 9.sp
            )
        }
    }
}

@Composable
private fun PreparingPlexSignIn() {
    Row(verticalAlignment = Alignment.CenterVertically) {
        CircularProgressIndicator(modifier = Modifier.size(20.dp), strokeWidth = 2.dp, color = OrbitalVueTeal)
        Spacer(modifier = Modifier.width(10.dp))
        Text("Preparing secure Plex sign-in…", color = OrbitalVueText, fontWeight = FontWeight.SemiBold)
    }
}

@Composable
private fun PlexSignInChallenge(
    authorizationUrl: String,
    expiresAt: Instant,
    onCancel: () -> Unit
) {
    val uriHandler = LocalUriHandler.current
    val minutes = Duration.between(Instant.now(), expiresAt).toMinutes().coerceAtLeast(1)
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        PlexQrCode(authorizationUrl)
        Spacer(modifier = Modifier.height(12.dp))
        Text(
            "Scan to approve OrbitalVue in Plex",
            color = OrbitalVueText,
            fontWeight = FontWeight.Bold,
            textAlign = TextAlign.Center
        )
        Spacer(modifier = Modifier.height(6.dp))
        Text(
            "OrbitalVue checks the protected PIN automatically. This request expires in about $minutes minutes.",
            color = OrbitalVueMuted,
            fontSize = 9.sp,
            textAlign = TextAlign.Center
        )
        Spacer(modifier = Modifier.height(12.dp))
        Button(
            onClick = { runCatching { uriHandler.openUri(authorizationUrl) } },
            modifier = Modifier.fillMaxWidth()
        ) {
            Icon(Icons.AutoMirrored.Rounded.Launch, contentDescription = null)
            Spacer(modifier = Modifier.width(8.dp))
            Text("Open Plex sign-in")
        }
        Spacer(modifier = Modifier.height(10.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            CircularProgressIndicator(modifier = Modifier.size(17.dp), strokeWidth = 2.dp, color = OrbitalVueTeal)
            Spacer(modifier = Modifier.width(8.dp))
            Text("Waiting for approval", color = OrbitalVueMuted, fontSize = 10.sp)
        }
        TextButton(onClick = onCancel) { Text("Cancel sign-in") }
    }
}

@Composable
private fun PlexServerPicker(
    discovery: com.orbitalvue.player.data.PlexServerDiscovery,
    isConnecting: Boolean,
    onConnect: (String, String, String, Boolean) -> Unit,
    onUseAnotherAccount: () -> Unit
) {
    var selectedServerId by remember(discovery.sessionId) {
        mutableStateOf(discovery.servers.firstOrNull()?.serverId.orEmpty())
    }
    var selectedConnectionUrl by remember(discovery.sessionId) {
        mutableStateOf(discovery.servers.firstOrNull()?.preferredConnection?.url.orEmpty())
    }
    var allowHttp by remember(discovery.sessionId, selectedConnectionUrl) { mutableStateOf(false) }
    val selectedServer = discovery.servers.firstOrNull { it.serverId == selectedServerId }
        ?: discovery.servers.firstOrNull()
    val selectedConnection = selectedServer?.connections?.firstOrNull { it.url == selectedConnectionUrl }
        ?: selectedServer?.preferredConnection

    if (selectedServer == null || selectedConnection == null) {
        Text("Plex did not provide a usable server connection.", color = Color(0xFFFFC36A))
        return
    }

    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        PlexServerMenu(
            servers = discovery.servers,
            selected = selectedServer,
            enabled = !isConnecting,
            onSelected = { server ->
                selectedServerId = server.serverId
                selectedConnectionUrl = server.preferredConnection?.url.orEmpty()
                allowHttp = false
            }
        )
        if (selectedServer.connections.size > 1) {
            PlexConnectionMenu(
                connections = selectedServer.connections,
                selected = selectedConnection,
                enabled = !isConnecting,
                onSelected = { connection ->
                    selectedConnectionUrl = connection.url
                    allowHttp = false
                }
            )
        }
        Text(
            safeDisplayLocation(selectedConnection.url),
            color = OrbitalVueMuted,
            fontSize = 9.sp
        )
        if (!selectedConnection.isSecure) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(modifier = Modifier.weight(1f)) {
                    Text("Allow unencrypted local connection", color = Color(0xFFFFC36A), fontSize = 10.sp)
                    Text(
                        "HTTP can expose the server token and viewing activity on this network.",
                        color = OrbitalVueMuted,
                        fontSize = 8.sp
                    )
                }
                Switch(checked = allowHttp, onCheckedChange = { allowHttp = it }, enabled = !isConnecting)
            }
        }
        Button(
            onClick = {
                onConnect(
                    discovery.sessionId,
                    selectedServer.serverId,
                    selectedConnection.url,
                    !selectedConnection.isSecure && allowHttp
                )
            },
            enabled = !isConnecting && (selectedConnection.isSecure || allowHttp),
            modifier = Modifier.fillMaxWidth()
        ) {
            if (isConnecting) {
                CircularProgressIndicator(modifier = Modifier.size(18.dp), strokeWidth = 2.dp)
            } else {
                Icon(Icons.Rounded.Storage, contentDescription = null)
            }
            Spacer(modifier = Modifier.width(8.dp))
            Text(if (isConnecting) "Connecting server…" else "Connect ${selectedServer.name}")
        }
        TextButton(onClick = onUseAnotherAccount, enabled = !isConnecting, modifier = Modifier.fillMaxWidth()) {
            Text("Use another Plex account")
        }
    }
}

@Composable
private fun PlexServerMenu(
    servers: List<PlexDiscoveredServer>,
    selected: PlexDiscoveredServer,
    enabled: Boolean,
    onSelected: (PlexDiscoveredServer) -> Unit
) {
    var expanded by remember { mutableStateOf(false) }
    Box {
        OutlinedButton(onClick = { expanded = true }, enabled = enabled, modifier = Modifier.fillMaxWidth()) {
            Text(if (selected.isOwned) selected.name else "${selected.name} · Shared", modifier = Modifier.weight(1f))
            Icon(Icons.Rounded.KeyboardArrowDown, contentDescription = "Choose Plex server")
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            servers.forEach { server ->
                DropdownMenuItem(
                    text = { Text(if (server.isOwned) server.name else "${server.name} · Shared") },
                    onClick = { expanded = false; onSelected(server) }
                )
            }
        }
    }
}

@Composable
private fun PlexConnectionMenu(
    connections: List<PlexServerConnectionChoice>,
    selected: PlexServerConnectionChoice,
    enabled: Boolean,
    onSelected: (PlexServerConnectionChoice) -> Unit
) {
    var expanded by remember { mutableStateOf(false) }
    Box {
        OutlinedButton(onClick = { expanded = true }, enabled = enabled, modifier = Modifier.fillMaxWidth()) {
            Text(connectionLabel(selected), modifier = Modifier.weight(1f))
            Icon(Icons.Rounded.KeyboardArrowDown, contentDescription = "Choose Plex connection")
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            connections.forEach { connection ->
                DropdownMenuItem(
                    text = { Text(connectionLabel(connection)) },
                    onClick = { expanded = false; onSelected(connection) }
                )
            }
        }
    }
}

@Composable
private fun PlexQrCode(value: String) {
    val bitmap = remember(value) { createQrBitmap(value) }
    Surface(
        modifier = Modifier.size(184.dp),
        color = Color.White,
        shape = RoundedCornerShape(16.dp)
    ) {
        if (bitmap != null) {
            Image(
                bitmap = bitmap.asImageBitmap(),
                contentDescription = "QR code for Plex sign-in",
                modifier = Modifier.padding(10.dp)
            )
        } else {
            Box(contentAlignment = Alignment.Center, modifier = Modifier.background(Color.White)) {
                Text("Open Plex sign-in below", color = Color.Black, textAlign = TextAlign.Center)
            }
        }
    }
}

private fun createQrBitmap(value: String): Bitmap? = runCatching {
    val matrix = QRCodeWriter().encode(
        value,
        BarcodeFormat.QR_CODE,
        512,
        512,
        mapOf(
            EncodeHintType.ERROR_CORRECTION to ErrorCorrectionLevel.M,
            EncodeHintType.MARGIN to 1
        )
    )
    val pixels = IntArray(matrix.width * matrix.height)
    for (y in 0 until matrix.height) {
        for (x in 0 until matrix.width) {
            pixels[y * matrix.width + x] = if (matrix[x, y]) android.graphics.Color.BLACK else android.graphics.Color.WHITE
        }
    }
    Bitmap.createBitmap(matrix.width, matrix.height, Bitmap.Config.ARGB_8888).apply {
        setPixels(pixels, 0, matrix.width, 0, 0, matrix.width, matrix.height)
    }
}.getOrNull()

private fun connectionLabel(value: PlexServerConnectionChoice): String = buildList {
    add(if (value.isSecure) "Secure" else "HTTP")
    if (value.isLocal) add("Local")
    if (value.isRelay) add("Relay")
    if (value.isIpv6) add("IPv6")
}.joinToString(" · ")

private fun safeDisplayLocation(rawUrl: String): String = runCatching {
    MediaCenterUrlPolicy.safeDisplayLocation(URI(rawUrl))
}.getOrDefault("Verified Plex server")
