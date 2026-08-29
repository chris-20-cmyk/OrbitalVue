import { describe, expect, it } from "vitest";
import { evaluatePremiumAccess } from "../src/premium/PremiumAccess.js";

describe("television premium access", () => {
  it("includes personal media centers without a purchase", () => {
    const access = evaluatePremiumAccess("personal", false);
    expect(access.canUseMediaCenters).toBe(true);
    expect(access.accessState).toBe("included");
    expect(access.receiptVerification).toBe("not-required");
    expect(access.productId).toBeUndefined();
  });

  it("fails store and unknown modes closed until a real product is verified", () => {
    expect(evaluatePremiumAccess("store", false).canUseMediaCenters).toBe(false);
    expect(evaluatePremiumAccess("store", true).canUseMediaCenters).toBe(false);
    expect(evaluatePremiumAccess("typo", true, "valid.product").canUseMediaCenters).toBe(false);

    const verified = evaluatePremiumAccess("store", true, "com.streamvue.personal-media-centers");
    expect(verified.canUseMediaCenters).toBe(true);
    expect(verified.accessState).toBe("verified");
    expect(verified.acquisition).toBe("one-time");
  });
});
