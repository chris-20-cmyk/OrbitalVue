import Testing
@testable import StreamVueCore

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
            productID: "com.streamvue.personal-media-centers"
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
            .appendingPathComponent("StreamVuePremiumAccessTests-\(UUID().uuidString)", isDirectory: true)
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
            .appendingPathComponent("StreamVuePremiumRuntimeTests-\(UUID().uuidString)", isDirectory: true)
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
            productID: "com.streamvue.personal-media-centers"
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
}

private actor PremiumAccessNetworkProbe: MediaCenterHTTPClient {
    private var count = 0

    func send(_ request: MediaCenterHTTPRequest) async throws -> MediaCenterHTTPResponse {
        count += 1
        throw URLError(.cannotConnectToHost)
    }

    func requestCount() -> Int { count }
}
