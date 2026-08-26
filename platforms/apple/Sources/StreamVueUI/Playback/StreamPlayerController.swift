#if os(iOS) || os(tvOS)
import AVFoundation
import Foundation
import Observation
import StreamVueCore

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
    public private(set) var channel: CatalogChannel?
    public private(set) var phase: PlaybackPhase = .idle
    public private(set) var telemetry = PlaybackTelemetry()
    public private(set) var isExternalPlaybackActive = false
    public private(set) var reasonForWaiting: String?

    private var settings: StreamVueSettings?
    private var playerObservations: [NSKeyValueObservation] = []
    private var itemObservations: [NSKeyValueObservation] = []
    private var notificationTokens: [NSObjectProtocol] = []
    private var playbackRequested = false
    private var startupBeganAt: ContinuousClock.Instant?

    public init() {
        player = AVPlayer()
        player.automaticallyWaitsToMinimizeStalling = true
        player.actionAtItemEnd = .pause
        installPlayerObservations()
    }

    public func configure(settings: StreamVueSettings) {
        self.settings = settings
        player.allowsExternalPlayback = settings.allowsExternalPlayback
        player.currentItem?.preferredForwardBufferDuration = settings.bufferPreference.preferredForwardDuration
    }

    public func tune(to channel: CatalogChannel, settings: StreamVueSettings) {
        configure(settings: settings)
        stop(clearChannel: false)
        self.channel = channel
        telemetry = PlaybackTelemetry()
        reasonForWaiting = nil

        guard let url = URL(string: channel.stream.uri),
              let scheme = url.scheme?.lowercased(),
              ["http", "https", "file"].contains(scheme) else {
            fail("This stream format is not supported by native AVPlayer. HLS and compatible HTTP media are supported.")
            return
        }

        let unsupportedHeaders = channel.stream.requestHeaders.keys.filter { key in
            !key.caseInsensitiveEquals("User-Agent") && !key.caseInsensitiveEquals("Cookie")
        }
        if !unsupportedHeaders.isEmpty {
            fail("This channel requires request headers that Apple’s native player cannot safely apply. Referer and Authorization protected streams need a provider-compatible URL or cookie.")
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
        playbackRequested = settings.autoPlaySelection
        phase = .preparing
        startupBeganAt = ContinuousClock.now
        if playbackRequested {
            activateAudioSession()
            player.play()
        }
    }

    public func play() {
        guard player.currentItem != nil else { return }
        playbackRequested = true
        activateAudioSession()
        player.play()
        updatePhase()
    }

    public func pause() {
        playbackRequested = false
        player.pause()
        updatePhase()
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
        if clearChannel { channel = nil }
        phase = .idle
        reasonForWaiting = nil
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
            fail("Apple’s native player could not open this channel.")
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
