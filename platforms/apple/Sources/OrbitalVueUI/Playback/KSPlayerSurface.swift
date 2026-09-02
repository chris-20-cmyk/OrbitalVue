#if os(iOS) || os(tvOS)
#if canImport(KSPlayer)
import CoreMedia
#endif
import Foundation
#if canImport(KSPlayer)
@preconcurrency import KSPlayer
#endif
import SwiftUI
import UIKit

#if canImport(KSPlayer)
final class OrbitalVueKSOptions: KSOptions {
    private let adaptiveFrameRate: Bool
    private let preferredAudioLanguage: PreferredAudioLanguage

    init(
        adaptiveFrameRate: Bool,
        preferredAudioLanguage: PreferredAudioLanguage
    ) {
        self.adaptiveFrameRate = adaptiveFrameRate
        self.preferredAudioLanguage = preferredAudioLanguage
        super.init()
    }

    override func wantedAudio(tracks: [MediaPlayerTrack]) -> Int? {
        let languageCode = preferredAudioLanguage.code
            ?? Locale.preferredLanguages.first?.split(separator: "-").first.map(String.init)
        guard let languageCode else { return nil }
        return tracks.firstIndex { track in
            OrbitalVueKSVideoView.matches(track.languageCode, languageCode)
        }
    }

    @MainActor
    override func updateVideo(
        refreshRate: Float,
        isDovi: Bool,
        formatDescription: CMFormatDescription?
    ) {
        guard adaptiveFrameRate else { return }
        super.updateVideo(
            refreshRate: refreshRate,
            isDovi: isDovi,
            formatDescription: formatDescription
        )
    }
}

@MainActor
final class OrbitalVueKSVideoView: VideoPlayerView {
    var preferredSubtitleLanguage: PreferredSubtitleLanguage = .system
    var orbitalVueSubtitleFontSize: CGFloat = 16 {
        didSet { applyOrbitalVueSubtitleStyle() }
    }
    var allowsOrbitalVuePictureInPicture = true {
        didSet { toolBar.pipButton.isHidden = !allowsOrbitalVuePictureInPicture }
    }
    var onState: ((KSPlayerLayer, KSPlayerState) -> Void)?
    var onTime: ((KSPlayerLayer, TimeInterval, TimeInterval) -> Void)?
    var onFinish: ((KSPlayerLayer, Error?) -> Void)?
    var onBuffer: ((KSPlayerLayer, Int, TimeInterval) -> Void)?

    override func player(layer: KSPlayerLayer, state: KSPlayerState) {
        super.player(layer: layer, state: state)
        toolBar.pipButton.isHidden = !allowsOrbitalVuePictureInPicture
        onState?(layer, state)
        guard state == .readyToPlay else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.1) { [weak self, weak layer] in
            guard let self, let layer, self.playerLayer === layer else { return }
            self.selectPreferredSubtitle()
        }
    }

    override func player(
        layer: KSPlayerLayer,
        currentTime: TimeInterval,
        totalTime: TimeInterval
    ) {
        super.player(layer: layer, currentTime: currentTime, totalTime: totalTime)
        applyOrbitalVueSubtitleStyle()
        onTime?(layer, currentTime, totalTime)
    }

    override func player(layer: KSPlayerLayer, finish error: Error?) {
        super.player(layer: layer, finish: error)
        onFinish?(layer, error)
    }

    override func player(
        layer: KSPlayerLayer,
        bufferedCount: Int,
        consumeTime: TimeInterval
    ) {
        super.player(layer: layer, bufferedCount: bufferedCount, consumeTime: consumeTime)
        onBuffer?(layer, bufferedCount, consumeTime)
    }

    func refreshOrbitalVueSubtitleStyle() {
        applyOrbitalVueSubtitleStyle()
        selectPreferredSubtitle()
    }

    private func applyOrbitalVueSubtitleStyle() {
        if subtitleLabel.font.pointSize != orbitalVueSubtitleFontSize {
            subtitleLabel.font = .systemFont(ofSize: orbitalVueSubtitleFontSize)
        }
        guard let currentText = subtitleLabel.attributedText,
              currentText.length > 0 else {
            return
        }
        if let currentFont = currentText.attribute(.font, at: 0, effectiveRange: nil) as? UIFont,
           currentFont.pointSize == orbitalVueSubtitleFontSize {
            return
        }
        guard let attributedText = currentText.mutableCopy() as? NSMutableAttributedString else { return }
        attributedText.addAttribute(
            .font,
            value: UIFont.systemFont(ofSize: orbitalVueSubtitleFontSize),
            range: NSRange(location: 0, length: attributedText.length)
        )
        subtitleLabel.attributedText = attributedText
    }

    private func selectPreferredSubtitle() {
        if preferredSubtitleLanguage == .off {
            srtControl.selectedSubtitleInfo = nil
            return
        }
        let desiredCode = preferredSubtitleLanguage.code
            ?? Locale.preferredLanguages.first?.split(separator: "-").first.map(String.init)
        guard let desiredCode else { return }
        let selected = srtControl.subtitleInfos.first { info in
            if let track = info as? any MediaPlayerTrack {
                return Self.matches(track.languageCode, desiredCode)
            }
            return info.name.localizedCaseInsensitiveContains(desiredCode)
        }
        if let selected { srtControl.selectedSubtitleInfo = selected }
    }

    nonisolated static func matches(_ candidate: String?, _ desired: String) -> Bool {
        guard let candidate else { return false }
        let normalizedCandidate = candidate.lowercased().split(separator: "-").first.map(String.init)
        let normalizedDesired = desired.lowercased().split(separator: "-").first.map(String.init)
        guard let normalizedCandidate, let normalizedDesired else { return false }
        let aliases: [String: Set<String>] = [
            "en": ["en", "eng"],
            "es": ["es", "spa"],
            "fr": ["fr", "fra", "fre"],
            "de": ["de", "deu", "ger"],
            "it": ["it", "ita"],
            "pt": ["pt", "por"],
            "ja": ["ja", "jpn"]
        ]
        let desiredAliases = aliases[normalizedDesired] ?? [normalizedDesired]
        return desiredAliases.contains(normalizedCandidate)
    }
}
#endif

public enum StreamPlayerSurfaceRole: Equatable, Sendable {
    case inline
    case fullscreen
}

public struct StreamPlayerSurface: View {
    public let controller: StreamPlayerController
    public let aspectMode: VideoAspectMode
    public let allowsPictureInPicture: Bool
    public let role: StreamPlayerSurfaceRole

    public init(
        controller: StreamPlayerController,
        aspectMode: VideoAspectMode,
        allowsPictureInPicture: Bool,
        role: StreamPlayerSurfaceRole = .inline
    ) {
        self.controller = controller
        self.aspectMode = aspectMode
        self.allowsPictureInPicture = allowsPictureInPicture
        self.role = role
    }

    public var body: some View {
        Group {
            switch controller.activeEngine {
            case .ksPlayer:
                #if canImport(KSPlayer)
                if controller.ksLayer != nil, controller.surfaceOwner == role {
                    KSPlayerMetalSurface(
                        playerView: controller.ksVideoView,
                        aspectMode: aspectMode,
                        generation: controller.surfaceGeneration
                    )
                } else {
                    Color.black
                }
                #else
                nativeSurface
                #endif
            case .avKit:
                nativeSurface
            }
        }
        .background(Color.black)
        .onAppear { controller.claimSurface(role) }
        .onDisappear { controller.releaseSurface(role) }
    }

    @ViewBuilder
    private var nativeSurface: some View {
        if controller.surfaceOwner == role {
            NativePlayerSurface(
                player: controller.player,
                aspectMode: aspectMode,
                allowsPictureInPicture: allowsPictureInPicture
            )
        } else {
            Color.black
        }
    }
}

#if canImport(KSPlayer)
private struct KSPlayerMetalSurface: View {
    let playerView: OrbitalVueKSVideoView
    let aspectMode: VideoAspectMode
    let generation: Int

    var body: some View {
        GeometryReader { proxy in
            let size = fittedSize(in: proxy.size)
            ZStack {
                Color.black
                KSPlayerContainer(playerView: playerView, generation: generation)
                    .frame(width: size.width, height: size.height)
                    .clipped()
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .background(Color.black)
    }

    private func fittedSize(in available: CGSize) -> CGSize {
        guard let ratio = aspectMode.forcedRatio,
              available.width > 0,
              available.height > 0 else {
            return available
        }
        let availableRatio = available.width / available.height
        if availableRatio > ratio {
            return CGSize(width: available.height * ratio, height: available.height)
        }
        return CGSize(width: available.width, height: available.width / ratio)
    }
}

private struct KSPlayerContainer: UIViewRepresentable {
    let playerView: OrbitalVueKSVideoView
    let generation: Int

    func makeUIView(context: Context) -> PlayerHostView {
        let host = PlayerHostView()
        host.attach(playerView)
        return host
    }

    func updateUIView(_ host: PlayerHostView, context: Context) {
        _ = generation
        host.attach(playerView)
    }

    static func dismantleUIView(_ host: PlayerHostView, coordinator: Void) {
        _ = coordinator
        host.detachIfOwned()
    }
}

private final class PlayerHostView: UIView {
    private weak var playbackView: UIView?

    func attach(_ view: UIView?) {
        guard let view else { return }
        playbackView = view
        if view.superview !== self {
            view.removeFromSuperview()
            addSubview(view)
        }
        view.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        view.frame = bounds
    }

    func detachIfOwned() {
        guard playbackView?.superview === self else { return }
        playbackView?.removeFromSuperview()
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        if playbackView?.superview === self {
            playbackView?.frame = bounds
        }
    }
}
#endif
#endif
