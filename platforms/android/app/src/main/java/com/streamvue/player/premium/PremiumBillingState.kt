package com.streamvue.player.premium

import com.streamvue.player.BuildConfig
import java.net.URI

data class PremiumBillingConfiguration(
    val distributionMode: String,
    val productId: String?,
    val verificationEndpoint: URI?
) {
    val isStoreBuild: Boolean
        get() = distributionMode.equals("store", ignoreCase = true)

    val isReadyForPurchases: Boolean
        get() = isStoreBuild && productId != null && verificationEndpoint != null

    companion object {
        fun current(): PremiumBillingConfiguration = evaluate(
            BuildConfig.DISTRIBUTION_MODE,
            BuildConfig.PREMIUM_PRODUCT_ID,
            BuildConfig.PREMIUM_VERIFICATION_URL
        )

        fun evaluate(
            distributionMode: String?,
            productId: String?,
            verificationUrl: String?
        ): PremiumBillingConfiguration {
            val normalizedMode = distributionMode?.trim()?.lowercase() ?: "unknown"
            val normalizedProductId = productId?.trim()?.takeIf {
                it.length in 3..256 && it.all { character ->
                    character in 'a'..'z' || character in 'A'..'Z' ||
                        character in '0'..'9' || character == '.' || character == '_' || character == '-'
                }
            }
            val endpoint = verificationUrl?.trim()?.takeIf(String::isNotEmpty)?.let { value ->
                runCatching { URI(value) }.getOrNull()?.takeIf { uri ->
                    uri.scheme.equals("https", ignoreCase = true) &&
                        !uri.host.isNullOrBlank() && uri.userInfo == null &&
                        uri.query == null && uri.fragment == null
                }
            }
            return PremiumBillingConfiguration(normalizedMode, normalizedProductId, endpoint)
        }
    }
}

data class PremiumBillingState(
    val access: PremiumAccessSnapshot,
    val configuredProductId: String? = null,
    val productTitle: String? = null,
    val localizedPrice: String? = null,
    val isBusy: Boolean = false,
    val canPurchase: Boolean = false,
    val canRestore: Boolean = false,
    val message: String = access.explanation
) {
    companion object {
        fun initial(
            access: PremiumAccessSnapshot = PremiumAccessPolicy.current(),
            configuration: PremiumBillingConfiguration = PremiumBillingConfiguration.current()
        ): PremiumBillingState {
            if (access.canUseMediaCenters) return PremiumBillingState(access = access)
            val message = when {
                configuration.productId == null ->
                    "The one-time Google Play product has not been configured for this build."
                configuration.verificationEndpoint == null ->
                    "Secure Google Play purchase verification has not been configured for this build."
                else -> "Checking Google Play for the one-time premium product…"
            }
            return PremiumBillingState(
                access = access,
                configuredProductId = configuration.productId,
                isBusy = configuration.isReadyForPurchases,
                canRestore = configuration.isReadyForPurchases,
                message = message
            )
        }
    }
}
