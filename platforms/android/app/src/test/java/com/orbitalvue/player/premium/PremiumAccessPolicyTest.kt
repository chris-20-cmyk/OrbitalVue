package com.orbitalvue.player.premium

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class PremiumAccessPolicyTest {
    @Test
    fun `personal build includes media centers without a receipt`() {
        val access = PremiumAccessPolicy.evaluate("personal", hasVerifiedStorePurchase = false)

        assertTrue(access.canUseMediaCenters)
        assertEquals(PremiumAccessState.Included, access.accessState)
        assertEquals("not-required", access.receiptVerification)
        assertNull(access.productId)
    }

    @Test
    fun `store build fails closed until both purchase and product are verified`() {
        assertFalse(PremiumAccessPolicy.evaluate("store", false).canUseMediaCenters)
        assertFalse(PremiumAccessPolicy.evaluate("store", true).canUseMediaCenters)
        assertFalse(PremiumAccessPolicy.evaluate("unknown-mode", true, "valid.product").canUseMediaCenters)
        assertFalse(PremiumAccessPolicy.evaluate("store", true, "premium.\u00E9").canUseMediaCenters)

        val verified = PremiumAccessPolicy.evaluate(
            distributionMode = "store",
            hasVerifiedStorePurchase = true,
            productId = "com.orbitalvue.personal_media_centers"
        )
        assertTrue(verified.canUseMediaCenters)
        assertEquals(PremiumAccessState.Verified, verified.accessState)
        assertEquals("one-time", verified.acquisition)
    }
}
