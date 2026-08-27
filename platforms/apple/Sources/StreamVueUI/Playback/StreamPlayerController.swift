#if os(iOS) || os(tvOS)
import AVFoundation
import Foundation
@preconcurrency import KSPlayer
import Observation
import StreamVueCore

@MainActor
private enum KSPlayerCompatibility {
    /// KSPlayer 2.3 exposes engine selection and display-link cadence as process-wide
    /// settings. Keep the pre-concurrency dependency's mutable globals behind one
    /// main-actor boundary until KSPlayer offers per-instance equivalents.
    static func configureMetalEngine(adaptiveFrameRate: Bool) {
        KSOptions.firstPlayerType = KSMEPlayer.self
        KSOptions.preferredFrame = adaptiveFrameRate
    }
}

public enum PlaybackPhase: Equatable, Sendable {
    case idle
    case preparing
    case buffering
    case playing
    case paused
    case externalPlayback
    case failed(String)

    public var label: String {
        switch self {
        case .idle: "Idle"
        case .preparing: "Preparing"
        case .buffering: "Buffering"
        case .playing: "Playing"
        case .paused: "Paused"
        case .externalPlayback: "AirPlay"
        case .failed: "Playback error"
        }
    }

    public var isBuffering: Bool {
        self == .preparing || self == .buffering
    }
}

public struct PlaybackTelemetry: Equatable, Sendable {
    public var observedBitrate: Double?
    public var indicatedBitrate: Double?
    public var stallCount = 0
    public var droppedFrames = 0
    public var startupMilliseconds: Int?

    public init() {}
}

@MainActor
@Observable
public final class StreamPlayerController {
    public let player: AVPlayer
    public private(set) var activeEngine: PlaybackEnginePreference = .avKit
    public private(set) var channel: CatalogChannel?
    public private(set) var phase: PlaybackPhase = .idle
    public private(set) var telemetry = PlaybackTelemetry()
    public private(set) var isExternalPlaybackActive = false
    public private(set) var reasonForWaiting: String?
    public private(set) var ksLayer: KSPlayerLayer?
    public private(set) var surfaceOwner: StreamPlayerSurfaceRole = .inline
    public private(set) var surfaceGeneration = 0
    let ksVideoView = StreamVueKSVideoView()

    private var settings: StreamVueSettings?
    private var playerObservations: [NSKeyValueObservation] = []
    private var itemObservations: [NSKeyValueObservation] = []
    private var notificationTokens: [NSObjectProtocol] = []
    private var playbackRequested = false
    private var startupBeganAt: ContinuousClock.Instant?
    private var didFallbackForCurrentChannel = false

    public init() {
        player = AVPlayer()
        player.automaticallyWaitsToMinimizeStalling = true
        player.actionAtItemEnd = .pause
        installPlayerObservations()
        ksVideoView.onState = { [weak self] layer, state in
            self?.handleKSState(layer: layer, state: state)
        }
        ksVideoView.onTime = { [weak self] layer, currentTime, totalTime in
            self?.handleKSTime(layer: layer, currentTime: currentTime, totalTime: totalTime)
        }
        ksVideoView.onFinish = { [weak self] layer, error in
            self?.handleKSFinish(layer: layer, error: error)
        }
        ksVideoView.onBuffer = { [weak self] layer, count, consumeTime in
            self?.handleKSBuffer(layer: layer, count: count, consumeTime: consumeTime)
        }
    }

    public func configure(settings: StreamVueSettings) {
        self.settings = settings
        player.allowsExternalPlayback = settings.allowsExternalPlayback
        player.currentItem?.preferredForwardBufferDuration = settings.bufferPreference.preferredForwardDuration
        ksLayer?.player.allowsExternalPlayback = settings.allowsExternalPlayback
        ksLayer?.options.canStartPictureInPictureAutomaticallyFromInline = settings.allowsPictureInPicture
        ksVideoView.allowsStreamVuePictureInPicture = settings.allowsPictureInPicture
        ksVideoView.preferredSubtitleLanguage = settings.preferredSubtitleLanguage
        ksVideoView.streamVueSubtitleFontSize = CGFloat(settings.ksSubtitleFontSize)
        ksVideoView.refreshStreamVueSubtitleStyle()
        applyKSAspectMode(settings.aspectMode)
    }

    public func tune(to channel: CatalogChannel, settings: StreamVueSettings) {
        configure(settings: settings)
        stop(clearChannel: false)
        self.channel = channel
        telemetry = PlaybackTelemetry()
        reasonForWaiting = nil
        didFallbackForCurrentChannel = false

        switch settings.playbackEngine {
        case .ksPlayer:
            prepareKSPlayer(channel: channel, settings: settings)
        case .avKit:
            prepareNativePlayer(channel: channel, settings: settings)
        }
    }

    private func prepareNativePlayer(
        channel: CatalogChannel,
        settings: StreamVueSettings,
        autoPlayOverride: Bool? = nil
    ) {
        activeEngine = .avKit

        guard let url = URL(string: channel.stream.uri),
              let scheme = url.scheme?.lowercased(),
              ["http", "https", "file"].contains(scheme) else {
            handleNativeFailure(
                "This stream format is not supported by native AVPlayer. HLS and compatible HTTP media are supported."
            )
            return
        }

        let unsupportedHeaders = channel.stream.requestHeaders.keys.filter { key in
            !key.caseInsensitiveEquals("User-Agent") && !key.caseInsensitiveEquals("Cookie")
        }
        if !unsupportedHeaders.isEmpty {
            handleNativeFailure(
                "This channel requires request headers that Apple’s native player cannot safely apply. Referer and Authorization protected streams need KSPlayer or a provider-compatible URL."
            )
            return
        }

        var options: [String: Any] = [:]
        if let userAgent = header(named: "User-Agent", in: channel.stream.requestHeaders) {
            options[AVURLAssetHTTPUserAgentKey] = userAgent
        }
        if let cookieHeader = header(named: "Cookie", in: channel.stream.requestHeaders) {
            let cookies = HTTPCookie.cookies(
                withResponseHeaderFields: ["Set-Cookie": cookieHeader],
                for: url
            )
            if !cookies.isEmpty { options[AVURLAssetHTTPCookiesKey] = cookies }
        }

        let asset = AVURLAsset(url: url, options: options)
        let item = AVPlayerItem(asset: asset)
        item.preferredForwardBufferDuration = settings.bufferPreference.preferredForwardDuration
        installItemObservations(item)
        player.replaceCurrentItem(with: item)
        player.allowsExternalPlayback = settings.allowsExternalPlayback
        playbackRequested = autoPlayOverride ?? settings.autoPlaySelection
        phase = playbackRequested ? .preparing : .paused
        startupBeganAt = playbackRequested ? .now : nil
        if playbackRequested {
            activateAudioSession()
            player.play()
        }
    }

    private func prepareKSPlayer(channel: CatalogChannel, settings: StreamVueSettings) {
        activeEngine = .ksPlayer
        guard let url = URL(string: channel.stream.uri),
              let scheme = url.scheme?.lowercased(),
              ["http", "https", "file", "rtsp", "rtmp", "udp"].contains(scheme) else {
            fail("This stream format is not supported by the KSPlayer engine.")
            return
        }

        KSPlayerCompatibility.configureMetalEngine(
            adaptiveFrameRate: settings.ksAdaptiveFrameRate
        )

        let options = StreamVueKSOptions(
            adaptiveFrameRate: settings.ksAdaptiveFrameRate,
            preferredAudioLanguage: settings.preferredAudioLanguage
        )
        options.preferredForwardBufferDuration = Double(settings.ksBufferDurationSeconds)
        options.maxBufferDuration = min(
            max(Double(settings.ksBufferDurationSeconds) * 2, 12),
            30
        )
        options.hardwareDecode = settings.ksHardwareDecode
        options.asynchronousDecompression = settings.ksAsynchronousDecompression
        options.autoDeInterlace = settings.ksAutomaticDeinterlacing
        options.videoAdaptable = true
        options.canStartPictureInPictureAutomaticallyFromInline = settings.allowsPictureInPicture
        options.autoSelectEmbedSubtitle = settings.preferredSubtitleLanguage != .off
        if let userAgent = header(named: "User-Agent", in: channel.stream.requestHeaders) {
            options.userAgent = userAgent
        }
        if let referer = header(named: "Referer", in: channel.stream.requestHeaders) {
            options.referer = referer
        }
        let additionalHeaders = channel.stream.requestHeaders.filter { key, _ in
            !key.caseInsensitiveEquals("User-Agent") && !key.caseInsensitiveEquals("Referer")
        }
        if !additionalHeaders.isEmpty {
            options.appendHeader(additionalHeaders)
        }

        playbackRequested = settings.autoPlaySelection
        phase = playbackRequested ? .preparing : .paused
        startupBeganAt = playbackRequested ? .now : nil
        let layer = KSPlayerLayer(
            url: url,
            isAutoPlay: false,
            options: options,
            delegate: nil
        )
        layer.player.allowsExternalPlayback = settings.allowsExternalPlayback
        ksLayer = layer
        ksVideoView.titleLabel.text = channel.name
        ksVideoView.srtControl.url = url
        ksVideoView.preferredSubtitleLanguage = settings.preferredSubtitleLanguage
        ksVideoView.allowsStreamVuePictureInPicture = settings.allowsPictureInPicture
        ksVideoView.playerLayer = layer
        ksVideoView.refreshStreamVueSubtitleStyle()
        applyKSAspectMode(settings.aspectMode)
        surfaceGeneration += 1
        if playbackRequested {
            activateAudioSession()
            ksVideoView.play()
        }
    }

    public func play() {
        playbackRequested = true
        activateAudioSession()
        switch activeEngine {
        case .ksPlayer:
            guard let ksLayer else { return }
            if !ksLayer.player.isPlaying {
                phase = .preparing
                startupBeganAt = .now
            }
            ksVideoView.play()
        case .avKit:
            guard player.currentItem != nil else { return }
            if player.timeControlStatus != .playing {
                phase = .preparing
                startupBeganAt = .now
            }
            player.play()
            updatePhase()
        }
    }

    public func pause() {
        playbackRequested = false
        switch activeEngine {
        case .ksPlayer:
            ksVideoView.pause()
        case .avKit:
            player.pause()
            updatePhase()
        }
    }

    public func retry() {
        guard let channel, let settings else { return }
        tune(to: channel, settings: settings)
    }

    public func stop(clearChannel: Bool = true) {
        playbackRequested = false
        player.pause()
        player.replaceCurrentItem(with: nil)
        clearItemObservations()
        ksLayer?.delegate = nil
        ksVideoView.playerLayer = nil
        ksLayer?.stop()
        ksLayer = nil
        surfaceGeneration += 1
        if clearChannel { channel = nil }
        phase = .idle
        isExternalPlaybackActive = false
        reasonForWaiting = nil
    }

    public func claimSurface(_ role: StreamPlayerSurfaceRole) {
        guard surfaceOwner != role else { return }
        surfaceOwner = role
        surfaceGeneration += 1
    }

    public func releaseSurface(_ role: StreamPlayerSurfaceRole) {
        guard surfaceOwner == role, role == .fullscreen else { return }
        surfaceOwner = .inline
        surfaceGeneration += 1
    }

    public func useAVKitForExternalPlayback() {
        guard activeEngine == .ksPlayer, let channel, let settings else { return }
        let shouldContinuePlaying = playbackRequested
        didFallbackForCurrentChannel = true
        ksLayer?.delegate = nil
        ksVideoView.playerLayer = nil
        ksLayer?.stop()
        ksLayer = nil
        surfaceGeneration += 1
        prepareNativePlayer(
            channel: channel,
            settings: settings,
            autoPlayOverride: shouldContinuePlaying
        )
    }

    public func applyKSAspectMode(_ mode: VideoAspectMode) {
        guard let ksLayer else { return }
        switch mode {
        case .fill:
            ksLayer.player.contentMode = .scaleAspectFill
        case .stretch:
            ksLayer.player.contentMode = .scaleToFill
        default:
            ksLayer.player.contentMode = .scaleAspectFit
        }
    }

    private func installPlayerObservations() {
        playerObservations = [
            player.observe(\.timeControlStatus, options: [.initial, .new]) { [weak self] _, _ in
                Task { @MainActor [weak self] in self?.updatePhase() }
            },
            player.observe(\.isExternalPlaybackActive, options: [.initial, .new]) { [weak self] player, _ in
                Task { @MainActor [weak self] in
                    self?.isExternalPlaybackActive = player.isExternalPlaybackActive
                    self?.updatePhase()
                }
            }
        ]
    }

    private func installItemObservations(_ item: AVPlayerItem) {
        clearItemObservations()
        itemObservations = [
            item.observe(\.status, options: [.initial, .new]) { [weak self] _, _ in
                Task { @MainActor [weak self] in self?.updatePhase() }
            },
            item.observe(\.isPlaybackLikelyToKeepUp, options: [.new]) { [weak self] _, _ in
                Task { @MainActor [weak self] in self?.updatePhase() }
            },
            item.observe(\.isPlaybackBufferEmpty, options: [.new]) { [weak self] _, _ in
                Task { @MainActor [weak self] in self?.updatePhase() }
            }
        ]
        notificationTokens = [
            NotificationCenter.default.addObserver(
                forName: .AVPlayerItemPlaybackStalled,
                object: item,
                queue: .main
            ) { [weak self] _ in
                Task { @MainActor [weak self] in
                    self?.telemetry.stallCount += 1
                    self?.phase = .buffering
                    self?.refreshTelemetry()
                }
            },
            NotificationCenter.default.addObserver(
                forName: .AVPlayerItemNewAccessLogEntry,
                object: item,
                queue: .main
            ) { [weak self] _ in
                Task { @MainActor [weak self] in self?.refreshTelemetry() }
            }
        ]
    }

    private func clearItemObservations() {
        itemObservations.removeAll()
        notificationTokens.forEach(NotificationCenter.default.removeObserver)
        notificationTokens.removeAll()
    }

    private func updatePhase() {
        guard let item = player.currentItem else {
            if channel == nil { phase = .idle }
            return
        }
        if item.status == .failed {
            handleNativeFailure("Apple’s native player could not open this channel.")
            return
        }
        reasonForWaiting = player.reasonForWaitingToPlay?.rawValue
        switch player.timeControlStatus {
        case .playing:
            phase = player.isExternalPlaybackActive ? .externalPlayback : .playing
            if telemetry.startupMilliseconds == nil, let startupBeganAt {
                let duration = startupBeganAt.duration(to: .now)
                telemetry.startupMilliseconds = Int(duration.components.seconds * 1_000) +
                    Int(duration.components.attoseconds / 1_000_000_000_000_000)
            }
        case .waitingToPlayAtSpecifiedRate:
            phase = playbackRequested ? .buffering : .paused
        case .paused:
            phase = item.status == .unknown ? .preparing : playbackRequested ? .preparing : .paused
        @unknown default:
            phase = .preparing
        }
        refreshTelemetry()
    }

    private func refreshTelemetry() {
        guard let event = player.currentItem?.accessLog()?.events.last else { return }
        telemetry.observedBitrate = event.observedBitrate > 0 ? event.observedBitrate : nil
        telemetry.indicatedBitrate = event.indicatedBitrate > 0 ? event.indicatedBitrate : nil
        telemetry.stallCount = max(telemetry.stallCount, event.numberOfStalls)
        telemetry.droppedFrames = event.numberOfDroppedVideoFrames
    }

    private func fail(_ message: String) {
        playbackRequested = false
        player.pause()
        phase = .failed(message)
        reasonForWaiting = nil
    }

    private func handleKSFailure() {
        guard activeEngine == .ksPlayer else { return }
        if let channel,
           let settings,
           settings.fallbackPlaybackEngine,
           !didFallbackForCurrentChannel {
            didFallbackForCurrentChannel = true
            ksLayer?.delegate = nil
            ksVideoView.playerLayer = nil
            ksLayer?.stop()
            ksLayer = nil
            surfaceGeneration += 1
            prepareNativePlayer(
                channel: channel,
                settings: settings,
                autoPlayOverride: playbackRequested
            )
        } else {
            fail("KSPlayer could not open this stream, and no compatible fallback engine succeeded.")
        }
    }

    private func handleNativeFailure(_ message: String) {
        guard activeEngine == .avKit else { return }
        if let channel,
           let settings,
           settings.fallbackPlaybackEngine,
           !didFallbackForCurrentChannel {
            didFallbackForCurrentChannel = true
            player.pause()
            player.replaceCurrentItem(with: nil)
            clearItemObservations()
            prepareKSPlayer(channel: channel, settings: settings)
        } else {
            fail(message)
        }
    }

    private func recordStartupIfNeeded() {
        guard telemetry.startupMilliseconds == nil, let startupBeganAt else { return }
        let duration = startupBeganAt.duration(to: .now)
        telemetry.startupMilliseconds = Int(duration.components.seconds * 1_000) +
            Int(duration.components.attoseconds / 1_000_000_000_000_000)
    }

    private func handleKSState(layer: KSPlayerLayer, state: KSPlayerState) {
        guard layer === ksLayer, activeEngine == .ksPlayer else { return }
        switch state {
        case .initialized:
            phase = playbackRequested ? .preparing : .paused
        case .preparing:
            phase = playbackRequested ? .preparing : .paused
        case .readyToPlay:
            phase = playbackRequested ? .preparing : .paused
            if playbackRequested { recordStartupIfNeeded() }
        case .buffering:
            phase = .buffering
        case .bufferFinished:
            phase = playbackRequested ? .playing : .paused
            if playbackRequested { recordStartupIfNeeded() }
        case .paused, .playedToTheEnd:
            phase = .paused
        case .error:
            handleKSFailure()
        }
        refreshKSTelemetry(layer)
    }

    private func handleKSTime(
        layer: KSPlayerLayer,
        currentTime: TimeInterval,
        totalTime: TimeInterval
    ) {
        _ = currentTime
        _ = totalTime
        guard layer === ksLayer else { return }
        refreshKSTelemetry(layer)
    }

    private func handleKSFinish(layer: KSPlayerLayer, error: Error?) {
        guard layer === ksLayer else { return }
        if error != nil {
            handleKSFailure()
        } else {
            phase = .paused
        }
    }

    private func handleKSBuffer(
        layer: KSPlayerLayer,
        count: Int,
        consumeTime: TimeInterval
    ) {
        _ = consumeTime
        guard layer === ksLayer else { return }
        if count > 0 { telemetry.stallCount = max(telemetry.stallCount, count) }
        refreshKSTelemetry(layer)
    }

    private func refreshKSTelemetry(_ layer: KSPlayerLayer) {
        if let dynamicInfo = layer.player.dynamicInfo {
            if dynamicInfo.videoBitrate > 0 {
                telemetry.observedBitrate = Double(dynamicInfo.videoBitrate)
            }
            telemetry.droppedFrames = Int(dynamicInfo.droppedVideoFrameCount)
        }
        if let videoTrack = layer.player.tracks(mediaType: .video).first(where: { $0.isEnabled }),
           videoTrack.bitRate > 0 {
            telemetry.indicatedBitrate = Double(videoTrack.bitRate)
            if telemetry.observedBitrate == nil {
                telemetry.observedBitrate = Double(videoTrack.bitRate)
            }
        }
    }

    private func activateAudioSession() {
        do {
            let session = AVAudioSession.sharedInstance()
            try session.setCategory(.playback, mode: .moviePlayback)
            try session.setActive(true)
        } catch {
            // Playback can still proceed; AVPlayer will surface a real media failure if one occurs.
        }
    }

    private func header(named name: String, in values: [String: String]) -> String? {
        values.first { $0.key.caseInsensitiveEquals(name) }?.value
    }
}

private extension String {
    func caseInsensitiveEquals(_ other: String) -> Bool {
        compare(other, options: .caseInsensitive) == .orderedSame
    }
}
#endif
