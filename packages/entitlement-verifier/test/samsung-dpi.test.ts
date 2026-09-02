import { describe, expect, it } from "vitest";
import {
  SamsungDpiHttpClient,
  createEntitlementVerifierHandler,
  parseSamsungStatusRequest,
  verifySamsungStatus
} from "../src/index.js";

describe("Samsung DPI entitlement verification", () => {
  const securityKey = "dpi-test-security-key";
  const request = parseSamsungStatusRequest({
    schemaVersion: 1,
    platform: "samsung",
    action: "status",
    appId: "1234567890",
    productId: "orbitalvue-premium",
    customId: "samsung-account-user",
    countryCode: "US"
  });

  it("verifies the exact product, signed purchase history, and invoice", async () => {
    const calls: Array<{ path: string; body: Record<string, unknown> }> = [];
    const client = new SamsungDpiHttpClient({
      appId: request.appId,
      productId: request.productId,
      securityKey,
      fetcher: dpiFetcher(calls, {
        product: true,
        invoice: { canceled: false, verify: true }
      })
    });

    const response = await verifySamsungStatus(request, {
      appId: request.appId,
      productId: request.productId
    }, client);

    expect(response.verified).toBe(true);
    expect(response.checkoutAvailable).toBe(true);
    expect(response.product).toMatchObject({
      productId: request.productId,
      title: "OrbitalVue Personal Media Centers",
      orderTotal: "9.99",
      currencyId: "USD"
    });
    expect(calls).toHaveLength(3);
    expect(calls.slice(0, 2).map((call) => call.path).sort()).toEqual([
      "/openapi/cont/list",
      "/openapi/invoice/list"
    ]);
    expect(calls[2]?.path).toBe("/openapi/invoice/verify");
    expect(calls.find((call) => call.path === "/openapi/invoice/list")?.body.CustomID).toBe(request.customId);
    expect(JSON.stringify(response)).not.toContain(request.customId);
    expect(JSON.stringify(response)).not.toContain(securityKey);
    expect(JSON.stringify(response)).not.toContain("CheckValue");
  });

  it("returns no fabricated offer where the exact DPI product is unavailable", async () => {
    const client = new SamsungDpiHttpClient({
      appId: request.appId,
      productId: request.productId,
      securityKey,
      fetcher: dpiFetcher([], { product: false, invoice: null })
    });

    const response = await verifySamsungStatus(request, {
      appId: request.appId,
      productId: request.productId
    }, client);

    expect(response).toEqual({
      schemaVersion: 1,
      verified: false,
      checkoutAvailable: false,
      productId: request.productId
    });
  });

  it("restores verified ownership even when the country has no current sale offer", async () => {
    const client = new SamsungDpiHttpClient({
      appId: request.appId,
      productId: request.productId,
      securityKey,
      fetcher: dpiFetcher([], {
        product: false,
        invoice: { canceled: false, verify: true }
      })
    });

    const response = await verifySamsungStatus(request, {
      appId: request.appId,
      productId: request.productId
    }, client);

    expect(response).toEqual({
      schemaVersion: 1,
      verified: true,
      checkoutAvailable: false,
      productId: request.productId
    });
  });

  it("does not unlock a canceled record or a failed exact invoice verification", async () => {
    for (const invoice of [
      { canceled: true, verify: true },
      { canceled: false, verify: false }
    ]) {
      const client = new SamsungDpiHttpClient({
        appId: request.appId,
        productId: request.productId,
        securityKey,
        fetcher: dpiFetcher([], { product: true, invoice })
      });
      const response = await verifySamsungStatus(request, {
        appId: request.appId,
        productId: request.productId
      }, client);
      expect(response.verified).toBe(false);
    }
  });

  it("fails closed when the DPI response HMAC is not authentic", async () => {
    const fetcher: typeof fetch = async (input) => {
      const path = new URL(String(input)).pathname;
      if (path.endsWith("/cont/list")) {
        return Response.json({
          CPStatus: "100000",
          CPResult: "EOF",
          TotalCount: 0,
          ItemDetails: [],
          CheckValue: "tampered"
        });
      }
      throw new Error("Unexpected call");
    };
    const client = new SamsungDpiHttpClient({
      appId: request.appId,
      productId: request.productId,
      securityKey,
      fetcher
    });
    await expect(client.getProductOffer("US")).rejects.toThrow("HMAC verification failed");
  });

  it("keeps Samsung identifiers and provider errors out of HTTP error responses", async () => {
    const handler = createEntitlementVerifierHandler({
      samsung: {
        config: { appId: request.appId, productId: request.productId },
        dpi: {
          getProductOffer: async () => { throw new Error("custom-id-secret"); },
          findActivePurchase: async () => null,
          verifyInvoice: async () => false
        }
      }
    });
    const response = await handler(new Request("https://verify.example/samsung/status", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request)
    }));
    expect(response.status).toBe(503);
    expect(await response.text()).not.toContain("custom-id-secret");
  });

  function dpiFetcher(
    calls: Array<{ path: string; body: Record<string, unknown> }>,
    scenario: { product: boolean; invoice: { canceled: boolean; verify: boolean } | null }
  ): typeof fetch {
    return async (input, init) => {
      const path = new URL(String(input)).pathname;
      const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
      calls.push({ path, body });
      if (path.endsWith("/cont/list")) {
        expect(body.CheckValue).toBe(await hmac(`${request.appId}${request.countryCode}`));
        const entries = scenario.product ? [{
          ItemID: request.productId,
          ItemTitle: "OrbitalVue Personal Media Centers",
          ItemType: 2,
          Price: "9.99",
          CurrencyID: "USD"
        }] : [];
        return signedList("ItemDetails", entries);
      }
      if (path.endsWith("/invoice/list")) {
        const page = Number(body.PageNumber);
        expect(body.CheckValue).toBe(await hmac(`${request.appId}${request.customId}${request.countryCode}1${page}`));
        const entries = scenario.invoice ? [{
          ItemID: request.productId,
          ItemType: 2,
          InvoiceID: "invoice-123",
          CancelStatus: scenario.invoice.canceled
        }] : [];
        return signedList("InvoiceDetails", entries);
      }
      if (path.endsWith("/invoice/verify")) {
        return Response.json({
          CPStatus: scenario.invoice?.verify ? "100000" : "500000",
          CPResult: scenario.invoice?.verify ? "SUCCESS" : "FAILED",
          AppID: request.appId,
          InvoiceID: body.InvoiceID
        });
      }
      throw new Error(`Unexpected DPI path ${path}`);
    };
  }

  async function signedList(field: "ItemDetails" | "InvoiceDetails", entries: Array<Record<string, unknown>>): Promise<Response> {
    const status = "100000";
    const result = "EOF";
    const count = entries.length;
    const material = `${status}${result}${count}${entries.map((entry) => entry.ItemID).join("")}`;
    return Response.json({
      CPStatus: status,
      CPResult: result,
      TotalCount: count,
      [field]: entries,
      CheckValue: await hmac(material)
    });
  }

  async function hmac(value: string): Promise<string> {
    const key = await crypto.subtle.importKey(
      "raw",
      new TextEncoder().encode(securityKey),
      { name: "HMAC", hash: "SHA-256" },
      false,
      ["sign"]
    );
    const signature = new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(value)));
    let binary = "";
    for (const byte of signature) binary += String.fromCharCode(byte);
    return btoa(binary);
  }
});
