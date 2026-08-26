#if os(iOS) || os(tvOS)
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
                Section("Playback") {
                    Picker("Default aspect", selection: $settings.aspectMode) {
                        ForEach(VideoAspectMode.allCases) { mode in
                            Text(mode.label).tag(mode)
                        }
                    }
                    Picker("Buffer strategy", selection: $settings.bufferPreference) {
                        ForEach(BufferPreference.allCases) { preference in
                            Text(preference.label).tag(preference)
                        }
                    }
                    Toggle("Play when a channel is selected", isOn: $settings.autoPlaySelection)
                    Toggle("Allow AirPlay and external playback", isOn: $settings.allowsExternalPlayback)
                    #if os(iOS)
                    Toggle("Picture in Picture", isOn: $settings.allowsPictureInPicture)
                    #endif
                }

                Section("Native engine") {
                    CapabilityRow(
                        icon: "cpu",
                        title: "Hardware decoding",
                        detail: "Managed automatically by AVFoundation for the device and codec."
                    )
                    CapabilityRow(
                        icon: "waveform",
                        title: "Spatial audio and Atmos",
                        detail: "Preserved when the source, Apple device, and output route support it."
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
