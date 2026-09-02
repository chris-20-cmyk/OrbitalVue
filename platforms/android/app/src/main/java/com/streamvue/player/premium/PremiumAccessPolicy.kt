package com.streamvue.player.premium

import com.streamvue.player.BuildConfig

enum class StreamVueDistributionMode {
    Personal,
    Store,
    Unknown
}

enum class PremiumAccessState {
    Included,
    Verified,
    Unavailable
}

data class PremiumAccessSnapshot(
    val contractVersion: String,
    val featureId: String,
    val distributionMode: StreamVueDistributionMode,
    val accessState: PremiumAccessState,
    val acquisition: String,
    val receiptVerification: String,
    val productId: String? = null
) {
    val canUseMediaCenters: Boolean
        get() = accessState == PremiumAccessState.Included || accessState == PremiumAccessState.Verified

    val badgeText: String
        get() = when (accessState) {
            PremiumAccessState.Included -> "PERSONAL BUILD • INCLUDED"
            PremiumAccessState.Verified -> "PREMIUM • VERIFIED"
            PremiumAccessState.Unavailable -> "PREMIUM • STORE LOCKED"
        }

    val explanation: String
        get() = when (accessState) {
            PremiumAccessState.Included -> "Plex and Emby are included in this personal build."
            PremiumAccessState.Verified -> "A one-time store purchase was verified for this device account."
            PremiumAccessState.Unavailable ->
                "A verified one-time store purchase is required. Store purchase verification is not connected in this build."
        }

    fun requireMediaCenters() {
        check(canUseMediaCenters) { explanation }
    }
}

object PremiumAccessPolicy {
    const val ContractVersion = "1.0"
    const val MediaCentersFeatureId = "personal-media-centers"

    fun current(): PremiumAccessSnapshot = evaluate(
        distributionMode = BuildConfig.DISTRIBUTION_MODE,
        hasVerifiedStorePurchase = false
    )

    fun evaluate(
        distributionMode: String?,
        hasVerifiedStorePurchase: Boolean,
        productId: String? = null
    ): PremiumAccessSnapshot {
        val mode = when (distributionMode?.trim()?.lowercase()) {
            "personal" -> StreamVueDistributionMode.Personal
            "store" -> StreamVueDistributionMode.Store
            else -> StreamVueDistributionMode.Unknown
        }
        if (mode == StreamVueDistributionMode.Personal) {
            return PremiumAccessSnapshot(
                contractVersion = ContractVersion,
                featureId = MediaCentersFeatureId,
                distributionMode = mode,
                accessState = PremiumAccessState.Included,
                acquisition = "included",
                receiptVerification = "not-required"
            )
        }

        val normalizedProductId = productId?.trim()?.takeIf(::isValidProductId)
        if (mode == StreamVueDistributionMode.Store &&
            hasVerifiedStorePurchase &&
            normalizedProductId != null
        ) {
            return PremiumAccessSnapshot(
                contractVersion = ContractVersion,
                featureId = MediaCentersFeatureId,
                distributionMode = mode,
                accessState = PremiumAccessState.Verified,
                acquisition = "one-time",
                receiptVerification = "verified",
                productId = normalizedProductId
            )
        }

        return PremiumAccessSnapshot(
            contractVersion = ContractVersion,
            featureId = MediaCentersFeatureId,
            distributionMode = mode,
            accessState = PremiumAccessState.Unavailable,
            acquisition = "one-time",
            receiptVerification = "unavailable"
        )
    }

    private fun isValidProductId(value: String): Boolean =
        value.length in 3..256 && value.all {
            it in 'a'..'z' || it in 'A'..'Z' || it in '0'..'9' || it == '.' || it == '_' || it == '-'
        }
}
