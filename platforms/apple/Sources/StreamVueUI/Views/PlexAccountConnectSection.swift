#if os(iOS) || os(tvOS)
import CoreImage
import CoreImage.CIFilterBuiltins
import StreamVueCore
import SwiftUI
import UIKit

struct PlexAccountConnectSection: View {
    @Bindable var model: PlexAccountConnectModel
    let onConnected: () -> Void

    @Environment(StreamVueStore.self) private var store
    @Environment(StreamVueTheme.self) private var theme
    @State private var actionTask: Task<Void, Never>?

    var body: some View {
        Section {
            content
        } header: {
            Text("Plex account")
        } footer: {
            Text("Plex account and server tokens stay outside this screen and are never written to the StreamVue catalog. The selected server credential is saved in Apple Keychain only after its identity is verified.")
        }
        .task(id: model.challengeID) {
            guard model.challengeID != nil else { return }
            await model.poll(using: store)
        }
        .onDisappear {
            actionTask?.cancel()
            Task { await model.cancel(using: store) }
        }
    }

    @ViewBuilder
    private var content: some View {
        if let challenge = model.challenge {
            signInChallenge(challenge)
        } else if let discovery = model.discovery {
            serverSelection(discovery)
        } else {
            if let failure = model.failureMessage {
                Label(failure, systemImage: "exclamationmark.triangle.fill")
                    .font(.footnote)
                    .foregroundStyle(theme.warning)
                    .accessibilityLabel("Plex sign-in error: \(failure)")
            }
            Button {
                startSignIn()
            } label: {
                HStack(spacing: 10) {
                    if model.isCreating { ProgressView() }
                    Label(
                        model.isCreating ? "Preparing secure sign-in…" : "Sign in with Plex",
                        systemImage: "person.crop.circle.badge.checkmark"
                    )
                    .frame(maxWidth: .infinity)
                }
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
            .disabled(model.isCreating || store.isLoading)
        }
    }

    @ViewBuilder
    private func signInChallenge(_ challenge: PlexPinChallenge) -> some View {
        VStack(spacing: 14) {
            PlexSignInQRCode(url: challenge.authorizationURL)
                .frame(width: 184, height: 184)
                .padding(10)
                .background(.white, in: RoundedRectangle(cornerRadius: 16, style: .continuous))

            Text("Scan to approve StreamVue in Plex")
                .font(.headline)
                .multilineTextAlignment(.center)
            Text("Or open Plex sign-in on this device. StreamVue checks the protected PIN automatically and never receives your Plex password.")
                .font(.footnote)
                .foregroundStyle(theme.muted)
                .multilineTextAlignment(.center)
            Link(destination: challenge.authorizationURL) {
                Label("Open Plex sign-in", systemImage: "arrow.up.right.square")
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)

            HStack(spacing: 9) {
                ProgressView()
                Text("Waiting for approval")
                Spacer()
                Text(challenge.expiresAt, style: .timer)
                    .monospacedDigit()
                    .foregroundStyle(theme.muted)
            }
            .font(.footnote.weight(.medium))

            Button("Cancel sign-in", role: .cancel) {
                cancelSignIn()
            }
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 8)
    }

    @ViewBuilder
    private func serverSelection(_ discovery: PlexServerDiscovery) -> some View {
        if let selectedServer = model.selectedServer,
           let selectedConnection = model.selectedConnection {
            Picker(
                "Server",
                selection: Binding(
                    get: { model.selectedServerID ?? selectedServer.serverID },
                    set: { selectedID in model.selectServer(selectedID) }
                )
            ) {
                ForEach(discovery.servers) { server in
                    Text(server.isOwned ? server.name : "\(server.name) · Shared")
                        .tag(server.serverID)
                }
            }

            if selectedServer.connections.count > 1 {
                Picker(
                    "Connection",
                    selection: Binding(
                        get: { model.selectedConnectionID ?? selectedConnection.id },
                        set: { selectedID in model.selectConnection(selectedID) }
                    )
                ) {
                    ForEach(selectedServer.connections) { connection in
                        Text(connectionLabel(connection)).tag(connection.id)
                    }
                }
            }

            LabeledContent("Address") {
                Text(MediaCenterURLPolicy.safeDisplayLocation(for: selectedConnection.url))
                    .foregroundStyle(theme.muted)
                    .multilineTextAlignment(.trailing)
            }

            if !selectedConnection.isSecure {
                Toggle("Allow unencrypted local connection", isOn: $model.allowInsecureHTTP)
                Label(
                    "HTTP can expose the server credential and viewing activity on this network.",
                    systemImage: "exclamationmark.shield.fill"
                )
                .font(.footnote)
                .foregroundStyle(theme.warning)
            }

            Button {
                actionTask?.cancel()
                actionTask = Task { @MainActor in
                    await model.connect(using: store, onConnected: onConnected)
                }
            } label: {
                HStack(spacing: 10) {
                    if model.isConnecting { ProgressView() }
                    Label(
                        model.isConnecting ? "Connecting server…" : "Connect \(selectedServer.name)",
                        systemImage: "server.rack"
                    )
                    .frame(maxWidth: .infinity)
                }
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
            .disabled(!model.canConnect || store.isLoading)

            Button("Use another Plex account") {
                startSignIn()
            }
            .disabled(model.isConnecting)
        } else {
            Label(
                "Plex did not provide a usable server connection.",
                systemImage: "exclamationmark.triangle.fill"
            )
            .foregroundStyle(theme.warning)
        }
    }

    private func connectionLabel(_ connection: PlexServerConnectionChoice) -> String {
        var details: [String] = [connection.isSecure ? "Secure" : "HTTP"]
        if connection.isLocal { details.append("Local") }
        if connection.isRelay { details.append("Relay") }
        if connection.isIPv6 { details.append("IPv6") }
        return details.joined(separator: " · ")
    }

    private func startSignIn() {
        actionTask?.cancel()
        actionTask = Task { @MainActor in
            await model.start(using: store)
        }
    }

    private func cancelSignIn() {
        actionTask?.cancel()
        actionTask = Task { @MainActor in
            await model.cancel(using: store)
        }
    }
}

private struct PlexSignInQRCode: View {
    private let image: UIImage?

    init(url: URL) {
        let filter = CIFilter.qrCodeGenerator()
        filter.message = Data(url.absoluteString.utf8)
        filter.correctionLevel = "M"
        let context = CIContext(options: [.useSoftwareRenderer: false])
        if let output = filter.outputImage?.transformed(by: CGAffineTransform(scaleX: 8, y: 8)),
           let cgImage = context.createCGImage(output, from: output.extent) {
            image = UIImage(cgImage: cgImage)
        } else {
            image = nil
        }
    }

    var body: some View {
        Group {
            if let image {
                Image(uiImage: image)
                    .interpolation(.none)
                    .resizable()
                    .scaledToFit()
            } else {
                Image(systemName: "qrcode")
                    .resizable()
                    .scaledToFit()
                    .foregroundStyle(.black)
                    .padding(28)
            }
        }
        .accessibilityLabel("QR code for Plex sign-in")
    }
}
#endif
