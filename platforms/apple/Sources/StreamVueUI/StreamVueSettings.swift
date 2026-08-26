#if os(iOS) || os(tvOS)
import Foundation
import Observation

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
public final class StreamVueSettings {
    public var aspectMode: VideoAspectMode { didSet { persist() } }
    public var bufferPreference: BufferPreference { didSet { persist() } }
    public var allowsExternalPlayback: Bool { didSet { persist() } }
    public var allowsPictureInPicture: Bool { didSet { persist() } }
    public var autoPlaySelection: Bool { didSet { persist() } }

    private let defaults: UserDefaults

    public init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        aspectMode = VideoAspectMode(rawValue: defaults.string(forKey: Keys.aspectMode) ?? "") ?? .automatic
        bufferPreference = BufferPreference(rawValue: defaults.string(forKey: Keys.bufferPreference) ?? "") ?? .automatic
        allowsExternalPlayback = defaults.object(forKey: Keys.externalPlayback) as? Bool ?? true
        allowsPictureInPicture = defaults.object(forKey: Keys.pictureInPicture) as? Bool ?? true
        autoPlaySelection = defaults.object(forKey: Keys.autoPlay) as? Bool ?? true
    }

    private func persist() {
        defaults.set(aspectMode.rawValue, forKey: Keys.aspectMode)
        defaults.set(bufferPreference.rawValue, forKey: Keys.bufferPreference)
        defaults.set(allowsExternalPlayback, forKey: Keys.externalPlayback)
        defaults.set(allowsPictureInPicture, forKey: Keys.pictureInPicture)
        defaults.set(autoPlaySelection, forKey: Keys.autoPlay)
    }

    private enum Keys {
        static let aspectMode = "apple.aspect-mode"
        static let bufferPreference = "apple.buffer-preference"
        static let externalPlayback = "apple.external-playback"
        static let pictureInPicture = "apple.picture-in-picture"
        static let autoPlay = "apple.auto-play"
    }
}
#endif
