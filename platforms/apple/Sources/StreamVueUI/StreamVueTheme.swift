#if os(iOS) || os(tvOS)
import Observation
import SwiftUI

@MainActor
@Observable
public final class StreamVueTheme {
    public let background = Color(red: 0.018, green: 0.032, blue: 0.055)
    public let backgroundRaised = Color(red: 0.035, green: 0.059, blue: 0.092)
    public let surface = Color(red: 0.047, green: 0.076, blue: 0.115)
    public let surfaceRaised = Color(red: 0.067, green: 0.104, blue: 0.153)
    public let border = Color.white.opacity(0.11)
    public let text = Color(red: 0.94, green: 0.97, blue: 1)
    public let muted = Color(red: 0.56, green: 0.64, blue: 0.74)
    public let accent = Color(red: 0.17, green: 0.88, blue: 0.82)
    public let accentDim = Color(red: 0.08, green: 0.31, blue: 0.33)
    public let warning = Color(red: 1, green: 0.70, blue: 0.33)
    public let error = Color(red: 1, green: 0.33, blue: 0.43)

    public init() {}

    public var backgroundGradient: LinearGradient {
        LinearGradient(
            colors: [background, Color(red: 0.024, green: 0.080, blue: 0.112), background],
            startPoint: .topLeading,
            endPoint: .bottomTrailing
        )
    }
}
#endif
