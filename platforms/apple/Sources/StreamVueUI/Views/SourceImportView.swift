#if os(iOS) || os(tvOS)
import StreamVueCore
import SwiftUI
import UniformTypeIdentifiers

struct SourceImportView: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(StreamVueStore.self) private var store
    @Environment(StreamVueTheme.self) private var theme
    @State private var playlistURL = ""
    @State private var isImportingFile = false
    @FocusState private var isURLFocused: Bool

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 24) {
                    BrandMark()
                    VStack(alignment: .leading, spacing: 8) {
                        Text("Connect your channels")
                            .font(.largeTitle.bold())
                        Text("Use a provider URL or import an M3U/M3U8 file you are authorized to access. StreamVue never uploads your playlist.")
                            .foregroundStyle(theme.muted)
                    }

                    VStack(alignment: .leading, spacing: 10) {
                        Text("PLAYLIST URL")
                            .font(.caption.weight(.bold))
                            .tracking(1.4)
                            .foregroundStyle(theme.muted)
                        TextField("https://provider.example/list.m3u", text: $playlistURL)
                            .textInputAutocapitalization(.never)
                            .autocorrectionDisabled()
                            .focused($isURLFocused)
                            .textFieldStyle(.plain)
                            .padding(16)
                            .background(theme.surface, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                            .overlay {
                                RoundedRectangle(cornerRadius: 14, style: .continuous)
                                    .stroke(theme.border, lineWidth: 1)
                            }
                            .onSubmit(connectURL)
                        if usesCleartextHTTP {
                            Label(
                                "This provider uses unencrypted HTTP. Playlist credentials and channel requests may be visible on the network.",
                                systemImage: "exclamationmark.shield.fill"
                            )
                            .font(.footnote.weight(.medium))
                            .foregroundStyle(theme.warning)
                            .padding(.horizontal, 4)
                        }
                        Button(action: connectURL) {
                            Label(store.isLoading ? "Connecting…" : "Connect playlist", systemImage: "link")
                                .frame(maxWidth: .infinity)
                        }
                        .buttonStyle(.borderedProminent)
                        .controlSize(.large)
                        .disabled(playlistURL.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || store.isLoading)
                    }

                    #if os(iOS)
                    HStack(spacing: 12) {
                        Rectangle().fill(theme.border).frame(height: 1)
                        Text("OR")
                            .font(.caption2.weight(.bold))
                            .foregroundStyle(theme.muted)
                        Rectangle().fill(theme.border).frame(height: 1)
                    }
                    Button {
                        isImportingFile = true
                    } label: {
                        Label("Choose M3U file", systemImage: "doc.badge.plus")
                            .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.bordered)
                    .controlSize(.large)
                    #endif

                    Label(
                        "By connecting a source, you confirm that you have permission to view its content.",
                        systemImage: "checkmark.shield"
                    )
                    .font(.footnote)
                    .foregroundStyle(theme.muted)
                    .padding(14)
                    .background(theme.backgroundRaised, in: RoundedRectangle(cornerRadius: 13, style: .continuous))
                }
                .frame(maxWidth: 620, alignment: .leading)
                .padding(28)
                .frame(maxWidth: .infinity)
            }
            .background(theme.backgroundGradient.ignoresSafeArea())
            .navigationTitle("Add source")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
            }
            .onAppear { isURLFocused = true }
            #if os(iOS)
            .fileImporter(
                isPresented: $isImportingFile,
                allowedContentTypes: [.m3uPlaylist, .plainText],
                allowsMultipleSelection: false
            ) { result in
                guard case .success(let urls) = result, let url = urls.first else { return }
                Task {
                    let previousID = store.catalog?.catalogId
                    await store.importDocument(url)
                    if store.catalog?.catalogId != previousID { dismiss() }
                }
            }
            #endif
        }
    }

    private func connectURL() {
        let value = playlistURL
        Task {
            let previousID = store.catalog?.catalogId
            await store.importURL(value)
            if store.catalog?.catalogId != previousID { dismiss() }
        }
    }

    private var usesCleartextHTTP: Bool {
        playlistURL.trimmingCharacters(in: .whitespacesAndNewlines)
            .lowercased()
            .hasPrefix("http://")
    }
}

struct FirstRunOnboardingView: View {
    let onAddSource: () -> Void
    @Environment(StreamVueTheme.self) private var theme

    var body: some View {
        ZStack {
            theme.backgroundGradient.ignoresSafeArea()
            VStack(spacing: 28) {
                BrandMark()
                ZStack {
                    Circle()
                        .fill(theme.accentDim.opacity(0.65))
                        .frame(width: 128, height: 128)
                        .overlay { Circle().stroke(theme.accent.opacity(0.28), lineWidth: 1) }
                    Image(systemName: "play.fill")
                        .font(.system(size: 54, weight: .black))
                        .foregroundStyle(theme.accent)
                        .offset(x: 4)
                }
                VStack(spacing: 10) {
                    Text("A better signal path")
                        .font(.system(.largeTitle, design: .rounded, weight: .bold))
                    Text("Native playback, exact playlist groups, private on-device storage, and a library designed for every Apple screen.")
                        .font(.title3)
                        .foregroundStyle(theme.muted)
                        .multilineTextAlignment(.center)
                        .frame(maxWidth: 660)
                }
                Button(action: onAddSource) {
                    Label("Connect a playlist", systemImage: "plus")
                        .frame(minWidth: 220)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                Label("No bundled channels. Your source stays on this device.", systemImage: "lock.shield")
                    .font(.footnote)
                    .foregroundStyle(theme.muted)
            }
            .padding(30)
        }
    }
}
#endif
