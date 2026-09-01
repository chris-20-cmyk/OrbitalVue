import { describe, expect, it } from "vitest";
import { SELF } from "cloudflare:test";
import { handleEntitlementVerifierWorkerRequest } from "../src/index.js";

const TEST_HOST = "entitlements.streamvue.test";

describe("StreamVue entitlement verifier Worker boundary", () => {
  it("runs the deployed entry point inside the Workers runtime", async () => {
    const response = await SELF.fetch(`https://${TEST_HOST}/healthz`);

    expect(response.status).toBe(200);
    await expect(response.json()).resolves.toMatchObject({
      service: "streamvue-entitlement-verifier",
      status: "available"
    });
  });

  it("serves a secret-free health response with hardened headers", async () => {
    const response = await handleEntitlementVerifierWorkerRequest(
      new Request(`https://${TEST_HOST}/healthz`),
      workerEnv()
    );

    expect(response.status).toBe(200);
    await expect(response.json()).resolves.toEqual({
      schemaVersion: 1,
      service: "streamvue-entitlement-verifier",
      status: "available"
    });
    expect(response.headers.get("cache-control")).toBe("no-store, max-age=0");
    expect(response.headers.get("access-control-allow-origin")).toBeNull();
    expect(response.headers.get("content-security-policy")).toContain("default-src 'none'");
    expect(response.headers.get("x-content-type-options")).toBe("nosniff");
  });

  it("rejects requests for a different deployment host", async () => {
    const response = await handleEntitlementVerifierWorkerRequest(
      new Request("https://wrong-host.streamvue.test/healthz"),
      workerEnv()
    );

    expect(response.status).toBe(421);
    await expect(response.json()).resolves.toEqual({ schemaVersion: 1, error: "misdirected-request" });
  });

  it("reflects only an exact configured browser origin", async () => {
    const allowed = "https://operations.streamvue.test";
    const accepted = await handleEntitlementVerifierWorkerRequest(
      new Request(`https://${TEST_HOST}/healthz`, { headers: { Origin: allowed } }),
      workerEnv({ ALLOWED_BROWSER_ORIGINS: JSON.stringify([allowed]) })
    );
    const rejected = await handleEntitlementVerifierWorkerRequest(
      new Request(`https://${TEST_HOST}/healthz`, { headers: { Origin: "https://attacker.example" } }),
      workerEnv({ ALLOWED_BROWSER_ORIGINS: JSON.stringify([allowed]) })
    );
    const insecureProductionOrigin = await handleEntitlementVerifierWorkerRequest(
      new Request(`https://${TEST_HOST}/healthz`, { headers: { Origin: "http://operations.streamvue.test" } }),
      workerEnv({
        DEPLOYMENT_ENVIRONMENT: "production",
        ALLOWED_BROWSER_ORIGINS: JSON.stringify(["http://operations.streamvue.test"])
      })
    );

    expect(accepted.status).toBe(200);
    expect(accepted.headers.get("access-control-allow-origin")).toBe(allowed);
    expect(accepted.headers.get("vary")).toContain("Origin");
    expect(rejected.status).toBe(403);
    expect(rejected.headers.get("access-control-allow-origin")).toBeNull();
    expect(insecureProductionOrigin.status).toBe(503);
  });

  it("answers only an allowed browser preflight", async () => {
    const allowed = "https://operations.streamvue.test";
    const response = await handleEntitlementVerifierWorkerRequest(
      new Request(`https://${TEST_HOST}/samsung/status`, {
        method: "OPTIONS",
        headers: {
          Origin: allowed,
          "Access-Control-Request-Method": "POST",
          "Access-Control-Request-Headers": "content-type"
        }
      }),
      workerEnv({ ALLOWED_BROWSER_ORIGINS: JSON.stringify([allowed]) })
    );

    expect(response.status).toBe(204);
    expect(response.headers.get("access-control-allow-origin")).toBe(allowed);
    expect(response.headers.get("access-control-allow-methods")).toBe("POST, OPTIONS");
    expect(response.headers.get("access-control-allow-credentials")).toBeNull();
  });

  it("accepts origin-less native clients and rate-limits on an opaque HMAC key", async () => {
    const keys: string[] = [];
    const env = workerEnv({
      VERIFICATION_RATE_LIMITER: capturingRateLimiter(keys, false)
    });
    const response = await handleEntitlementVerifierWorkerRequest(samsungRequest("account-user-123"), env);

    expect(response.status).toBe(429);
    expect(response.headers.get("retry-after")).toBe("60");
    expect(keys).toHaveLength(1);
    expect(keys[0]).toMatch(/^samsung:[0-9a-f]{64}$/);
    expect(keys[0]).not.toContain("account-user-123");
    expect(keys[0]).not.toContain("streamvue_premium");
  });

  it("stops provider-wide bursts before parsing or purchaser verification", async () => {
    let purchaserChecks = 0;
    const response = await handleEntitlementVerifierWorkerRequest(
      samsungRequest("account-user-123"),
      workerEnv({
        PROVIDER_RATE_LIMITER: capturingRateLimiter([], false),
        VERIFICATION_RATE_LIMITER: {
          async limit(): Promise<RateLimitOutcome> {
            purchaserChecks += 1;
            return { success: true };
          }
        }
      })
    );

    expect(response.status).toBe(429);
    expect(purchaserChecks).toBe(0);
  });

  it("uses stable but unlinkable rate-limit keys per purchaser", async () => {
    const keys: string[] = [];
    const env = workerEnv({
      VERIFICATION_RATE_LIMITER: capturingRateLimiter(keys, false)
    });

    await handleEntitlementVerifierWorkerRequest(samsungRequest("same-account"), env);
    await handleEntitlementVerifierWorkerRequest(samsungRequest("same-account"), env);
    await handleEntitlementVerifierWorkerRequest(samsungRequest("different-account"), env);

    expect(keys[0]).toBe(keys[1]);
    expect(keys[0]).not.toBe(keys[2]);
  });

  it("rejects invalid and oversized requests before touching the purchaser limiter", async () => {
    let calls = 0;
    const env = workerEnv({
      VERIFICATION_RATE_LIMITER: {
        async limit(): Promise<RateLimitOutcome> {
          calls += 1;
          return { success: true };
        }
      }
    });
    const invalid = await handleEntitlementVerifierWorkerRequest(
      jsonRequest("/samsung/status", { schemaVersion: 1, platform: "samsung" }),
      env
    );
    const oversized = await handleEntitlementVerifierWorkerRequest(
      new Request(`https://${TEST_HOST}/samsung/status`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Content-Length": "32769"
        },
        body: "{}"
      }),
      env
    );

    expect(invalid.status).toBe(400);
    expect(oversized.status).toBe(413);
    expect(calls).toBe(0);
  });

  it("keeps placeholder deployments and production test purchases unavailable", async () => {
    const placeholder = await handleEntitlementVerifierWorkerRequest(
      samsungRequest("account-user-123"),
      workerEnv({ SAMSUNG_CHECKOUT_APP_ID: "REPLACE_IN_SELLER_OFFICE" })
    );
    const productionTestMode = await handleEntitlementVerifierWorkerRequest(
      googleRequest(),
      workerEnv({
        DEPLOYMENT_ENVIRONMENT: "production",
        GOOGLE_PLAY_ALLOW_TEST_PURCHASES: "true"
      })
    );

    expect(placeholder.status).toBe(503);
    expect(productionTestMode.status).toBe(503);
  });
});

function workerEnv(overrides: Partial<Env> = {}): Env {
  return {
    VERIFICATION_RATE_LIMITER: capturingRateLimiter([], true),
    PROVIDER_RATE_LIMITER: capturingRateLimiter([], true),
    DEPLOYMENT_ENVIRONMENT: "local",
    EXPECTED_HOSTNAME: TEST_HOST,
    ALLOWED_BROWSER_ORIGINS: "[]",
    GOOGLE_PLAY_PACKAGE_NAME: "com.orbitalvue.player",
    GOOGLE_PLAY_PRODUCT_ID: "streamvue_premium_once",
    GOOGLE_PLAY_ALLOW_TEST_PURCHASES: "false",
    SAMSUNG_CHECKOUT_APP_ID: "StreamVueCheckout",
    SAMSUNG_PREMIUM_PRODUCT_ID: "streamvue_premium",
    GOOGLE_SERVICE_ACCOUNT_EMAIL: "service-account@example.invalid",
    GOOGLE_SERVICE_ACCOUNT_PRIVATE_KEY: "LOCAL_TEST_KEY_ONLY",
    SAMSUNG_DPI_SECURITY_KEY: "LOCAL_TEST_KEY_ONLY",
    RATE_LIMIT_KEY_SECRET: "LOCAL_TEST_RATE_LIMIT_SECRET_AT_LEAST_32_CHARS",
    ...overrides
  };
}

function capturingRateLimiter(keys: string[], success: boolean): RateLimit {
  return {
    async limit(options: RateLimitOptions): Promise<RateLimitOutcome> {
      keys.push(options.key);
      return { success };
    }
  };
}

function samsungRequest(customId: string): Request {
  return jsonRequest("/samsung/status", {
    schemaVersion: 1,
    platform: "samsung",
    action: "status",
    appId: "StreamVueCheckout",
    productId: "streamvue_premium",
    customId,
    countryCode: "US"
  });
}

function googleRequest(): Request {
  return jsonRequest("/google-play/verify", {
    schemaVersion: 1,
    platform: "google-play",
    packageName: "com.orbitalvue.player",
    productId: "streamvue_premium_once",
    purchaseToken: "transient-purchase-token"
  });
}

function jsonRequest(path: string, body: unknown): Request {
  return new Request(`https://${TEST_HOST}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
}
