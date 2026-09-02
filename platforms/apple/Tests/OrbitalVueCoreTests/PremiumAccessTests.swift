import Foundation
import Testing
@testable import OrbitalVueCore

@Suite("Premium access")
struct PremiumAccessTests {
    @Test("Personal builds include media centers without a receipt")
    func personalBuildIncluded() {
        let access = PremiumAccessPolicy.evaluate(
            distributionMode: "personal",
            hasVerifiedStorePurchase: false
        )

        #expect(access.canUseMediaCenters)
        #expect(access.accessState == .included)
        #expect(access.receiptVerification == "not-required")
        #expect(access.productID == nil)
    }

    @Test("Store and unknown builds fail closed until a real product is verified")
    func storeBuildFailsClosed() {
        #expect(!PremiumAccessPolicy.evaluate(
            distributionMode: "store",
            hasVerifiedStorePurchase: false
        ).canUseMediaCenters)
        #expect(!PremiumAccessPolicy.evaluate(
            distributionMode: "store",
            hasVerifiedStorePurchase: true
        ).canUseMediaCenters)
        #expect(!PremiumAccessPolicy.evaluate(
            distributionMode: "typo",
            hasVerifiedStorePurchase: true,
            productID: "valid.product"
        ).canUseMediaCenters)

        let verified = PremiumAccessPolicy.evaluate(
            distributionMode: "store",
            hasVerifiedStorePurchase: true,
            productID: "com.orbitalvue.personal-media-centers"
        )
        #expect(verified.canUseMediaCenters)
        #expect(verified.accessState == .verified)
        #expect(verified.acquisition == "one-time")
    }

    @Test("Locked repository rejects a connection before any network request")
    func lockedRepositoryDoesNotReachNetwork() async {
        let http = PremiumAccessNetworkProbe()
        let service = MediaCenterService(httpClient: http)
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("OrbitalVuePremiumAccessTests-\(UUID().uuidString)", isDirectory: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let repository = MediaCenterRepository(
            directory: directory,
            service: service,
            premiumAccess: PremiumAccessPolicy.evaluate(
                distributionMode: "store",
                hasVerifiedStorePurchase: false
            )
        )

        do {
            _ = try await repository.connectPlex(
                serverAddress: "https://plex.invalid:32400",
                token: "must-never-leave-this-test"
            )
            Issue.record("A locked store repository accepted a Plex connection.")
        } catch let error as PremiumAccessError {
            #expect(error.errorDescription?.contains("one-time store purchase") == true)
        } catch {
            Issue.record("The repository returned the wrong lock error: \(error)")
        }
        #expect(await http.requestCount() == 0)
    }

    @Test("Runtime entitlement changes reach an existing repository")
    func runtimeEntitlementUpdatesRepository() async {
        let locked = PremiumAccessPolicy.evaluate(
            distributionMode: "store",
            hasVerifiedStorePurchase: false
        )
        let runtime = PremiumAccessRuntime(initial: locked)
        let http = PremiumAccessNetworkProbe()
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("OrbitalVuePremiumRuntimeTests-\(UUID().uuidString)", isDirectory: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let repository = MediaCenterRepository(
            directory: directory,
            service: MediaCenterService(httpClient: http),
            premiumAccessRuntime: runtime
        )

        do {
            _ = try await repository.connectPlex(
                serverAddress: "https://plex.invalid:32400",
                token: "runtime-token"
            )
            Issue.record("The locked runtime accepted a Plex connection.")
        } catch is PremiumAccessError {
        } catch {
            Issue.record("The locked runtime returned the wrong error: \(error)")
        }
        #expect(await http.requestCount() == 0)

        await runtime.update(PremiumAccessPolicy.evaluate(
            distributionMode: "store",
            hasVerifiedStorePurchase: true,
            productID: "com.orbitalvue.personal-media-centers"
        ))
        do {
            _ = try await repository.connectPlex(
                serverAddress: "https://plex.invalid:32400",
                token: "runtime-token"
            )
        } catch {
            // The probe intentionally rejects the first request; reaching it proves the updated gate was read.
        }
        #expect(await http.requestCount() == 1)
    }

    @Test("Plex account discovery fails closed when premium access is revoked in flight")
    func accountDiscoveryRechecksRuntimeEntitlement() async throws {
        let unlocked = PremiumAccessPolicy.evaluate(
            distributionMode: "store",
            hasVerifiedStorePurchase: true,
            productID: "com.orbitalvue.personal-media-centers"
        )
        let locked = PremiumAccessPolicy.evaluate(
            distributionMode: "store",
            hasVerifiedStorePurchase: false
        )
        let runtime = PremiumAccessRuntime(initial: unlocked)
        let http = PausingPlexResourcesHTTPClient()
        let service = MediaCenterService(
            httpClient: http,
            secretStore: PremiumAccessMemorySecretStore(),
            plexClientIdentifier: "orbitalvue-premium-race-test"
        )
        let repository = MediaCenterRepository(
            service: service,
            premiumAccessRuntime: runtime
        )
        let challenge = try await repository.createPlexSignInChallenge()
        let completionTask = Task {
            try await repository.completePlexSignIn(challenge: challenge)
        }

        await http.waitUntilResourcesRequested()
        await runtime.update(locked)
        await http.releaseResources()

        do {
            _ = try await completionTask.value
            Issue.record("Discovery completed after premium access was revoked.")
        } catch is PremiumAccessError {
        } catch {
            Issue.record("Discovery returned the wrong revocation error: \(error)")
        }
    }
}

private actor PremiumAccessNetworkProbe: MediaCenterHTTPClient {
    private var count = 0

    func send(_ request: MediaCenterHTTPRequest) async throws -> MediaCenterHTTPResponse {
        count += 1
        throw URLError(.cannotConnectToHost)
    }

    func requestCount() -> Int { count }
}

private actor PausingPlexResourcesHTTPClient: MediaCenterHTTPClient {
    private var resourcesRequested = false
    private var resourcesReleased = false
    private var resourcesContinuation: CheckedContinuation<Void, Never>?

    func send(_ request: MediaCenterHTTPRequest) async throws -> MediaCenterHTTPResponse {
        switch (request.url.host, request.url.path) {
        case ("clients.plex.tv", "/api/v2/pins"):
            return response(#"{"id":91,"code":"PREMIUM","expiresIn":300}"#)
        case ("clients.plex.tv", "/api/v2/pins/91"):
            return response(#"{"authToken":"transient-premium-test-token","expiresIn":604800}"#)
        case ("plex.tv", "/api/v2/user"):
            return response(#"{"id":1}"#)
        case ("clients.plex.tv", "/api/v2/resources"):
            resourcesRequested = true
            if !resourcesReleased {
                await withCheckedContinuation { continuation in
                    resourcesContinuation = continuation
                }
            }
            return response(
                #"[{"name":"Home Plex","clientIdentifier":"plex-premium-race","provides":"server","owned":true,"accessToken":"server-token","connections":[{"uri":"https://plex.premium:32400","local":true,"relay":false,"IPv6":false}]}]"#
            )
        default:
            return MediaCenterHTTPResponse(statusCode: 404, body: Data())
        }
    }

    func waitUntilResourcesRequested() async {
        while !resourcesRequested {
            await Task.yield()
        }
    }

    func releaseResources() {
        resourcesReleased = true
        resourcesContinuation?.resume()
        resourcesContinuation = nil
    }

    private func response(_ json: String) -> MediaCenterHTTPResponse {
        MediaCenterHTTPResponse(
            statusCode: 200,
            headers: ["Content-Type": "application/json"],
            body: Data(json.utf8)
        )
    }
}

private actor PremiumAccessMemorySecretStore: SourceSecretStore {
    private var values: [String: String] = [:]

    func save(_ value: String, for key: String) {
        values[key] = value
    }

    func value(for key: String) -> String? {
        values[key]
    }

    func removeValue(for key: String) {
        values.removeValue(forKey: key)
    }
}
