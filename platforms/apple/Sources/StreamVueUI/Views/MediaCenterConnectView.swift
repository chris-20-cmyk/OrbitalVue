#if os(iOS) || os(tvOS)
import StreamVueCore
import SwiftUI

struct MediaCenterConnectView: View {
    let provider: MediaCenterProvider
    let onConnected: () -> Void

    @Environment(StreamVueStore.self) private var store
    @Environment(StreamVueTheme.self) private var theme
    @State private var serverAddress = ""
    @State private var displayName = ""
    @State private var plexToken = ""
    @State private var username = ""
    @State private var password = ""
    @State private var allowInsecureHTTP = false
    @State private var isSubmitting = false
    @State private var isShowingManualPlex = false
    @State private var plexAccount = PlexAccountConnectModel()
    @FocusState private var focusedField: Field?

    var body: some View {
        Form {
            if provider == .plex {
                PlexAccountConnectSection(model: plexAccount, onConnected: onConnected)

                Section {
                    #if os(tvOS)
                    Button {
                        isShowingManualPlex.toggle()
                    } label: {
                        HStack {
                            Text("Connect with a server address and token")
                            Spacer()
                            Image(systemName: isShowingManualPlex ? "chevron.up" : "chevron.down")
                                .foregroundStyle(theme.muted)
                        }
                    }
                    if isShowingManualPlex {
                        manualPlexFields
                    }
                    #else
                    DisclosureGroup(
                        "Connect with a server address and token",
                        isExpanded: $isShowingManualPlex
                    ) {
                        manualPlexFields
                    }
                    #endif
                } header: {
                    Text("Advanced manual connection")
                }
            } else {
                Section {
                    TextField("https://media-server.example:port", text: $serverAddress)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .focused($focusedField, equals: .serverAddress)
                        #if os(iOS)
                        .keyboardType(.URL)
                        #endif
                    TextField("Server nickname (optional)", text: $displayName)
                        .focused($focusedField, equals: .displayName)
                } header: {
                    Text("Server")
                } footer: {
                    Text("Use the full address of a server you control. HTTPS is required unless you explicitly approve local unencrypted HTTP below.")
                }

                Section {
                    TextField("Username", text: $username)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .focused($focusedField, equals: .username)
                    SecureField("Password", text: $password)
                        .focused($focusedField, equals: .password)
                } header: {
                    Text("Emby sign in")
                }
            }

            if usesCleartextHTTP && (provider == .emby || isShowingManualPlex) {
                Section {
                    Toggle("Allow unencrypted local connection", isOn: $allowInsecureHTTP)
                } header: {
                    Label("Security warning", systemImage: "exclamationmark.shield.fill")
                        .foregroundStyle(theme.warning)
                } footer: {
                    Text("HTTP can expose your sign-in and viewing activity to devices on this network. OrbitalVue saves this approval only for the verified server.")
                }
            }

            Section {
                Label(
                    "Credentials stay in Apple Keychain. Saved library data contains only a protected credential reference.",
                    systemImage: "lock.shield.fill"
                )
                .font(.footnote)
                .foregroundStyle(theme.muted)
            }

            if provider == .emby || isShowingManualPlex {
                Section {
                    Button(action: connect) {
                        HStack(spacing: 10) {
                            if isSubmitting { ProgressView() }
                            Label(
                                isSubmitting ? "Verifying server…" : "Connect \(provider.displayName) manually",
                                systemImage: "link"
                            )
                            .frame(maxWidth: .infinity)
                        }
                    }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.large)
                    .disabled(!canConnect || isSubmitting || store.isLoading)
                }
            }
        }
        #if os(iOS)
        .scrollContentBackground(.hidden)
        #endif
        .background(theme.backgroundGradient.ignoresSafeArea())
        .navigationTitle("Connect \(provider.displayName)")
        #if os(iOS)
        .navigationBarTitleDisplayMode(.inline)
        #endif
        #if os(iOS)
        .onAppear {
            if provider == .emby { focusedField = .serverAddress }
        }
        #endif
        .onChange(of: isShowingManualPlex) { _, isShowing in
            if !isShowing {
                serverAddress = ""
                displayName = ""
                plexToken = ""
                allowInsecureHTTP = false
                focusedField = nil
            }
        }
        .onChange(of: usesCleartextHTTP) { _, usesHTTP in
            if !usesHTTP { allowInsecureHTTP = false }
        }
    }

    @ViewBuilder
    private var manualPlexFields: some View {
        TextField("https://media-server.example:port", text: $serverAddress)
            .textInputAutocapitalization(.never)
            .autocorrectionDisabled()
            .focused($focusedField, equals: .serverAddress)
            #if os(iOS)
            .keyboardType(.URL)
            #endif
        TextField("Server nickname (optional)", text: $displayName)
            .focused($focusedField, equals: .displayName)
        SecureField("Plex server token", text: $plexToken)
            .textInputAutocapitalization(.never)
            .autocorrectionDisabled()
            .focused($focusedField, equals: .plexToken)
        Text("Use this only when account discovery is unavailable. Enter a server-scoped token, never a Plex password.")
            .font(.footnote)
            .foregroundStyle(theme.muted)
    }

    private var usesCleartextHTTP: Bool {
        normalized(serverAddress).lowercased().hasPrefix("http://")
    }

    private var canConnect: Bool {
        guard !normalized(serverAddress).isEmpty else { return false }
        guard !usesCleartextHTTP || allowInsecureHTTP else { return false }
        switch provider {
        case .plex:
            return !normalized(plexToken).isEmpty
        case .emby:
            return !normalized(username).isEmpty && !password.isEmpty
        }
    }

    private func connect() {
        guard canConnect, !isSubmitting else { return }
        focusedField = nil
        isSubmitting = true
        Task { @MainActor in
            let name = normalized(displayName)
            let connected: Bool
            switch provider {
            case .plex:
                connected = await store.connectPlex(
                    serverAddress: normalized(serverAddress),
                    token: normalized(plexToken),
                    displayName: name.isEmpty ? nil : name,
                    allowInsecureHTTP: usesCleartextHTTP && allowInsecureHTTP
                )
            case .emby:
                connected = await store.connectEmby(
                    serverAddress: normalized(serverAddress),
                    username: normalized(username),
                    password: password,
                    displayName: name.isEmpty ? nil : name,
                    allowInsecureHTTP: usesCleartextHTTP && allowInsecureHTTP
                )
            }
            isSubmitting = false
            if connected { onConnected() }
        }
    }

    private func normalized(_ value: String) -> String {
        value.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private enum Field: Hashable {
        case serverAddress
        case displayName
        case plexToken
        case username
        case password
    }
}
#endif
