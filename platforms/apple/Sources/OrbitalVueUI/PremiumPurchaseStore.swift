#if os(iOS) || os(tvOS)
import Foundation
import Observation
import StoreKit
import OrbitalVueCore

@MainActor
@Observable
public final class PremiumPurchaseStore {
    public private(set) var access: PremiumAccessSnapshot
    public private(set) var configuredProductID: String?
    public private(set) var productTitle: String?
    public private(set) var localizedPrice: String?
    public private(set) var isBusy = false
    public private(set) var message: String

    public var canPurchase: Bool {
        access.distributionMode == .store && !access.canUseMediaCenters && product != nil && !isBusy
    }

    public var canRestore: Bool {
        access.distributionMode == .store && configuredProductID != nil && !isBusy
    }

    private let runtime: PremiumAccessRuntime
    private var product: Product?
    private var updatesTask: Task<Void, Never>?
    private var hasStarted = false

    public init(
        runtime: PremiumAccessRuntime = .shared,
        bundle: Bundle = .main
    ) {
        let initial = PremiumAccessPolicy.current
        self.runtime = runtime
        self.access = initial
        self.configuredProductID = Self.productID(from: bundle)
        self.message = initial.canUseMediaCenters
            ? initial.explanation
            : Self.lockedMessage(productID: Self.productID(from: bundle))
    }

    public func start() async {
        guard !hasStarted else { return }
        hasStarted = true
        await runtime.update(access)
        guard access.distributionMode == .store else { return }
        guard configuredProductID != nil else {
            message = Self.lockedMessage(productID: nil)
            return
        }
        updatesTask = Task { [weak self] in
            for await result in Transaction.updates {
                guard !Task.isCancelled else { return }
                await self?.handleTransactionUpdate(result)
            }
        }
        await loadProduct()
        await refreshEntitlement()
    }

    public func purchase() async {
        guard let product, let productID = configuredProductID else {
            message = "The App Store one-time product is not available."
            return
        }
        isBusy = true
        message = "Opening the App Store…"
        do {
            switch try await product.purchase() {
            case .success(let result):
                switch result {
                case .verified(let transaction):
                    guard transaction.productID == productID,
                          transaction.revocationDate == nil else {
                        await lock("The App Store transaction did not match the configured product.")
                        return
                    }
                    await transaction.finish()
                    await unlock(productID: productID, message: "Premium media centers unlocked with a verified one-time purchase.")
                case .unverified:
                    await lock("The App Store could not cryptographically verify this transaction.")
                }
            case .pending:
                await lock("Purchase pending. Premium stays locked until the App Store confirms payment.")
            case .userCancelled:
                isBusy = false
                message = "Purchase canceled. Nothing was charged."
            @unknown default:
                await lock("The App Store returned an unsupported purchase result.")
            }
        } catch {
            await lock("The App Store could not complete the purchase: \(error.localizedDescription)")
        }
    }

    public func restore() async {
        guard configuredProductID != nil else {
            message = Self.lockedMessage(productID: nil)
            return
        }
        isBusy = true
        message = "Restoring App Store purchases…"
        do {
            // AppStore.sync may show an account prompt, so it is called only from this explicit action.
            try await AppStore.sync()
            await refreshEntitlement(restoring: true)
        } catch {
            await lock("The App Store could not restore purchases: \(error.localizedDescription)")
        }
    }

    public func refreshEntitlement(restoring: Bool = false) async {
        guard let productID = configuredProductID else {
            await lock(Self.lockedMessage(productID: nil))
            return
        }
        var ownsProduct = false
        for await result in Transaction.currentEntitlements {
            guard case .verified(let transaction) = result,
                  transaction.productID == productID,
                  transaction.revocationDate == nil else { continue }
            ownsProduct = true
        }
        if ownsProduct {
            await unlock(
                productID: productID,
                message: restoring
                    ? "Premium purchase restored from the App Store."
                    : "Verified App Store purchase active."
            )
        } else {
            await lock(
                restoring
                    ? "No verified premium purchase was found for this App Store account."
                    : "No verified App Store purchase is currently owned."
            )
        }
    }

    private func loadProduct() async {
        guard let productID = configuredProductID else { return }
        isBusy = true
        message = "Loading the one-time App Store product…"
        do {
            let products = try await Product.products(for: [productID])
            guard let product = products.single(where: { $0.id == productID }),
                  product.type == .nonConsumable else {
                self.product = nil
                isBusy = false
                message = "The configured App Store product is missing or is not a non-consumable purchase."
                return
            }
            self.product = product
            productTitle = product.displayName
            localizedPrice = product.displayPrice
            isBusy = false
            message = "Buy once from the App Store or restore a purchase already owned by this account."
        } catch {
            product = nil
            isBusy = false
            message = "The App Store product could not be loaded: \(error.localizedDescription)"
        }
    }

    private func handleTransactionUpdate(_ result: VerificationResult<Transaction>) async {
        switch result {
        case .verified(let transaction):
            guard transaction.productID == configuredProductID else { return }
            await transaction.finish()
            await refreshEntitlement()
        case .unverified(let transaction, _):
            guard transaction.productID == configuredProductID else { return }
            await lock("The App Store reported a transaction that could not be cryptographically verified.")
        }
    }

    private func unlock(productID: String, message: String) async {
        let verified = PremiumAccessPolicy.evaluate(
            distributionMode: "store",
            hasVerifiedStorePurchase: true,
            productID: productID
        )
        access = verified
        isBusy = false
        self.message = message
        await runtime.update(verified)
    }

    private func lock(_ message: String) async {
        let locked = PremiumAccessPolicy.evaluate(
            distributionMode: "store",
            hasVerifiedStorePurchase: false,
            productID: configuredProductID
        )
        access = locked
        isBusy = false
        self.message = message
        await runtime.update(locked)
    }

    private static func productID(from bundle: Bundle) -> String? {
        guard let rawValue = bundle.object(forInfoDictionaryKey: "OrbitalVuePremiumProductID") as? String else {
            return nil
        }
        let candidate = rawValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard candidate.range(
            of: #"^[A-Za-z0-9._-]{3,256}$"#,
            options: .regularExpression
        ) != nil else { return nil }
        return candidate
    }

    private static func lockedMessage(productID: String?) -> String {
        productID == nil
            ? "The one-time App Store product has not been configured for this build."
            : "Checking the App Store for a verified one-time purchase…"
    }
}

private extension Array {
    func single(where predicate: (Element) throws -> Bool) rethrows -> Element? {
        let matches = try filter(predicate)
        return matches.count == 1 ? matches[0] : nil
    }
}
#endif
