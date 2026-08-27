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
    @FocusState private var focusedField: Field?

    var body: some View {
        Form {
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

            if usesCleartextHTTP {
                Section {
                    Toggle("Allow unencrypted local connection", isOn: $allowInsecureHTTP)
                } header: {
                    Label("Security warning", systemImage: "exclamationmark.shield.fill")
                        .foregroundStyle(theme.warning)
                } footer: {
                    Text("HTTP can expose your sign-in and viewing activity to devices on this network. StreamVue saves this approval only for the verified server.")
                }
            }

            if provider == .plex {
                Section {
                    SecureField("Plex server token", text: $plexToken)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .focused($focusedField, equals: .plexToken)
                } header: {
                    Text("Plex access")
                } footer: {
                    Text("This first premium checkpoint accepts a token for the selected Plex server. Plex account sign-in and automatic server discovery are the next connection upgrade.")
                }
            } else {
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

            Section {
                Label(
                    "Credentials stay in Apple Keychain. Saved library data contains only a protected credential reference.",
                    systemImage: "lock.shield.fill"
                )
                .font(.footnote)
                .foregroundStyle(theme.muted)
            }

            Section {
                Button(action: connect) {
                    HStack(spacing: 10) {
                        if isSubmitting { ProgressView() }
                        Label(
                            isSubmitting ? "Verifying server…" : "Connect \(provider.displayName)",
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
        #if os(iOS)
        .scrollContentBackground(.hidden)
        #endif
        .background(theme.backgroundGradient.ignoresSafeArea())
        .navigationTitle("Connect \(provider.displayName)")
        #if os(iOS)
        .navigationBarTitleDisplayMode(.inline)
        #endif
        #if os(iOS)
        .onAppear { focusedField = .serverAddress }
        #endif
        .onChange(of: usesCleartextHTTP) { _, usesHTTP in
            if !usesHTTP { allowInsecureHTTP = false }
        }
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
