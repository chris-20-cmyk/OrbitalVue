package com.streamvue.player.premium

import com.google.gson.Gson
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class PremiumBillingStateTest {
    @Test
    fun `store billing requires an exact product and a clean https verifier endpoint`() {
        val ready = PremiumBillingConfiguration.evaluate(
            distributionMode = "store",
            productId = "com.example.streamvue.premium",
            verificationUrl = "https://billing.example.com/google-play/verify"
        )
        assertTrue(ready.isReadyForPurchases)

        val queryEndpoint = PremiumBillingConfiguration.evaluate(
            "store",
            "com.example.streamvue.premium",
            "https://billing.example.com/verify?token=blocked"
        )
        assertNull(queryEndpoint.verificationEndpoint)
        assertFalse(queryEndpoint.isReadyForPurchases)

        val unicodeProduct = PremiumBillingConfiguration.evaluate(
            "store",
            "premium.é",
            "https://billing.example.com/verify"
        )
        assertNull(unicodeProduct.productId)
    }

    @Test
    fun `backend verifier sends the token only to transport and requires a matching response`() = runBlocking {
        var captured: PremiumVerificationRequest? = null
        val verifier = BackendPremiumPurchaseVerifier("com.streamvue.player") { request ->
            captured = request
            PremiumVerificationResponse(
                schemaVersion = 1,
                verified = true,
                productId = request.productId
            )
        }

        assertTrue(verifier.verify("premium.once", "purchase-token"))
        assertEquals("google-play", captured?.platform)
        assertEquals("com.streamvue.player", captured?.packageName)
        assertEquals("purchase-token", captured?.purchaseToken)

        val mismatch = BackendPremiumPurchaseVerifier("com.streamvue.player") {
            PremiumVerificationResponse(1, true, "different.product")
        }
        assertFalse(mismatch.verify("premium.once", "purchase-token"))
    }

    @Test
    fun `verification wire contract uses stable field names`() {
        val gson = Gson()
        val request = PremiumVerificationRequest(
            packageName = "com.streamvue.player",
            productId = "premium.once",
            purchaseToken = "transient-token"
        )

        assertEquals(
            setOf("schemaVersion", "platform", "packageName", "productId", "purchaseToken"),
            gson.toJsonTree(request).asJsonObject.keySet()
        )
        val response = gson.fromJson(
            """{"schemaVersion":1,"verified":true,"productId":"premium.once"}""",
            PremiumVerificationResponse::class.java
        )
        assertEquals(1, response.schemaVersion)
        assertTrue(response.verified)
        assertEquals("premium.once", response.productId)
    }

    @Test
    fun `verification response rejects extra or mistyped fields`() {
        val valid = parsePremiumVerificationResponse(
            """{"schemaVersion":1,"verified":true,"productId":"premium.once"}"""
        )
        assertTrue(valid.verified)

        val extra = runCatching {
            parsePremiumVerificationResponse(
                """{"schemaVersion":1,"verified":true,"productId":"premium.once","purchaseToken":"leak"}"""
            )
        }
        assertTrue(extra.isFailure)

        val mistyped = runCatching {
            parsePremiumVerificationResponse(
                """{"schemaVersion":1,"verified":"true","productId":"premium.once"}"""
            )
        }
        assertTrue(mistyped.isFailure)

        val fractionalVersion = runCatching {
            parsePremiumVerificationResponse(
                """{"schemaVersion":1.5,"verified":true,"productId":"premium.once"}"""
            )
        }
        assertTrue(fractionalVersion.isFailure)
    }
}
