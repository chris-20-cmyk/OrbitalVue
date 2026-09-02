#if os(iOS) || os(tvOS)
import Foundation
import Observation

public enum PlaybackEnginePreference: String, CaseIterable, Identifiable, Sendable {
    case ksPlayer
    case avKit

    public var id: String { rawValue }

    public static var availableCases: [Self] {
        #if canImport(KSPlayer)
        [.ksPlayer, .avKit]
        #else
        [.avKit]
        #endif
    }

    public var label: String {
        switch self {
        case .ksPlayer: "KSPlayer (Metal)"
        case .avKit: "AVKit (Native)"
        }
    }

    public var detail: String {
        switch self {
        case .ksPlayer: "FFmpeg demuxing, Metal rendering, broader formats, custom headers, and advanced tuning."
        case .avKit: "Apple-native playback with system-managed HLS, AirPlay, Picture in Picture, HDR, and Atmos."
        }
    }
}

public enum PreferredAudioLanguage: String, CaseIterable, Identifiable, Sendable {
    case system
    case english = "en"
    case spanish = "es"
    case french = "fr"
    case german = "de"
    case italian = "it"
    case portuguese = "pt"
    case japanese = "ja"

    public var id: String { rawValue }
    public var code: String? { self == .system ? nil : rawValue }

    public var label: String {
        switch self {
        case .system: "System language"
        case .english: "English"
        case .spanish: "Spanish"
        case .french: "French"
        case .german: "German"
        case .italian: "Italian"
        case .portuguese: "Portuguese"
        case .japanese: "Japanese"
        }
    }
}

public enum PreferredSubtitleLanguage: String, CaseIterable, Identifiable, Sendable {
    case off
    case system
    case english = "en"
    case spanish = "es"
    case french = "fr"
    case german = "de"
    case italian = "it"
    case portuguese = "pt"
    case japanese = "ja"

    public var id: String { rawValue }
    public var code: String? {
        switch self {
        case .off, .system: nil
        default: rawValue
        }
    }

    public var label: String {
        switch self {
        case .off: "Off"
        case .system: "System language"
        case .english: "English"
        case .spanish: "Spanish"
        case .french: "French"
        case .german: "German"
        case .italian: "Italian"
        case .portuguese: "Portuguese"
        case .japanese: "Japanese"
        }
    }
}

public enum VideoAspectMode: String, CaseIterable, Identifiable, Sendable {
    case automatic
    case fill
    case stretch
    case ratio4x3
    case ratio5x4
    case ratio3x2
    case ratio14x9
    case ratio16x10
    case ratio16x9
    case ratio18x9
    case ratio21x9
    case ratio235x1
    case ratio239x1
    case ratio32x9

    public var id: String { rawValue }

    public var label: String {
        switch self {
        case .automatic: "Auto / Fit"
        case .fill: "Fill / Crop"
        case .stretch: "Stretch"
        case .ratio4x3: "4:3"
        case .ratio5x4: "5:4"
        case .ratio3x2: "3:2"
        case .ratio14x9: "14:9"
        case .ratio16x10: "16:10"
        case .ratio16x9: "16:9"
        case .ratio18x9: "18:9"
        case .ratio21x9: "21:9"
        case .ratio235x1: "2.35:1"
        case .ratio239x1: "2.39:1"
        case .ratio32x9: "32:9"
        }
    }

    public var forcedRatio: CGFloat? {
        switch self {
        case .ratio4x3: 4 / 3
        case .ratio5x4: 5 / 4
        case .ratio3x2: 3 / 2
        case .ratio14x9: 14 / 9
        case .ratio16x10: 16 / 10
        case .ratio16x9: 16 / 9
        case .ratio18x9: 18 / 9
        case .ratio21x9: 21 / 9
        case .ratio235x1: 2.35
        case .ratio239x1: 2.39
        case .ratio32x9: 32 / 9
        case .automatic, .fill, .stretch: nil
        }
    }
}

public enum BufferPreference: String, CaseIterable, Identifiable, Sendable {
    case automatic
    case responsive
    case stable

    public var id: String { rawValue }

    public var label: String {
        switch self {
        case .automatic: "Automatic"
        case .responsive: "Responsive"
        case .stable: "Stable"
        }
    }

    public var preferredForwardDuration: TimeInterval {
        switch self {
        case .automatic: 0
        case .responsive: 2
        case .stable: 8
        }
    }
}

@MainActor
@Observable
public final class OrbitalVueSettings {
    public var playbackEngine: PlaybackEnginePreference { didSet { persist() } }
    public var fallbackPlaybackEngine: Bool { didSet { persist() } }
    public var aspectMode: VideoAspectMode { didSet { persist() } }
    public var bufferPreference: BufferPreference { didSet { persist() } }
    public var allowsExternalPlayback: Bool { didSet { persist() } }
    public var allowsPictureInPicture: Bool { didSet { persist() } }
    public var autoPlaySelection: Bool { didSet { persist() } }
    public var channelZappingDelayMilliseconds: Int { didSet { persist() } }
    public var ksBufferDurationSeconds: Int { didSet { persist() } }
    public var ksAdaptiveFrameRate: Bool { didSet { persist() } }
    public var ksHardwareDecode: Bool { didSet { persist() } }
    public var ksAsynchronousDecompression: Bool { didSet { persist() } }
    public var ksAutomaticDeinterlacing: Bool { didSet { persist() } }
    public var preferredAudioLanguage: PreferredAudioLanguage { didSet { persist() } }
    public var preferredSubtitleLanguage: PreferredSubtitleLanguage { didSet { persist() } }
    public var ksSubtitleFontSize: Double { didSet { persist() } }

    private let defaults: UserDefaults

    public init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        let savedPlaybackEngine = PlaybackEnginePreference(
            rawValue: defaults.string(forKey: Keys.playbackEngine) ?? ""
        )
        #if canImport(KSPlayer)
        playbackEngine = savedPlaybackEngine ?? .ksPlayer
        #else
        playbackEngine = .avKit
        #endif
        fallbackPlaybackEngine = defaults.object(forKey: Keys.fallbackPlaybackEngine) as? Bool ?? true
        aspectMode = VideoAspectMode(rawValue: defaults.string(forKey: Keys.aspectMode) ?? "") ?? .automatic
        bufferPreference = BufferPreference(rawValue: defaults.string(forKey: Keys.bufferPreference) ?? "") ?? .automatic
        allowsExternalPlayback = defaults.object(forKey: Keys.externalPlayback) as? Bool ?? true
        allowsPictureInPicture = defaults.object(forKey: Keys.pictureInPicture) as? Bool ?? true
        autoPlaySelection = defaults.object(forKey: Keys.autoPlay) as? Bool ?? true
        channelZappingDelayMilliseconds = Self.clamp(
            defaults.object(forKey: Keys.zappingDelay) as? Int ?? 0,
            to: 0 ... 2_000
        )
        ksBufferDurationSeconds = Self.clamp(
            defaults.object(forKey: Keys.ksBufferDuration) as? Int ?? 3,
            to: 1 ... 30
        )
        ksAdaptiveFrameRate = defaults.object(forKey: Keys.ksAdaptiveFrameRate) as? Bool ?? true
        ksHardwareDecode = defaults.object(forKey: Keys.ksHardwareDecode) as? Bool ?? true
        ksAsynchronousDecompression = defaults.object(forKey: Keys.ksAsynchronousDecompression) as? Bool ?? false
        ksAutomaticDeinterlacing = defaults.object(forKey: Keys.ksAutomaticDeinterlacing) as? Bool ?? true
        preferredAudioLanguage = PreferredAudioLanguage(
            rawValue: defaults.string(forKey: Keys.preferredAudioLanguage) ?? ""
        ) ?? .system
        preferredSubtitleLanguage = PreferredSubtitleLanguage(
            rawValue: defaults.string(forKey: Keys.preferredSubtitleLanguage) ?? ""
        ) ?? .system
        #if os(tvOS)
        let defaultSubtitleSize = 58.0
        #else
        let defaultSubtitleSize = 22.0
        #endif
        ksSubtitleFontSize = min(
            max(defaults.object(forKey: Keys.ksSubtitleFontSize) as? Double ?? defaultSubtitleSize, 12),
            80
        )
    }

    private func persist() {
        defaults.set(playbackEngine.rawValue, forKey: Keys.playbackEngine)
        defaults.set(fallbackPlaybackEngine, forKey: Keys.fallbackPlaybackEngine)
        defaults.set(aspectMode.rawValue, forKey: Keys.aspectMode)
        defaults.set(bufferPreference.rawValue, forKey: Keys.bufferPreference)
        defaults.set(allowsExternalPlayback, forKey: Keys.externalPlayback)
        defaults.set(allowsPictureInPicture, forKey: Keys.pictureInPicture)
        defaults.set(autoPlaySelection, forKey: Keys.autoPlay)
        defaults.set(channelZappingDelayMilliseconds, forKey: Keys.zappingDelay)
        defaults.set(ksBufferDurationSeconds, forKey: Keys.ksBufferDuration)
        defaults.set(ksAdaptiveFrameRate, forKey: Keys.ksAdaptiveFrameRate)
        defaults.set(ksHardwareDecode, forKey: Keys.ksHardwareDecode)
        defaults.set(ksAsynchronousDecompression, forKey: Keys.ksAsynchronousDecompression)
        defaults.set(ksAutomaticDeinterlacing, forKey: Keys.ksAutomaticDeinterlacing)
        defaults.set(preferredAudioLanguage.rawValue, forKey: Keys.preferredAudioLanguage)
        defaults.set(preferredSubtitleLanguage.rawValue, forKey: Keys.preferredSubtitleLanguage)
        defaults.set(ksSubtitleFontSize, forKey: Keys.ksSubtitleFontSize)
    }

    private static func clamp(_ value: Int, to range: ClosedRange<Int>) -> Int {
        min(max(value, range.lowerBound), range.upperBound)
    }

    private enum Keys {
        static let playbackEngine = "apple.playback-engine"
        static let fallbackPlaybackEngine = "apple.fallback-playback-engine"
        static let aspectMode = "apple.aspect-mode"
        static let bufferPreference = "apple.buffer-preference"
        static let externalPlayback = "apple.external-playback"
        static let pictureInPicture = "apple.picture-in-picture"
        static let autoPlay = "apple.auto-play"
        static let zappingDelay = "apple.channel-zapping-delay-ms"
        static let ksBufferDuration = "apple.ksplayer.buffer-duration-seconds"
        static let ksAdaptiveFrameRate = "apple.ksplayer.adaptive-frame-rate"
        static let ksHardwareDecode = "apple.ksplayer.hardware-decode"
        static let ksAsynchronousDecompression = "apple.ksplayer.asynchronous-decompression"
        static let ksAutomaticDeinterlacing = "apple.ksplayer.automatic-deinterlacing"
        static let preferredAudioLanguage = "apple.preferred-audio-language"
        static let preferredSubtitleLanguage = "apple.preferred-subtitle-language"
        static let ksSubtitleFontSize = "apple.ksplayer.subtitle-font-size"
    }
}
#endif
