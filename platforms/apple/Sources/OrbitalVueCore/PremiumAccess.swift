import Foundation

public enum OrbitalVueDistributionMode: String, Codable, Sendable {
    case personal
    case store
    case unknown
}

public enum PremiumAccessState: String, Codable, Sendable {
    case included
    case verified
    case unavailable
}

public struct PremiumAccessSnapshot: Codable, Equatable, Sendable {
    public let contractVersion: String
    public let featureID: String
    public let distributionMode: OrbitalVueDistributionMode
    public let accessState: PremiumAccessState
    public let acquisition: String
    public let receiptVerification: String
    public let productID: String?

    public var canUseMediaCenters: Bool {
        accessState == .included || accessState == .verified
    }

    public var badgeText: String {
        switch accessState {
        case .included: "PERSONAL BUILD • INCLUDED"
        case .verified: "PREMIUM • VERIFIED"
        case .unavailable: "PREMIUM • STORE LOCKED"
        }
    }

    public var explanation: String {
        switch accessState {
        case .included:
            "Plex and Emby are included in this personal build."
        case .verified:
            "A one-time store purchase was verified for this device account."
        case .unavailable:
            "A verified one-time store purchase is required. Store purchase verification is not connected in this build."
        }
    }

    public func requireMediaCenters() throws {
        guard canUseMediaCenters else { throw PremiumAccessError.mediaCentersUnavailable(explanation) }
    }
}

public enum PremiumAccessError: LocalizedError, Equatable, Sendable {
    case mediaCentersUnavailable(String)

    public var errorDescription: String? {
        switch self {
        case .mediaCentersUnavailable(let message): message
        }
    }
}

public enum PremiumAccessPolicy {
    public static let contractVersion = "1.0"
    public static let mediaCentersFeatureID = "personal-media-centers"

    public static var current: PremiumAccessSnapshot {
        let configuredMode = Bundle.main.object(
            forInfoDictionaryKey: "OrbitalVueDistributionMode"
        ) as? String
        return evaluate(
            distributionMode: configuredMode ?? "personal",
            hasVerifiedStorePurchase: false
        )
    }

    public static func evaluate(
        distributionMode: String?,
        hasVerifiedStorePurchase: Bool,
        productID: String? = nil
    ) -> PremiumAccessSnapshot {
        let normalizedMode = distributionMode?
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .lowercased()
        let mode: OrbitalVueDistributionMode = switch normalizedMode {
        case "personal": .personal
        case "store": .store
        default: .unknown
        }

        if mode == .personal {
            return snapshot(
                mode: mode,
                state: .included,
                acquisition: "included",
                verification: "not-required"
            )
        }

        let normalizedProductID = normalizeProductID(productID)
        if mode == .store, hasVerifiedStorePurchase, let normalizedProductID {
            return snapshot(
                mode: mode,
                state: .verified,
                acquisition: "one-time",
                verification: "verified",
                productID: normalizedProductID
            )
        }
        return snapshot(
            mode: mode,
            state: .unavailable,
            acquisition: "one-time",
            verification: "unavailable"
        )
    }

    private static func snapshot(
        mode: OrbitalVueDistributionMode,
        state: PremiumAccessState,
        acquisition: String,
        verification: String,
        productID: String? = nil
    ) -> PremiumAccessSnapshot {
        PremiumAccessSnapshot(
            contractVersion: contractVersion,
            featureID: mediaCentersFeatureID,
            distributionMode: mode,
            accessState: state,
            acquisition: acquisition,
            receiptVerification: verification,
            productID: productID
        )
    }

    private static func normalizeProductID(_ value: String?) -> String? {
        guard let candidate = value?.trimmingCharacters(in: .whitespacesAndNewlines),
              candidate.range(
                of: #"^[A-Za-z0-9._-]{3,256}$"#,
                options: .regularExpression
              ) != nil else { return nil }
        return candidate
    }
}

public actor PremiumAccessRuntime {
    public static let shared = PremiumAccessRuntime()

    private var snapshot: PremiumAccessSnapshot

    public init(initial: PremiumAccessSnapshot = PremiumAccessPolicy.current) {
        snapshot = initial
    }

    public func current() -> PremiumAccessSnapshot {
        snapshot
    }

    public func update(_ value: PremiumAccessSnapshot) {
        snapshot = value
    }

    public func requireMediaCenters() throws {
        try snapshot.requireMediaCenters()
    }
}
