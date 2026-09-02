import { describe, expect, it, vi } from "vitest";
import {
  GooglePlayPublisherHttpClient,
  createEntitlementVerifierHandler,
  createGoogleServiceAccountTokenProvider,
  isPurchasedProduct,
  parseGooglePlayRequest,
  verifyGooglePlayPurchase,
  type ProductPurchaseV2
} from "../src/index.js";

describe("Google Play entitlement verification", () => {
  const purchased: ProductPurchaseV2 = {
    purchaseStateContext: { purchaseState: "PURCHASED" },
    purchaseCompletionTime: "2026-08-29T12:00:00Z",
    productLineItem: [{
      productId: "orbitalvue.premium",
      productOfferDetails: {
        quantity: 1,
        refundableQuantity: 1,
        consumptionState: "YET_TO_BE_CONSUMED"
      }
    }]
  };

  it("accepts only a completed, matching, unconsumed purchase", () => {
    expect(isPurchasedProduct(purchased, "orbitalvue.premium")).toBe(true);
    expect(isPurchasedProduct({ ...purchased, purchaseStateContext: { purchaseState: "PENDING" } }, "orbitalvue.premium")).toBe(false);
    expect(isPurchasedProduct({ ...purchased, testPurchaseContext: { fopType: "TEST" } }, "orbitalvue.premium")).toBe(false);
    expect(isPurchasedProduct({ ...purchased, testPurchaseContext: { fopType: "TEST" } }, "orbitalvue.premium", true)).toBe(true);
    expect(isPurchasedProduct({ ...purchased, testPurchaseContext: {} }, "orbitalvue.premium", true)).toBe(false);
    expect(isPurchasedProduct({
      ...purchased,
      productLineItem: [{
        productId: "orbitalvue.premium",
        productOfferDetails: { refundableQuantity: 1, consumptionState: "YET_TO_BE_CONSUMED" }
      }]
    }, "orbitalvue.premium")).toBe(false);
    expect(isPurchasedProduct({
      ...purchased,
      productLineItem: [{
        productId: "orbitalvue.premium",
        productOfferDetails: { refundableQuantity: 0 }
      }]
    }, "orbitalvue.premium")).toBe(false);
    expect(isPurchasedProduct({
      ...purchased,
      productLineItem: [{
        productId: "orbitalvue.premium",
        productOfferDetails: { refundableQuantity: 1, consumptionState: "CONSUMED" }
      }]
    }, "orbitalvue.premium")).toBe(false);
  });

  it("rejects an identity mismatch before sending the purchase token upstream", async () => {
    const publisher = { getProductPurchase: vi.fn(async () => purchased) };
    const request = parseGooglePlayRequest({
      schemaVersion: 1,
      platform: "google-play",
      packageName: "com.attacker.player",
      productId: "orbitalvue.premium",
      purchaseToken: "transient-secret"
    });

    await expect(verifyGooglePlayPurchase(request, {
      packageName: "com.orbitalvue.player",
      productId: "orbitalvue.premium"
    }, publisher)).rejects.toThrow("does not match");
    expect(publisher.getProductPurchase).not.toHaveBeenCalled();
  });

  it("calls ProductPurchaseV2 without exposing the access token in the result", async () => {
    const requests: Request[] = [];
    const client = new GooglePlayPublisherHttpClient({
      getAccessToken: async () => "oauth-secret",
      fetcher: async (input, init) => {
        requests.push(new Request(input, init));
        return Response.json(purchased);
      }
    });

    const response = await verifyGooglePlayPurchase({
      schemaVersion: 1,
      platform: "google-play",
      packageName: "com.orbitalvue.player",
      productId: "orbitalvue.premium",
      purchaseToken: "purchase/token+value"
    }, {
      packageName: "com.orbitalvue.player",
      productId: "orbitalvue.premium"
    }, client);

    expect(requests[0]?.url).toContain("/purchases/productsv2/tokens/purchase%2Ftoken%2Bvalue");
    expect(requests[0]?.headers.get("authorization")).toBe("Bearer oauth-secret");
    expect(response).toEqual({ schemaVersion: 1, verified: true, productId: "orbitalvue.premium" });
    expect(JSON.stringify(response)).not.toContain("oauth-secret");
    expect(JSON.stringify(response)).not.toContain("purchase/token");
  });

  it("keeps malformed and upstream failures generic at the HTTP boundary", async () => {
    const handler = createEntitlementVerifierHandler({
      googlePlay: {
        config: { packageName: "com.orbitalvue.player", productId: "orbitalvue.premium" },
        publisher: { getProductPurchase: async () => { throw new Error("upstream included purchase-token-secret"); } }
      }
    });
    const invalid = await handler(new Request("https://verify.example/google-play/verify", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ purchaseToken: "secret" })
    }));
    expect(invalid.status).toBe(400);
    expect(await invalid.json()).toEqual({ schemaVersion: 1, error: "invalid-request" });

    const unavailable = await handler(new Request("https://verify.example/google-play/verify", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        schemaVersion: 1,
        platform: "google-play",
        packageName: "com.orbitalvue.player",
        productId: "orbitalvue.premium",
        purchaseToken: "purchase-token-secret"
      })
    }));
    expect(unavailable.status).toBe(503);
    expect(await unavailable.text()).not.toContain("purchase-token-secret");
    expect(unavailable.headers.get("cache-control")).toContain("no-store");
  });

  it("creates and caches a service-account OAuth assertion without returning the private key", async () => {
    const keys = await crypto.subtle.generateKey(
      { name: "RSASSA-PKCS1-v1_5", modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]), hash: "SHA-256" },
      true,
      ["sign", "verify"]
    );
    const pkcs8 = new Uint8Array(await crypto.subtle.exportKey("pkcs8", keys.privateKey));
    const privateKey = pem(pkcs8);
    const assertions: string[] = [];
    const fetcher: typeof fetch = async (_input, init) => {
      const params = new URLSearchParams(String(init?.body));
      assertions.push(params.get("assertion") ?? "");
      return Response.json({ access_token: "short-lived-oauth-token", token_type: "Bearer", expires_in: 3600 });
    };
    const getAccessToken = createGoogleServiceAccountTokenProvider({
      clientEmail: "billing@orbitalvue-test.iam.gserviceaccount.com",
      privateKey
    }, { fetcher, now: () => 1_787_999_000_000 });

    expect(await getAccessToken()).toBe("short-lived-oauth-token");
    expect(await getAccessToken()).toBe("short-lived-oauth-token");
    expect(assertions).toHaveLength(1);
    const claims = JSON.parse(atob(assertions[0]!.split(".")[1]!.replaceAll("-", "+").replaceAll("_", "/"))) as Record<string, unknown>;
    expect(claims.scope).toBe("https://www.googleapis.com/auth/androidpublisher");
    expect(assertions[0]).not.toContain("PRIVATE KEY");
  });
});

function pem(value: Uint8Array): string {
  let binary = "";
  for (const byte of value) binary += String.fromCharCode(byte);
  const base64 = btoa(binary).match(/.{1,64}/g)?.join("\n") ?? "";
  return `-----BEGIN PRIVATE KEY-----\n${base64}\n-----END PRIVATE KEY-----`;
}
