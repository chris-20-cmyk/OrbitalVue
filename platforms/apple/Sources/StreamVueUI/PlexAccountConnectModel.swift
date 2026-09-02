#if os(iOS) || os(tvOS)
import Foundation
import Observation
import StreamVueCore

@MainActor
@Observable
final class PlexAccountConnectModel {
    enum Phase {
        case idle
        case creating
        case waiting(PlexPinChallenge)
        case ready(PlexServerDiscovery)
        case connecting(PlexServerDiscovery)
        case failed(String)
    }

    private(set) var phase: Phase = .idle
    private(set) var selectedServerID: String?
    private(set) var selectedConnectionID: String?
    var allowInsecureHTTP = false

    var challengeID: Int? {
        guard case .waiting(let challenge) = phase else { return nil }
        return challenge.id
    }

    var challenge: PlexPinChallenge? {
        guard case .waiting(let challenge) = phase else { return nil }
        return challenge
    }

    var discovery: PlexServerDiscovery? {
        switch phase {
        case .ready(let discovery), .connecting(let discovery): discovery
        default: nil
        }
    }

    var isCreating: Bool {
        if case .creating = phase { true } else { false }
    }

    var isConnecting: Bool {
        if case .connecting = phase { true } else { false }
    }

    var failureMessage: String? {
        guard case .failed(let message) = phase else { return nil }
        return message
    }

    var selectedServer: PlexDiscoveredServer? {
        guard let selectedServerID else { return discovery?.servers.first }
        return discovery?.servers.first { $0.serverID == selectedServerID }
    }

    var selectedConnection: PlexServerConnectionChoice? {
        guard let server = selectedServer else { return nil }
        guard let selectedConnectionID else { return server.preferredConnection }
        return server.connections.first { $0.id == selectedConnectionID }
    }

    var canConnect: Bool {
        guard !isConnecting, let selectedConnection else { return false }
        return selectedConnection.isSecure || allowInsecureHTTP
    }

    func start(using store: StreamVueStore) async {
        await cancel(using: store)
        phase = .creating
        do {
            let challenge = try await store.createPlexSignInChallenge()
            guard !Task.isCancelled else { return }
            phase = .waiting(challenge)
        } catch is CancellationError {
            phase = .idle
        } catch {
            phase = .failed(PlaylistSourcePolicy.redactedErrorMessage(error))
        }
    }

    func poll(using store: StreamVueStore) async {
        guard case .waiting(let challenge) = phase else { return }
        var consecutiveFailures = 0
        while !Task.isCancelled, Date() < challenge.expiresAt {
            do {
                if let discovery = try await store.completePlexSignIn(challenge: challenge) {
                    guard !Task.isCancelled else {
                        await store.cancelPlexDiscovery(sessionID: discovery.sessionID)
                        return
                    }
                    selectDefaults(in: discovery)
                    phase = .ready(discovery)
                    return
                }
                consecutiveFailures = 0
            } catch is CancellationError {
                return
            } catch {
                consecutiveFailures += 1
                if consecutiveFailures >= 3 {
                    phase = .failed(PlaylistSourcePolicy.redactedErrorMessage(error))
                    return
                }
            }

            do {
                try await Task.sleep(for: .seconds(2))
            } catch {
                return
            }
        }
        guard !Task.isCancelled else { return }
        phase = .failed(MediaCenterError.accountSignInExpired.localizedDescription)
    }

    func selectServer(_ serverID: String) {
        guard let server = discovery?.servers.first(where: { $0.serverID == serverID }) else {
            return
        }
        selectedServerID = server.serverID
        selectedConnectionID = server.preferredConnection?.id
        allowInsecureHTTP = false
    }

    func selectConnection(_ connectionID: String) {
        guard selectedServer?.connections.contains(where: { $0.id == connectionID }) == true else {
            return
        }
        selectedConnectionID = connectionID
        allowInsecureHTTP = false
    }

    func connect(using store: StreamVueStore, onConnected: () -> Void) async {
        guard case .ready(let discovery) = phase,
              let server = selectedServer,
              let connection = selectedConnection,
              canConnect else { return }
        phase = .connecting(discovery)
        let connected = await store.connectDiscoveredPlexServer(
            discovery: discovery,
            serverID: server.serverID,
            connectionURL: connection.url,
            allowInsecureHTTP: !connection.isSecure && allowInsecureHTTP
        )
        guard !Task.isCancelled else { return }
        if connected {
            onConnected()
        } else {
            phase = .ready(discovery)
        }
    }

    func cancel(using store: StreamVueStore) async {
        let sessionID = discovery?.sessionID
        phase = .idle
        selectedServerID = nil
        selectedConnectionID = nil
        allowInsecureHTTP = false
        if let sessionID {
            await store.cancelPlexDiscovery(sessionID: sessionID)
        }
    }

    private func selectDefaults(in discovery: PlexServerDiscovery) {
        let server = discovery.servers.first
        selectedServerID = server?.serverID
        selectedConnectionID = server?.preferredConnection?.id
        allowInsecureHTTP = false
    }
}
#endif
