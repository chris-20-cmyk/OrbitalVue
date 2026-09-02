import {
  assertConfiguredSamsungIdentity,
  isRecord,
  requiredString,
  type SamsungProductOffer,
  type SamsungStatusRequest,
  type SamsungStatusResponse
} from "./contracts.js";
import { readBoundedJson } from "./google-play.js";

const DEFAULT_DPI_ORIGIN = "https://checkoutapi.samsungcheckout.com";
const MAX_DPI_RESPONSE_BYTES = 256 * 1024;
const MAX_PURCHASE_PAGES = 50;
const PRICE_PATTERN = /^(?:0|[1-9]\d{0,11})(?:\.\d{1,2})?$/;
const CURRENCY_PATTERN = /^[A-Z]{3}$/;

export interface SamsungDpiVerifierConfig {
  appId: string;
  productId: string;
}

export interface SamsungDpiProvider {
  getProductOffer(countryCode: string): Promise<SamsungProductOffer | null>;
  findActivePurchase(customId: string, countryCode: string): Promise<{ invoiceId: string } | null>;
  verifyInvoice(invoiceId: string, customId: string, countryCode: string): Promise<boolean>;
}

export async function verifySamsungStatus(
  request: SamsungStatusRequest,
  config: SamsungDpiVerifierConfig,
  dpi: SamsungDpiProvider
): Promise<SamsungStatusResponse> {
  assertConfiguredSamsungIdentity(request, config.appId, config.productId);
  const [product, purchase] = await Promise.all([
    dpi.getProductOffer(request.countryCode),
    dpi.findActivePurchase(request.customId, request.countryCode)
  ]);
  const verified = purchase === null
    ? false
    : await dpi.verifyInvoice(purchase.invoiceId, request.customId, request.countryCode);
  const response: SamsungStatusResponse = {
    schemaVersion: 1,
    verified,
    checkoutAvailable: product !== null,
    productId: request.productId
  };
  if (product) response.product = product;
  return response;
}

export interface SamsungDpiHttpClientOptions extends SamsungDpiVerifierConfig {
  securityKey: string;
  fetcher?: typeof fetch;
  crypto?: Crypto;
  dpiOrigin?: string;
  maximumPurchasePages?: number;
}

export class SamsungDpiHttpClient implements SamsungDpiProvider {
  private readonly fetcher: typeof fetch;
  private readonly webCrypto: Crypto;
  private readonly dpiOrigin: string;
  private readonly maximumPurchasePages: number;

  constructor(private readonly options: SamsungDpiHttpClientOptions) {
    if (!/^[A-Za-z0-9._-]{3,30}$/.test(options.appId)) throw new Error("Samsung DPI app ID is invalid.");
    if (!/^[A-Za-z0-9_-]{1,20}$/.test(options.productId)) throw new Error("Samsung DPI product ID is invalid.");
    if (!options.securityKey || options.securityKey.length > 4096 || /[\u0000-\u001F\u007F]/.test(options.securityKey)) {
      throw new Error("Samsung DPI security key is invalid.");
    }
    this.fetcher = options.fetcher ?? globalThis.fetch.bind(globalThis);
    this.webCrypto = options.crypto ?? globalThis.crypto;
    this.dpiOrigin = normalizeDpiOrigin(options.dpiOrigin ?? DEFAULT_DPI_ORIGIN);
    this.maximumPurchasePages = options.maximumPurchasePages ?? MAX_PURCHASE_PAGES;
    if (!Number.isInteger(this.maximumPurchasePages)
      || this.maximumPurchasePages < 1
      || this.maximumPurchasePages > MAX_PURCHASE_PAGES) {
      throw new Error(`Samsung DPI purchase history is limited to ${MAX_PURCHASE_PAGES} pages.`);
    }
  }

  async getProductOffer(countryCode: string): Promise<SamsungProductOffer | null> {
    const material = `${this.options.appId}${countryCode}`;
    const body = {
      AppID: this.options.appId,
      CountryCode: countryCode,
      ProductIDList: [this.options.productId],
      PageSize: 100,
      PageNumber: 1,
      CheckValue: await this.hmac(material)
    };
    const raw = await this.post("/openapi/cont/list", body);
    const page = await this.parseSignedList(raw, "ItemDetails");
    if (page.status !== "100000") throw new Error("Samsung DPI product lookup failed.");
    const match = page.entries.find((entry) => entry.ItemID === this.options.productId);
    if (!match) return null;
    if (String(match.ItemType) !== "2") throw new Error("Samsung DPI product is not a non-consumable.");
    const title = safeDisplay(entryValue(match, "ItemTitle"), "Samsung DPI product title", 120);
    const orderTotal = normalizePrice(entryValue(match, "Price"));
    const currencyId = safeDisplay(entryValue(match, "CurrencyID"), "Samsung DPI currency", 3);
    if (!CURRENCY_PATTERN.test(currencyId)) throw new Error("Samsung DPI currency is invalid.");
    return {
      productId: this.options.productId,
      title,
      localizedPrice: formatPrice(orderTotal, currencyId, countryCode),
      orderTotal,
      currencyId
    };
  }

  async findActivePurchase(customId: string, countryCode: string): Promise<{ invoiceId: string } | null> {
    for (let pageNumber = 1; pageNumber <= this.maximumPurchasePages; pageNumber += 1) {
      const itemType = "1";
      const material = `${this.options.appId}${customId}${countryCode}${itemType}${pageNumber}`;
      const body = {
        AppID: this.options.appId,
        CustomID: customId,
        CountryCode: countryCode,
        ItemType: itemType,
        PageNumber: pageNumber,
        CheckValue: await this.hmac(material)
      };
      const raw = await this.post("/openapi/invoice/list", body);
      const page = await this.parseSignedList(raw, "InvoiceDetails");
      if (page.status !== "100000") throw new Error("Samsung DPI purchase-history lookup failed.");
      for (const entry of page.entries) {
        if (entry.ItemID !== this.options.productId || String(entry.ItemType) !== "2") continue;
        if (parseBoolean(entry.CancelStatus, "CancelStatus")) continue;
        return { invoiceId: safeDisplay(entry.InvoiceID, "Samsung DPI invoice ID", 200) };
      }
      if (page.result === "EOF" || page.result === "Your Invoice Not Found") return null;
      if (page.result !== "hasNext:TRUE") throw new Error("Samsung DPI purchase history returned an unknown paging state.");
    }
    throw new Error("Samsung DPI purchase history exceeded the safe paging limit.");
  }

  async verifyInvoice(invoiceId: string, customId: string, countryCode: string): Promise<boolean> {
    const raw = await this.post("/openapi/invoice/verify", {
      AppID: this.options.appId,
      InvoiceID: invoiceId,
      CustomID: customId,
      CountryCode: countryCode
    });
    if (!isRecord(raw)) throw new Error("Samsung DPI invoice verification response is invalid.");
    return raw.CPStatus === "100000"
      && raw.CPResult === "SUCCESS"
      && raw.AppID === this.options.appId
      && raw.InvoiceID === invoiceId;
  }

  private async post(path: string, body: Record<string, unknown>): Promise<unknown> {
    const response = await this.fetcher(`${this.dpiOrigin}${path}`, {
      method: "POST",
      headers: {
        Accept: "application/json;charset=UTF-8",
        "Content-Type": "application/json;charset=UTF-8"
      },
      body: JSON.stringify(body),
      cache: "no-store",
      redirect: "error"
    });
    if (!response.ok) throw new Error(`Samsung DPI returned HTTP ${response.status}.`);
    return readBoundedJson(response, MAX_DPI_RESPONSE_BYTES, "Samsung DPI");
  }

  private async parseSignedList(
    value: unknown,
    entriesField: "ItemDetails" | "InvoiceDetails"
  ): Promise<{ status: string; result: string; entries: Array<Record<string, unknown>> }> {
    if (!isRecord(value)) throw new Error("Samsung DPI returned an invalid signed list.");
    const status = requiredString(value.CPStatus, "CPStatus", 32);
    const result = requiredString(value.CPResult, "CPResult", 128);
    const totalCount = value.TotalCount;
    if (typeof totalCount !== "number" || !Number.isInteger(totalCount) || totalCount < 0 || totalCount > 100_000) {
      throw new Error("Samsung DPI returned an invalid total count.");
    }
    const source = value[entriesField];
    const entries = source === undefined || source === null
      ? []
      : Array.isArray(source) && source.every(isRecord)
        ? source
        : null;
    if (!entries || entries.length > 100 || totalCount < entries.length) {
      throw new Error("Samsung DPI returned invalid list entries.");
    }
    const itemIds = entries.map((entry) => safeDisplay(entry.ItemID, "Samsung DPI item ID", 256));
    const checkValue = safeDisplay(value.CheckValue, "Samsung DPI check value", 512);
    const material = `${status}${result}${totalCount}${itemIds.join("")}`;
    const expected = await this.hmac(material);
    if (!constantTimeEqual(checkValue, expected)) throw new Error("Samsung DPI response HMAC verification failed.");
    return { status, result, entries };
  }

  private async hmac(value: string): Promise<string> {
    const key = await this.webCrypto.subtle.importKey(
      "raw",
      new TextEncoder().encode(this.options.securityKey),
      { name: "HMAC", hash: "SHA-256" },
      false,
      ["sign"]
    );
    const signature = await this.webCrypto.subtle.sign("HMAC", key, new TextEncoder().encode(value));
    return base64Bytes(new Uint8Array(signature));
  }
}

function entryValue(entry: Record<string, unknown>, name: string): unknown {
  return entry[name];
}

function safeDisplay(value: unknown, name: string, maximumLength: number): string {
  const result = requiredString(value, name, maximumLength);
  if (result.trim() !== result) throw new Error(`${name} is invalid.`);
  return result;
}

function normalizePrice(value: unknown): string {
  const candidate = typeof value === "number" && Number.isFinite(value) ? String(value) : value;
  if (typeof candidate !== "string" || !PRICE_PATTERN.test(candidate)) {
    throw new Error("Samsung DPI product price is invalid.");
  }
  return candidate;
}

function formatPrice(value: string, currencyId: string, countryCode: string): string {
  try {
    return new Intl.NumberFormat(`en-${countryCode}`, {
      style: "currency",
      currency: currencyId
    }).format(Number(value));
  } catch {
    return `${value} ${currencyId}`;
  }
}

function parseBoolean(value: unknown, name: string): boolean {
  if (value === true || value === "true") return true;
  if (value === false || value === "false") return false;
  throw new Error(`Samsung DPI ${name} is invalid.`);
}

function normalizeDpiOrigin(value: string): string {
  const url = new URL(value);
  if (url.protocol !== "https:"
    || !url.hostname
    || url.username
    || url.password
    || url.pathname !== "/"
    || url.search
    || url.hash) {
    throw new Error("Samsung DPI origin must be a clean HTTPS origin.");
  }
  return url.origin;
}

function base64Bytes(value: Uint8Array): string {
  let binary = "";
  for (const byte of value) binary += String.fromCharCode(byte);
  return btoa(binary);
}

function constantTimeEqual(left: string, right: string): boolean {
  const maximum = Math.max(left.length, right.length);
  let difference = left.length ^ right.length;
  for (let index = 0; index < maximum; index += 1) {
    difference |= (left.charCodeAt(index) || 0) ^ (right.charCodeAt(index) || 0);
  }
  return difference === 0;
}
