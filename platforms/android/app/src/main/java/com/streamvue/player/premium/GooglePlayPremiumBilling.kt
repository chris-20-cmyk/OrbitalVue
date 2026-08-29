package com.streamvue.player.premium

import android.app.Activity
import android.content.Context
import com.android.billingclient.api.AcknowledgePurchaseParams
import com.android.billingclient.api.BillingClient
import com.android.billingclient.api.BillingClientStateListener
import com.android.billingclient.api.BillingFlowParams
import com.android.billingclient.api.BillingResult
import com.android.billingclient.api.PendingPurchasesParams
import com.android.billingclient.api.ProductDetails
import com.android.billingclient.api.Purchase
import com.android.billingclient.api.PurchasesUpdatedListener
import com.android.billingclient.api.QueryProductDetailsParams
import com.android.billingclient.api.QueryPurchasesParams
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException
import kotlinx.coroutines.suspendCancellableCoroutine

class GooglePlayPremiumBilling(
    context: Context,
    private val configuration: PremiumBillingConfiguration = PremiumBillingConfiguration.current(),
    verifier: BackendPremiumPurchaseVerifier? = configuration.verificationEndpoint?.let { endpoint ->
        BackendPremiumPurchaseVerifier(
            packageName = context.packageName,
            transport = HttpsPremiumVerificationTransport(endpoint)
        )
    }
) : PurchasesUpdatedListener {
    private val applicationContext = context.applicationContext
    private val verifier = verifier
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val mutableState = MutableStateFlow(PremiumBillingState.initial())
    private var billingClient: BillingClient? = null
    private var isConnecting = false
    private var isConnected = false

    val state: StateFlow<PremiumBillingState> = mutableState.asStateFlow()

    fun start() {
        if (!configuration.isStoreBuild || configuration.productId == null) return
        if (isConnected) {
            refresh()
            return
        }
        if (isConnecting) return
        isConnecting = true
        mutableState.update { it.copy(isBusy = true, message = "Connecting to Google Play…") }
        val client = billingClient ?: BillingClient.newBuilder(applicationContext)
            .setListener(this)
            .enablePendingPurchases(
                PendingPurchasesParams.newBuilder().enableOneTimeProducts().build()
            )
            .enableAutoServiceReconnection()
            .build()
            .also { billingClient = it }
        client.startConnection(object : BillingClientStateListener {
            override fun onBillingSetupFinished(result: BillingResult) {
                isConnecting = false
                isConnected = result.responseCode == BillingClient.BillingResponseCode.OK
                if (!isConnected) {
                    showBillingFailure("Google Play is unavailable", result)
                    return
                }
                scope.launch {
                    refreshOffering()
                    if (configuration.isReadyForPurchases) refreshPurchasesInternal()
                }
            }

            override fun onBillingServiceDisconnected() {
                isConnected = false
                mutableState.update {
                    it.copy(
                        isBusy = false,
                        canPurchase = false,
                        message = "Google Play disconnected. StreamVue will reconnect when you try again."
                    )
                }
            }
        })
    }

    fun refresh() {
        if (!configuration.isStoreBuild || configuration.productId == null) return
        if (!isConnected) {
            start()
            return
        }
        scope.launch {
            refreshOffering()
            if (configuration.isReadyForPurchases) refreshPurchasesInternal()
        }
    }

    fun launchPurchase(activity: Activity) {
        if (!configuration.isReadyForPurchases || verifier == null) {
            mutableState.update {
                it.copy(
                    isBusy = false,
                    canPurchase = false,
                    message = "A real product and secure Google Play verification service are required before purchasing."
                )
            }
            return
        }
        if (!isConnected) {
            start()
            return
        }
        scope.launch {
            mutableState.update { it.copy(isBusy = true, canPurchase = false, message = "Opening Google Play…") }
            runCatching { queryImmediateOffer() }
                .onSuccess { offer ->
                    val productParams = BillingFlowParams.ProductDetailsParams.newBuilder()
                        .setProductDetails(offer.product)
                        .setOfferToken(offer.offerToken)
                        .build()
                    val result = billingClient!!.launchBillingFlow(
                        activity,
                        BillingFlowParams.newBuilder()
                            .setProductDetailsParamsList(listOf(productParams))
                            .build()
                    )
                    if (result.responseCode != BillingClient.BillingResponseCode.OK) {
                        showBillingFailure("Google Play could not open the purchase", result)
                    }
                }
                .onFailure { showFailure(it.message ?: "The Google Play product is unavailable.") }
        }
    }

    fun restorePurchases() {
        if (!configuration.isReadyForPurchases || verifier == null) {
            showFailure("Secure Google Play purchase verification is not configured.")
            return
        }
        if (!isConnected) {
            start()
            return
        }
        scope.launch {
            mutableState.update { it.copy(isBusy = true, message = "Restoring Google Play purchases…") }
            refreshPurchasesInternal(restoring = true)
        }
    }

    fun close() {
        billingClient?.endConnection()
        billingClient = null
        isConnected = false
        isConnecting = false
        scope.cancel()
    }

    override fun onPurchasesUpdated(result: BillingResult, purchases: List<Purchase>?) {
        when {
            result.responseCode == BillingClient.BillingResponseCode.OK && purchases != null ->
                scope.launch { processPurchases(purchases, restoring = false) }
            result.responseCode == BillingClient.BillingResponseCode.USER_CANCELED ->
                mutableState.update {
                    it.copy(
                        isBusy = false,
                        canPurchase = it.localizedPrice != null && configuration.isReadyForPurchases,
                        message = "Purchase canceled. Nothing was charged."
                    )
                }
            else -> showBillingFailure("Google Play did not complete the purchase", result)
        }
    }

    private suspend fun refreshOffering() {
        runCatching { queryImmediateOffer() }
            .onSuccess { offer ->
                val ready = configuration.isReadyForPurchases && verifier != null
                mutableState.update {
                    it.copy(
                        configuredProductId = configuration.productId,
                        productTitle = offer.product.name,
                        localizedPrice = offer.formattedPrice,
                        isBusy = false,
                        canPurchase = ready && !it.access.canUseMediaCenters,
                        canRestore = ready,
                        message = if (ready) {
                            "Buy once from Google Play or restore a purchase already owned by this account."
                        } else {
                            "The product exists, but secure purchase verification is not configured for this build."
                        }
                    )
                }
            }
            .onFailure { showFailure(it.message ?: "The configured Google Play product is unavailable.") }
    }

    private suspend fun refreshPurchasesInternal(restoring: Boolean = false) {
        val client = billingClient ?: return
        val result = suspendCancellableCoroutine<Pair<BillingResult, List<Purchase>>> { continuation ->
            client.queryPurchasesAsync(
                QueryPurchasesParams.newBuilder()
                    .setProductType(BillingClient.ProductType.INAPP)
                    .build()
            ) { billingResult, purchases ->
                if (continuation.isActive) continuation.resume(billingResult to purchases)
            }
        }
        if (result.first.responseCode != BillingClient.BillingResponseCode.OK) {
            showBillingFailure("Google Play could not restore purchases", result.first)
            return
        }
        processPurchases(result.second, restoring)
    }

    private suspend fun processPurchases(purchases: List<Purchase>, restoring: Boolean) {
        val productId = configuration.productId ?: return
        val matching = purchases.filter { productId in it.products }
        val purchased = matching.firstOrNull { it.purchaseState == Purchase.PurchaseState.PURCHASED }
        if (purchased == null) {
            val pending = matching.any { it.purchaseState == Purchase.PurchaseState.PENDING }
            lockAccess(
                if (pending) "Purchase pending. Premium stays locked until Google Play confirms payment."
                else if (restoring) "No verified premium purchase was found for this Google Play account."
                else "No verified Google Play purchase is currently owned."
            )
            return
        }

        val activeVerifier = verifier
        if (activeVerifier == null) {
            lockAccess("The purchase was found but secure verification is not configured.")
            return
        }
        mutableState.update { it.copy(isBusy = true, canPurchase = false, message = "Verifying purchase securely…") }
        val verified = runCatching {
            activeVerifier.verify(productId, purchased.purchaseToken)
        }.getOrElse {
            lockAccess(it.message ?: "The verification service could not verify this purchase.")
            return
        }
        if (!verified) {
            lockAccess("Google Play could not verify ownership of this premium purchase.")
            return
        }
        if (!purchased.isAcknowledged) {
            val acknowledgement = acknowledge(purchased.purchaseToken)
            if (acknowledgement.responseCode != BillingClient.BillingResponseCode.OK) {
                showBillingFailure("Google Play could not acknowledge the verified purchase", acknowledgement)
                return
            }
        }

        val access = PremiumAccessPolicy.evaluate("store", true, productId)
        mutableState.update {
            it.copy(
                access = access,
                configuredProductId = productId,
                isBusy = false,
                canPurchase = false,
                canRestore = true,
                message = if (restoring) "Premium purchase restored from Google Play."
                else "Premium media centers unlocked with a verified one-time purchase."
            )
        }
    }

    private suspend fun acknowledge(purchaseToken: String): BillingResult {
        val client = billingClient ?: error("Google Play is not connected.")
        return suspendCancellableCoroutine { continuation ->
            client.acknowledgePurchase(
                AcknowledgePurchaseParams.newBuilder().setPurchaseToken(purchaseToken).build()
            ) { result ->
                if (continuation.isActive) continuation.resume(result)
            }
        }
    }

    private suspend fun queryImmediateOffer(): ImmediateOffer {
        val productId = configuration.productId ?: error("No Google Play product is configured.")
        val client = billingClient ?: error("Google Play is not connected.")
        val result = suspendCancellableCoroutine<Pair<BillingResult, List<ProductDetails>>> { continuation ->
            val product = QueryProductDetailsParams.Product.newBuilder()
                .setProductId(productId)
                .setProductType(BillingClient.ProductType.INAPP)
                .build()
            client.queryProductDetailsAsync(
                QueryProductDetailsParams.newBuilder().setProductList(listOf(product)).build()
            ) { billingResult, detailsResult ->
                if (continuation.isActive) {
                    continuation.resume(billingResult to detailsResult.productDetailsList)
                }
            }
        }
        if (result.first.responseCode != BillingClient.BillingResponseCode.OK) {
            throw IllegalStateException("Google Play product query failed (${result.first.responseCode}).")
        }
        val details = result.second.singleOrNull { it.productId == productId }
            ?: error("The configured Google Play product was not returned.")
        val offers = details.oneTimePurchaseOfferDetailsList.orEmpty()
            .ifEmpty { listOfNotNull(details.oneTimePurchaseOfferDetails) }
            .filter { it.rentalDetails == null && it.preorderDetails == null }
        val offer = offers.singleOrNull()
            ?: error("Configure exactly one immediate, non-rental purchase option for this product.")
        val offerToken = offer.offerToken?.takeIf(String::isNotBlank)
            ?: error("The configured purchase option did not include a Google Play offer token.")
        return ImmediateOffer(details, offerToken, offer.formattedPrice)
    }

    private fun lockAccess(message: String) {
        val access = PremiumAccessPolicy.evaluate("store", false, configuration.productId)
        mutableState.update {
            it.copy(
                access = access,
                isBusy = false,
                canPurchase = it.localizedPrice != null && configuration.isReadyForPurchases,
                canRestore = configuration.isReadyForPurchases,
                message = message
            )
        }
    }

    private fun showFailure(message: String) {
        mutableState.update {
            it.copy(
                isBusy = false,
                canPurchase = it.localizedPrice != null && configuration.isReadyForPurchases,
                canRestore = configuration.isReadyForPurchases,
                message = message
            )
        }
    }

    private fun showBillingFailure(prefix: String, result: BillingResult) {
        showFailure("$prefix (Google Play code ${result.responseCode}).")
    }

    private data class ImmediateOffer(
        val product: ProductDetails,
        val offerToken: String,
        val formattedPrice: String
    )
}
