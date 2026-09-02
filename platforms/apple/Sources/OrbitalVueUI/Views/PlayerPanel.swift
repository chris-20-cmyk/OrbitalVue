#if os(iOS) || os(tvOS)
import Foundation
import OrbitalVueCore
import SwiftUI

struct PlayerPanel: View {
    let onFullscreen: (CatalogChannel) -> Void
    @Environment(OrbitalVueStore.self) private var store
    @Environment(OrbitalVueSettings.self) private var settings
    @Environment(StreamPlayerController.self) private var player
    @Environment(OrbitalVueTheme.self) private var theme

    var body: some View {
        VStack(spacing: 0) {
            if let channel = store.selectedChannel {
                video(channel)
                controls(channel)
                telemetry(channel)
            } else {
                emptyPlayer
            }
        }
        .background(theme.backgroundRaised)
        .clipShape(RoundedRectangle(cornerRadius: 20, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: 20, style: .continuous)
                .stroke(theme.border, lineWidth: 1)
        }
    }

    private func video(_ channel: CatalogChannel) -> some View {
        ZStack {
            StreamPlayerSurface(
                controller: player,
                aspectMode: settings.aspectMode,
                allowsPictureInPicture: settings.allowsPictureInPicture,
                role: .inline
            )
            .aspectRatio(16 / 9, contentMode: .fit)

            if player.phase.isBuffering {
                VStack(spacing: 10) {
                    ProgressView().controlSize(.large).tint(theme.accent)
                    Text(player.phase.label)
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(theme.muted)
                }
                .padding(18)
                .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
            }

            if case .failed(let message) = player.phase {
                VStack(spacing: 14) {
                    Image(systemName: "exclamationmark.triangle.fill")
                        .font(.title)
                        .foregroundStyle(theme.warning)
                    Text("Playback needs attention")
                        .font(.headline)
                    Text(message)
                        .font(.caption)
                        .foregroundStyle(theme.muted)
                        .multilineTextAlignment(.center)
                        .frame(maxWidth: 420)
                    Button("Try again") { player.retry() }
                        .buttonStyle(.borderedProminent)
                }
                .padding(22)
                .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
            }

            VStack {
                HStack {
                    Text(channel.kind.label)
                        .font(.caption2.weight(.black))
                        .padding(.horizontal, 9)
                        .padding(.vertical, 5)
                        .background(Color.black.opacity(0.58), in: Capsule())
                    Spacer()
                    if player.isExternalPlaybackActive {
                        Label("AIRPLAY", systemImage: "airplayvideo")
                            .font(.caption2.weight(.black))
                            .padding(.horizontal, 9)
                            .padding(.vertical, 5)
                            .background(theme.accent, in: Capsule())
                            .foregroundStyle(theme.background)
                    }
                }
                Spacer()
            }
            .padding(14)
            .allowsHitTesting(false)
        }
        .background(Color.black)
    }

    private func controls(_ channel: CatalogChannel) -> some View {
        HStack(spacing: 12) {
            Button {
                player.phase == .playing || player.phase == .externalPlayback ? player.pause() : player.play()
            } label: {
                Image(systemName: player.phase == .playing || player.phase == .externalPlayback ? "pause.fill" : "play.fill")
                    .frame(width: 24, height: 24)
            }
            .buttonStyle(.borderedProminent)
            .accessibilityLabel(player.phase == .playing || player.phase == .externalPlayback ? "Pause" : "Play")
            .accessibilityHint("Controls \(channel.name)")

            VStack(alignment: .leading, spacing: 2) {
                Text(channel.name)
                    .font(.headline)
                    .lineLimit(1)
                Text(channel.group)
                    .font(.caption)
                    .foregroundStyle(theme.muted)
                    .lineLimit(1)
            }
            Spacer(minLength: 6)

            Button { store.toggleFavorite(channel) } label: {
                Image(systemName: store.favorites.contains(channel.id) ? "star.fill" : "star")
                    .foregroundStyle(store.favorites.contains(channel.id) ? theme.warning : theme.text)
            }
            .accessibilityLabel(
                store.favorites.contains(channel.id)
                    ? "Remove \(channel.name) from favorites"
                    : "Add \(channel.name) to favorites"
            )

            Menu {
                ForEach(VideoAspectMode.allCases) { mode in
                    Button {
                        settings.aspectMode = mode
                    } label: {
                        if settings.aspectMode == mode {
                            Label(mode.label, systemImage: "checkmark")
                        } else {
                            Text(mode.label)
                        }
                    }
                }
            } label: {
                Label(settings.aspectMode.label, systemImage: "aspectratio")
                    .labelStyle(.iconOnly)
            }
            .accessibilityLabel("Aspect ratio, \(settings.aspectMode.label)")
            .accessibilityValue(settings.aspectMode.label)

            if settings.allowsExternalPlayback {
                if player.activeEngine == .ksPlayer {
                    Button { player.useAVKitForExternalPlayback() } label: {
                        Image(systemName: "airplayvideo")
                    }
                    .accessibilityLabel("Switch to AVKit for AirPlay")
                } else {
                    OrbitalVueRoutePicker()
                        .frame(width: 42, height: 42)
                        .accessibilityLabel("AirPlay")
                }
            }

            Button { onFullscreen(channel) } label: {
                Image(systemName: "arrow.up.left.and.arrow.down.right")
            }
            .accessibilityLabel("Full screen")
        }
        .buttonStyle(.bordered)
        .padding(14)
        .foregroundStyle(theme.text)
    }

    private func telemetry(_ channel: CatalogChannel) -> some View {
        HStack(spacing: 18) {
            Label(player.phase.label, systemImage: "dot.radiowaves.left.and.right")
                .foregroundStyle(phaseColor)
            metric("START", player.telemetry.startupMilliseconds.map { "\($0) ms" } ?? "—")
            metric("BITRATE", formatBitrate(player.telemetry.observedBitrate))
            metric("STALLS", "\(player.telemetry.stallCount)")
            metric("DROPPED", "\(player.telemetry.droppedFrames)")
            Spacer()
            Text(player.activeEngine == .ksPlayer ? "KS METAL" : "AVKIT")
                .font(.caption2.weight(.black))
                .foregroundStyle(theme.accent)
            Text(channel.stream.uri.lowercased().contains(".m3u8") ? "HLS" : "DIRECT")
                .font(.caption2.weight(.black))
                .foregroundStyle(theme.muted)
        }
        .font(.caption)
        .padding(.horizontal, 16)
        .padding(.bottom, 13)
        .foregroundStyle(theme.muted)
    }

    private func metric(_ label: String, _ value: String) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            Text(label).font(.caption2.weight(.bold)).tracking(0.8)
            Text(value).font(.caption2.weight(.semibold)).foregroundStyle(theme.text)
        }
    }

    private var emptyPlayer: some View {
        VStack(spacing: 18) {
            ZStack {
                Circle().fill(theme.accentDim).frame(width: 92, height: 92)
                Image(systemName: "play.fill")
                    .font(.system(size: 38, weight: .black))
                    .foregroundStyle(theme.accent)
                    .offset(x: 3)
            }
            Text("Ready when you are")
                .font(.title2.bold())
            Text("Select a channel to start premium Apple playback.")
                .foregroundStyle(theme.muted)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .aspectRatio(16 / 10, contentMode: .fit)
        .padding(30)
    }

    private func formatBitrate(_ value: Double?) -> String {
        guard let value, value > 0 else { return "—" }
        return value >= 1_000_000
            ? String(format: "%.1f Mbps", value / 1_000_000)
            : String(format: "%.0f Kbps", value / 1_000)
    }

    private var phaseColor: Color {
        if case .failed = player.phase { return theme.error }
        return theme.accent
    }
}

struct FullscreenPlayerView: View {
    let channel: CatalogChannel
    @Environment(\.dismiss) private var dismiss
    @Environment(OrbitalVueSettings.self) private var settings
    @Environment(StreamPlayerController.self) private var player
    @Environment(OrbitalVueTheme.self) private var theme

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()
            StreamPlayerSurface(
                controller: player,
                aspectMode: settings.aspectMode,
                allowsPictureInPicture: settings.allowsPictureInPicture,
                role: .fullscreen
            )
            .ignoresSafeArea()

            VStack {
                HStack(spacing: 12) {
                    Button { dismiss() } label: {
                        Label("Exit full screen", systemImage: "xmark")
                            .labelStyle(.iconOnly)
                            .frame(width: 30, height: 30)
                    }
                    .buttonStyle(.bordered)
                    .accessibilityLabel("Exit full screen")
                    VStack(alignment: .leading, spacing: 2) {
                        Text(channel.name).font(.headline)
                        Text(channel.group).font(.caption).foregroundStyle(theme.muted)
                    }
                    Spacer()
                    Menu {
                        ForEach(VideoAspectMode.allCases) { mode in
                            Button(mode.label) { settings.aspectMode = mode }
                        }
                    } label: {
                        Label(settings.aspectMode.label, systemImage: "aspectratio")
                    }
                    .buttonStyle(.bordered)
                    if settings.allowsExternalPlayback {
                        if player.activeEngine == .ksPlayer {
                            Button {
                                player.useAVKitForExternalPlayback()
                            } label: {
                                Label("Use AVKit for AirPlay", systemImage: "airplayvideo")
                            }
                            .buttonStyle(.bordered)
                        } else {
                            OrbitalVueRoutePicker().frame(width: 48, height: 48)
                        }
                    }
                }
                .padding(18)
                .background(.ultraThinMaterial)
                Spacer()
            }
        }
        .persistentSystemOverlays(.hidden)
        .onChange(of: settings.aspectMode) { _, mode in
            player.applyKSAspectMode(mode)
        }
    }
}
#endif
