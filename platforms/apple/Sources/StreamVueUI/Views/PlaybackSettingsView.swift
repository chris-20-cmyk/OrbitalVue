#if os(iOS) || os(tvOS)
import Foundation
import StreamVueCore
import SwiftUI

struct PlaybackSettingsView: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(StreamVueStore.self) private var store
    @Environment(StreamVueSettings.self) private var settings
    @Environment(StreamVueTheme.self) private var theme
    @State private var confirmRemoval = false

    var body: some View {
        @Bindable var settings = settings
        NavigationStack {
            Form {
                Section("Primary video player") {
                    Picker("Primary video player", selection: $settings.playbackEngine) {
                        ForEach(PlaybackEnginePreference.allCases) { engine in
                            Text(engine.label).tag(engine)
                        }
                    }
                    .pickerStyle(.inline)
                    .labelsHidden()
                    Text(settings.playbackEngine.detail)
                        .font(.footnote)
                        .foregroundStyle(theme.muted)
                    Toggle("Fallback video players", isOn: $settings.fallbackPlaybackEngine)
                }

                Section("General playback") {
                    Picker("Default aspect", selection: $settings.aspectMode) {
                        ForEach(VideoAspectMode.allCases) { mode in
                            Text(mode.label).tag(mode)
                        }
                    }
                    Toggle("Play when a channel is selected", isOn: $settings.autoPlaySelection)
                    Stepper(
                        value: $settings.channelZappingDelayMilliseconds,
                        in: 0 ... 2_000,
                        step: 100
                    ) {
                        LabeledContent(
                            "Channel zapping delay",
                            value: "\(settings.channelZappingDelayMilliseconds) ms"
                        )
                    }
                    Toggle("Allow AirPlay and external playback", isOn: $settings.allowsExternalPlayback)
                    #if os(iOS)
                    Toggle("Picture in Picture", isOn: $settings.allowsPictureInPicture)
                    #endif
                }

                if settings.playbackEngine == .ksPlayer {
                    Section("KSPlayer (Metal)") {
                        Stepper(
                            value: $settings.ksBufferDurationSeconds,
                            in: 1 ... 30
                        ) {
                            LabeledContent(
                                "Buffer duration",
                                value: "\(settings.ksBufferDurationSeconds) seconds"
                            )
                        }
                        Toggle("Adaptive frame rate", isOn: $settings.ksAdaptiveFrameRate)
                        Toggle("Hardware decode", isOn: $settings.ksHardwareDecode)
                        Toggle(
                            "Asynchronous decompression",
                            isOn: $settings.ksAsynchronousDecompression
                        )
                        Toggle(
                            "Automatic deinterlacing",
                            isOn: $settings.ksAutomaticDeinterlacing
                        )
                        Picker("Preferred audio language", selection: $settings.preferredAudioLanguage) {
                            ForEach(PreferredAudioLanguage.allCases) { language in
                                Text(language.label).tag(language)
                            }
                        }
                        Picker("Preferred subtitles", selection: $settings.preferredSubtitleLanguage) {
                            ForEach(PreferredSubtitleLanguage.allCases) { language in
                                Text(language.label).tag(language)
                            }
                        }
                        Stepper(
                            value: $settings.ksSubtitleFontSize,
                            in: 12 ... 80,
                            step: 2
                        ) {
                            LabeledContent(
                                "Subtitle font size",
                                value: "\(Int(settings.ksSubtitleFontSize)) pt"
                            )
                        }
                    }

                    Section("KSPlayer capabilities") {
                        CapabilityRow(
                            icon: "bolt.horizontal.circle",
                            title: "Metal and VideoToolbox",
                            detail: "KSMEPlayer is primary, with FFmpeg demuxing and device-supported hardware decoding."
                        )
                        CapabilityRow(
                            icon: "captions.bubble",
                            title: "Embedded tracks and subtitles",
                            detail: "Player controls expose audio tracks and text/image subtitles, with preferred-language selection."
                        )
                        CapabilityRow(
                            icon: "waveform",
                            title: "Multichannel and spatial audio",
                            detail: "Uses the available route and codec support. Native E-AC-3 Atmos in KSPlayer requires its separately licensed build."
                        )
                    }

                    Section("KSPlayer distribution license") {
                        Text("The integrated public KSPlayer package is GPL-3.0. Personal source builds can use it now; an Apple Store binary requires a compatible GPL release of StreamVue or KSPlayer’s separately licensed LGPL/commercial package.")
                            .font(.footnote)
                            .foregroundStyle(theme.muted)
                        Link(
                            "Review KSPlayer license options",
                            destination: URL(string: "https://github.com/kingslay/KSPlayer#license")!
                        )
                    }
                } else {
                    Section("AVKit buffering") {
                        Picker("Buffer strategy", selection: $settings.bufferPreference) {
                            ForEach(BufferPreference.allCases) { preference in
                                Text(preference.label).tag(preference)
                            }
                        }
                    }
                }

                Section("Apple playback system") {
                    CapabilityRow(
                        icon: "cpu",
                        title: "Hardware decoding",
                        detail: "AVKit manages decoding automatically for the device and codec."
                    )
                    CapabilityRow(
                        icon: "waveform",
                        title: "Spatial audio and Atmos",
                        detail: "AVKit preserves it when the source, Apple device, and output route support it."
                    )
                    CapabilityRow(
                        icon: "tv",
                        title: "Adaptive display cadence",
                        detail: "Apple TV honors Match Content for compatible fullscreen media and display settings."
                    )
                }

                if let source = store.catalog?.sources.first {
                    Section("Connected source") {
                        LabeledContent("Name", value: source.name)
                        LabeledContent("Location", value: source.displayLocation)
                        LabeledContent("Startup refresh", value: source.refreshOnLaunch ? "On" : "File copy")
                        Button("Refresh now") { Task { await store.refresh() } }
                        Button("Remove playlist", role: .destructive) { confirmRemoval = true }
                    }
                }

                Section("Privacy") {
                    Text("Playlist addresses and credentials are excluded from interface labels and diagnostics. URL secrets are stored in Keychain; the last working playlist stays in protected app storage.")
                        .font(.footnote)
                        .foregroundStyle(theme.muted)
                }
            }
            .scrollContentBackground(.hidden)
            .background(theme.backgroundGradient.ignoresSafeArea())
            .navigationTitle("Playback & privacy")
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Done") { dismiss() }
                }
            }
            .confirmationDialog(
                "Remove this playlist from StreamVue?",
                isPresented: $confirmRemoval,
                titleVisibility: .visible
            ) {
                Button("Remove playlist", role: .destructive) {
                    Task {
                        await store.removeSource()
                        dismiss()
                    }
                }
                Button("Cancel", role: .cancel) {}
            } message: {
                Text("The protected source address and cached channel list will be removed from this device.")
            }
        }
    }
}

private struct CapabilityRow: View {
    let icon: String
    let title: String
    let detail: String
    @Environment(StreamVueTheme.self) private var theme

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: icon)
                .frame(width: 26)
                .foregroundStyle(theme.accent)
            VStack(alignment: .leading, spacing: 3) {
                Text(title).font(.headline)
                Text(detail).font(.caption).foregroundStyle(theme.muted)
            }
        }
        .padding(.vertical, 3)
    }
}
#endif
