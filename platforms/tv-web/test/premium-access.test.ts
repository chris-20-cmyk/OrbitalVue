import { describe, expect, it } from "vitest";
import { evaluatePremiumAccess } from "../src/premium/PremiumAccess.js";
import {
  SamsungCheckoutPremiumService,
  createTelevisionPremiumService,
  normalizeSamsungCheckoutConfig,
  type SamsungCheckoutApis,
  type SamsungCheckoutConfig
} from "../src/premium/TelevisionPremiumService.js";

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

    const verified = evaluatePremiumAccess("store", true, "com.orbitalvue.personal-media-centers");
    expect(verified.canUseMediaCenters).toBe(true);
    expect(verified.accessState).toBe("verified");
    expect(verified.acquisition).toBe("one-time");
  });
});

describe("television premium store adapters", () => {
  const config: SamsungCheckoutConfig = {
    appId: "1234567890",
    productId: "orbitalvue-premium",
    verificationUrl: "https://entitlements.orbitalvue.test/samsung/status"
  };

  it("validates Samsung seller identifiers and a clean HTTPS verifier URL", () => {
    expect(normalizeSamsungCheckoutConfig(config)).toEqual(config);
    expect(normalizeSamsungCheckoutConfig({
      ...config,
      productId: "this-product-id-is-over-twenty-bytes"
    })).toBeNull();
    expect(normalizeSamsungCheckoutConfig({
      ...config,
      appId: "a".repeat(31)
    })).toBeNull();
    expect(normalizeSamsungCheckoutConfig({
      ...config,
      verificationUrl: "https://entitlements.orbitalvue.test/status?unlock=true"
    })).toBeNull();
    expect(normalizeSamsungCheckoutConfig({
      ...config,
      verificationUrl: "http://entitlements.orbitalvue.test/status"
    })).toBeNull();
  });

  it("keeps LG store builds explicitly locked because LG native billing is discontinued", async () => {
    let requestCount = 0;
    const service = createTelevisionPremiumService({
      platform: "lg-webos",
      distributionMode: "store",
      fetcher: async () => {
        requestCount += 1;
        throw new Error("LG must not call a placeholder verifier");
      }
    });

    await service.start();

    expect(service.snapshot.provider).toBe("lg-third-party");
    expect(service.snapshot.status).toBe("unavailable");
    expect(service.snapshot.canBuy).toBe(false);
    expect(service.snapshot.canRestore).toBe(false);
    expect(service.snapshot.access.canUseMediaCenters).toBe(false);
    expect(service.snapshot.message).toContain("no longer provides a native TV billing service");
    expect(requestCount).toBe(0);
  });

  it("does not call Samsung APIs or a verifier when seller configuration is incomplete", async () => {
    let requestCount = 0;
    let nativePurchaseCount = 0;
    const service = new SamsungCheckoutPremiumService(
      null,
      samsungApis(() => { nativePurchaseCount += 1; }),
      async () => {
        requestCount += 1;
        throw new Error("unconfigured builds must not reach a verifier");
      }
    );

    await service.start();

    expect(service.snapshot.access.canUseMediaCenters).toBe(false);
    expect(service.snapshot.canBuy).toBe(false);
    expect(requestCount).toBe(0);
    expect(nativePurchaseCount).toBe(0);
  });

  it("loads an exact localized Samsung offer and sends the versioned status contract", async () => {
    const requests: unknown[] = [];
    const service = new SamsungCheckoutPremiumService(
      config,
      samsungApis(),
      verifier([unverifiedDecision()], requests)
    );

    await service.start();

    expect(service.snapshot.status).toBe("available");
    expect(service.snapshot.canBuy).toBe(true);
    expect(service.snapshot.localizedPrice).toBe("$9.99");
    expect(requests).toEqual([{
      schemaVersion: 1,
      platform: "samsung",
      action: "status",
      appId: config.appId,
      productId: config.productId,
      customId: "samsung-account-user",
      countryCode: "US"
    }]);
  });

  it("never trusts the Samsung purchase callback without server-side history verification", async () => {
    let payment: Record<string, unknown> | null = null;
    const service = new SamsungCheckoutPremiumService(
      config,
      samsungApis((details, success) => {
        payment = JSON.parse(details) as Record<string, unknown>;
        success({ payResult: "SUCCESS", payDetail: "untrusted-native-result" });
      }),
      verifier([unverifiedDecision(), unverifiedDecision()])
    );

    await service.start();
    await service.purchase();

    expect(payment).toEqual({
      OrderItemID: config.productId,
      OrderTitle: "OrbitalVue Personal Media Centers",
      OrderTotal: "9.99",
      OrderCurrencyID: "USD",
      OrderCustomID: "samsung-account-user"
    });
    expect(service.snapshot.access.canUseMediaCenters).toBe(false);
    expect(service.snapshot.status).toBe("available");
    expect(service.snapshot.message).toContain("has not verified");
  });

  it("does not offer a purchase when DPI says Checkout is unavailable in the service country", async () => {
    let nativePurchaseCount = 0;
    const service = new SamsungCheckoutPremiumService(
      config,
      samsungApis(() => { nativePurchaseCount += 1; }),
      verifier([unverifiedDecision(false)])
    );

    await service.start();

    expect(service.snapshot.status).toBe("unavailable");
    expect(service.snapshot.canBuy).toBe(false);
    expect(service.snapshot.canRestore).toBe(true);
    expect(service.snapshot.access.canUseMediaCenters).toBe(false);
    expect(service.snapshot.message).toContain("service country");
    await expect(service.purchase()).rejects.toThrow("service country");
    expect(nativePurchaseCount).toBe(0);
  });

  it("rechecks native Billing availability immediately before purchase", async () => {
    let nativePurchaseCount = 0;
    const service = new SamsungCheckoutPremiumService(
      config,
      samsungApis(() => { nativePurchaseCount += 1; }, [true, false]),
      verifier([unverifiedDecision()])
    );

    await service.start();
    expect(service.snapshot.canBuy).toBe(true);

    await expect(service.purchase()).rejects.toThrow("became unavailable");
    expect(service.snapshot.status).toBe("unavailable");
    expect(service.snapshot.canRestore).toBe(true);
    expect(nativePurchaseCount).toBe(0);
  });

  it("uses the required DPI country decision when a newer TV omits the deprecated native probe", async () => {
    const apis = samsungApis();
    if (apis.billing) delete apis.billing.isServiceAvailable;
    const service = new SamsungCheckoutPremiumService(
      config,
      apis,
      verifier([unverifiedDecision()])
    );

    await service.start();

    expect(service.snapshot.status).toBe("available");
    expect(service.snapshot.canBuy).toBe(true);
  });

  it("restores verified ownership even where new Checkout purchases are unavailable", async () => {
    const service = new SamsungCheckoutPremiumService(
      config,
      samsungApis(),
      verifier([verifiedDecision(false)])
    );

    await service.start();

    expect(service.snapshot.status).toBe("verified");
    expect(service.snapshot.access.canUseMediaCenters).toBe(true);
    expect(service.snapshot.canBuy).toBe(false);
    expect(service.snapshot.canRestore).toBe(true);
  });

  it("unlocks only after verification and locks again when Samsung history revokes ownership", async () => {
    const transitions: Array<{ status: string; allowed: boolean }> = [];
    const service = new SamsungCheckoutPremiumService(
      config,
      samsungApis((_details, success) => success({ payResult: "SUCCESS" })),
      verifier([unverifiedDecision(), verifiedDecision(), unverifiedDecision()])
    );
    service.subscribe((snapshot) => transitions.push({
      status: snapshot.status,
      allowed: snapshot.access.canUseMediaCenters
    }));

    await service.start();
    await service.purchase();
    expect(service.snapshot.access.canUseMediaCenters).toBe(true);
    expect(service.snapshot.access.productId).toBe(config.productId);

    transitions.length = 0;
    await service.refresh();

    expect(transitions[0]).toEqual({ status: "checking", allowed: true });
    expect(service.snapshot.status).toBe("available");
    expect(service.snapshot.access.canUseMediaCenters).toBe(false);
  });

  it("rejects mismatched or secret-bearing verifier responses", async () => {
    const service = new SamsungCheckoutPremiumService(
      config,
      samsungApis(),
      verifier([{
        ...verifiedDecision() as Record<string, unknown>,
        securityKey: "must-never-be-returned"
      }])
    );

    await service.start();

    expect(service.snapshot.status).toBe("error");
    expect(service.snapshot.access.canUseMediaCenters).toBe(false);
    expect(service.snapshot.message).toContain("invalid or mismatched decision");
  });

  function samsungApis(
    buy?: (
      paymentDetails: string,
      success: (data: { payResult?: string; payDetail?: string }) => void
    ) => void,
    serviceAvailability: boolean[] = []
  ): SamsungCheckoutApis {
    return {
      billing: {
        isServiceAvailable: (serverType, success) => {
          expect(serverType).toBe("PRD");
          const available = serviceAvailability.shift() ?? true;
          success({
            apiResult: JSON.stringify({
              status: "100000",
              result: "Success",
              serviceYn: available ? "Y" : "N"
            })
          });
        },
        buyItem: (_appId, serverType, paymentDetails, success) => {
          expect(serverType).toBe("PRD");
          if (buy) buy(paymentDetails, success);
          else success({ payResult: "SUCCESS" });
        }
      },
      productinfo: {
        ProductInfoConfigKey: { CONFIG_KEY_SERVICE_COUNTRY: "service-country" },
        getSystemConfig: () => "us"
      },
      sso: { getLoginUid: () => "samsung-account-user" }
    };
  }

  function verifier(decisions: unknown[], requests: unknown[] = []): typeof fetch {
    return (async (_input: RequestInfo | URL, init?: RequestInit) => {
      requests.push(JSON.parse(String(init?.body)) as unknown);
      const decision = decisions.shift();
      if (!decision) throw new Error("No verifier decision was queued.");
      return new Response(JSON.stringify(decision), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      });
    }) as typeof fetch;
  }

  function unverifiedDecision(checkoutAvailable = true): unknown {
    const decision: Record<string, unknown> = {
      schemaVersion: 1,
      verified: false,
      checkoutAvailable,
      productId: config.productId
    };
    if (checkoutAvailable) {
      decision.product = {
        productId: config.productId,
        title: "OrbitalVue Personal Media Centers",
        localizedPrice: "$9.99",
        orderTotal: "9.99",
        currencyId: "USD"
      };
    }
    return decision;
  }

  function verifiedDecision(checkoutAvailable = true): unknown {
    return {
      ...unverifiedDecision(checkoutAvailable) as Record<string, unknown>,
      verified: true
    };
  }
});
