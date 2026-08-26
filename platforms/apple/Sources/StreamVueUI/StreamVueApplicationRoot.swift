#if os(iOS) || os(tvOS)
import StreamVueCore
import SwiftUI

@MainActor
public struct StreamVueApplicationRoot: View {
    @State private var store: StreamVueStore
    @State private var settings: StreamVueSettings
    @State private var player: StreamPlayerController
    @State private var theme: StreamVueTheme

    public init() {
        _store = State(initialValue: StreamVueStore())
        _settings = State(initialValue: StreamVueSettings())
        _player = State(initialValue: StreamPlayerController())
        _theme = State(initialValue: StreamVueTheme())
    }

    public var body: some View {
        Group {
            #if os(tvOS)
            AppleTVRootView()
            #else
            MobileRootView()
            #endif
        }
        .environment(store)
        .environment(settings)
        .environment(player)
        .environment(theme)
        .tint(theme.accent)
        .preferredColorScheme(.dark)
        .task { await store.start() }
        .onChange(of: store.selectedChannel?.id) { _, _ in
            if let channel = store.selectedChannel {
                player.tune(to: channel, settings: settings)
            } else {
                player.stop()
            }
        }
        .onChange(of: settings.bufferPreference) { _, _ in player.configure(settings: settings) }
        .onChange(of: settings.allowsExternalPlayback) { _, _ in player.configure(settings: settings) }
    }
}

#Preview("iPhone / iPad foundation") {
    PreviewRoot()
}

@MainActor
private struct PreviewRoot: View {
    @State private var store = StreamVueStore.preview()
    @State private var settings = StreamVueSettings(
        defaults: UserDefaults(suiteName: "StreamVueSettingsPreview-\(UUID().uuidString)")!
    )
    @State private var player = StreamPlayerController()
    @State private var theme = StreamVueTheme()

    var body: some View {
        #if os(tvOS)
        AppleTVRootView()
            .environment(store)
            .environment(settings)
            .environment(player)
            .environment(theme)
            .preferredColorScheme(.dark)
        #else
        MobileRootView()
            .environment(store)
            .environment(settings)
            .environment(player)
            .environment(theme)
            .preferredColorScheme(.dark)
        #endif
    }
}
#endif
