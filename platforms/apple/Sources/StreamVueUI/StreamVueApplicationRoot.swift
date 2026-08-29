#if os(iOS) || os(tvOS)
import StreamVueCore
import SwiftUI

@MainActor
public struct StreamVueApplicationRoot: View {
    @State private var store: StreamVueStore
    @State private var settings: StreamVueSettings
    @State private var player: StreamPlayerController
    @State private var theme: StreamVueTheme
    @State private var premiumPurchases: PremiumPurchaseStore
    @State private var ksRetuneTask: Task<Void, Never>?
    @State private var playbackResolutionTask: Task<Void, Never>?

    public init() {
        _store = State(initialValue: StreamVueStore())
        _settings = State(initialValue: StreamVueSettings())
        _player = State(initialValue: StreamPlayerController())
        _theme = State(initialValue: StreamVueTheme())
        _premiumPurchases = State(initialValue: PremiumPurchaseStore())
        _ksRetuneTask = State(initialValue: nil)
        _playbackResolutionTask = State(initialValue: nil)
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
        .environment(premiumPurchases)
        .tint(theme.accent)
        .preferredColorScheme(.dark)
        .task { await store.start() }
        .task { await premiumPurchases.start() }
        .task(id: store.selectedChannel?.id) {
            guard let channel = store.selectedChannel else {
                player.stop()
                return
            }
            let delay = settings.channelZappingDelayMilliseconds
            if delay > 0 {
                do {
                    try await Task.sleep(for: .milliseconds(delay))
                } catch {
                    return
                }
            }
            guard !Task.isCancelled else { return }
            await resolveAndTune(channel)
        }
        .onChange(of: settings.playbackEngine) { _, _ in retuneSelectedChannel() }
        .onChange(of: settings.bufferPreference) { _, _ in player.configure(settings: settings) }
        .onChange(of: settings.allowsExternalPlayback) { _, _ in player.configure(settings: settings) }
        .onChange(of: settings.allowsPictureInPicture) { _, _ in player.configure(settings: settings) }
        .onChange(of: settings.aspectMode) { _, mode in player.applyKSAspectMode(mode) }
        .onChange(of: settings.ksBufferDurationSeconds) { _, _ in scheduleKSRetune() }
        .onChange(of: settings.ksAdaptiveFrameRate) { _, _ in scheduleKSRetune() }
        .onChange(of: settings.ksHardwareDecode) { _, _ in scheduleKSRetune() }
        .onChange(of: settings.ksAsynchronousDecompression) { _, _ in scheduleKSRetune() }
        .onChange(of: settings.ksAutomaticDeinterlacing) { _, _ in scheduleKSRetune() }
        .onChange(of: settings.preferredAudioLanguage) { _, _ in scheduleKSRetune() }
        .onChange(of: settings.preferredSubtitleLanguage) { _, _ in scheduleKSRetune() }
        .onChange(of: settings.ksSubtitleFontSize) { _, _ in player.configure(settings: settings) }
        .onChange(of: premiumPurchases.access) { previous, current in
            if previous.canUseMediaCenters, !current.canUseMediaCenters {
                player.stop()
            }
            Task {
                await store.premiumAccessDidChange(from: previous, to: current)
            }
        }
        .onDisappear {
            ksRetuneTask?.cancel()
            playbackResolutionTask?.cancel()
        }
    }

    private func retuneKSPlayer() {
        guard player.activeEngine == .ksPlayer else { return }
        retuneSelectedChannel()
    }

    private func scheduleKSRetune() {
        ksRetuneTask?.cancel()
        ksRetuneTask = Task { @MainActor in
            do {
                try await Task.sleep(for: .milliseconds(350))
            } catch {
                return
            }
            guard !Task.isCancelled else { return }
            retuneKSPlayer()
        }
    }

    private func retuneSelectedChannel() {
        guard let channel = store.selectedChannel else { return }
        playbackResolutionTask?.cancel()
        playbackResolutionTask = Task { @MainActor in
            await resolveAndTune(channel)
        }
    }

    private func resolveAndTune(_ channel: CatalogChannel) async {
        guard let playableChannel = await store.playbackChannel(for: channel) else {
            guard !Task.isCancelled, store.selectedChannel?.id == channel.id else { return }
            player.stop()
            return
        }
        guard !Task.isCancelled, store.selectedChannel?.id == channel.id else { return }
        player.tune(to: playableChannel, settings: settings)
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
    @State private var premiumPurchases = PremiumPurchaseStore()

    var body: some View {
        #if os(tvOS)
        AppleTVRootView()
            .environment(store)
            .environment(settings)
            .environment(player)
            .environment(theme)
            .environment(premiumPurchases)
            .preferredColorScheme(.dark)
        #else
        MobileRootView()
            .environment(store)
            .environment(settings)
            .environment(player)
            .environment(theme)
            .environment(premiumPurchases)
            .preferredColorScheme(.dark)
        #endif
    }
}
#endif
