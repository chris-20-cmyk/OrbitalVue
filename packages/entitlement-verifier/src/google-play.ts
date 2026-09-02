import {
  assertConfiguredGoogleIdentity,
  isRecord,
  type GooglePlayVerificationRequest,
  type GooglePlayVerificationResponse
} from "./contracts.js";

const DEFAULT_ANDROID_PUBLISHER_ORIGIN = "https://androidpublisher.googleapis.com";
const MAX_PROVIDER_RESPONSE_BYTES = 128 * 1024;

export interface ProductPurchaseV2 {
  purchaseStateContext?: { purchaseState?: string };
  testPurchaseContext?: { fopType?: string };
  purchaseCompletionTime?: string;
  productLineItem?: Array<{
    productId?: string;
    productOfferDetails?: {
      quantity?: number;
      refundableQuantity?: number;
      consumptionState?: string;
    };
  }>;
}

export interface GooglePlayPublisher {
  getProductPurchase(packageName: string, purchaseToken: string): Promise<ProductPurchaseV2>;
}

export interface GooglePlayVerifierConfig {
  packageName: string;
  productId: string;
  allowTestPurchases?: boolean;
}

export async function verifyGooglePlayPurchase(
  request: GooglePlayVerificationRequest,
  config: GooglePlayVerifierConfig,
  publisher: GooglePlayPublisher
): Promise<GooglePlayVerificationResponse> {
  assertConfiguredGoogleIdentity(request, config.packageName, config.productId);
  const purchase = await publisher.getProductPurchase(request.packageName, request.purchaseToken);
  const verified = isPurchasedProduct(purchase, request.productId, config.allowTestPurchases === true);
  return { schemaVersion: 1, verified, productId: request.productId };
}

export function isPurchasedProduct(
  purchase: ProductPurchaseV2,
  expectedProductId: string,
  allowTestPurchases = false
): boolean {
  if (purchase.purchaseStateContext?.purchaseState !== "PURCHASED") return false;
  if (purchase.testPurchaseContext !== undefined
    && (!allowTestPurchases || purchase.testPurchaseContext.fopType !== "TEST")) return false;
  if (!purchase.purchaseCompletionTime || Number.isNaN(Date.parse(purchase.purchaseCompletionTime))) return false;

  const items = purchase.productLineItem;
  if (!Array.isArray(items) || items.length !== 1) return false;
  const item = items[0];
  if (!item || item.productId !== expectedProductId) return false;
  const offer = item.productOfferDetails;
  if (!offer) return false;
  if (!Number.isInteger(offer.quantity) || (offer.quantity ?? 0) < 1) return false;
  if (!Number.isInteger(offer.refundableQuantity) || (offer.refundableQuantity ?? 0) < 1) return false;
  if (offer.consumptionState !== "YET_TO_BE_CONSUMED") return false;
  return true;
}

export interface GooglePlayPublisherHttpClientOptions {
  getAccessToken: () => Promise<string>;
  fetcher?: typeof fetch;
  apiOrigin?: string;
}

export class GooglePlayPublisherHttpClient implements GooglePlayPublisher {
  private readonly fetcher: typeof fetch;
  private readonly apiOrigin: string;

  constructor(private readonly options: GooglePlayPublisherHttpClientOptions) {
    this.fetcher = options.fetcher ?? globalThis.fetch.bind(globalThis);
    this.apiOrigin = normalizeApiOrigin(options.apiOrigin ?? DEFAULT_ANDROID_PUBLISHER_ORIGIN);
  }

  async getProductPurchase(packageName: string, purchaseToken: string): Promise<ProductPurchaseV2> {
    const accessToken = await this.options.getAccessToken();
    if (!accessToken || accessToken.length > 8192 || /[\u0000-\u0020\u007F]/.test(accessToken)) {
      throw new Error("Google OAuth access token is invalid.");
    }
    const path = `/androidpublisher/v3/applications/${encodeURIComponent(packageName)}`
      + `/purchases/productsv2/tokens/${encodeURIComponent(purchaseToken)}`;
    const response = await this.fetcher(`${this.apiOrigin}${path}`, {
      method: "GET",
      headers: { Accept: "application/json", Authorization: `Bearer ${accessToken}` },
      cache: "no-store",
      redirect: "error"
    });
    if (!response.ok) throw new Error(`Google Play Developer API returned HTTP ${response.status}.`);
    const raw = await readBoundedJson(response, MAX_PROVIDER_RESPONSE_BYTES, "Google Play Developer API");
    return parseProductPurchaseV2(raw);
  }
}

function parseProductPurchaseV2(value: unknown): ProductPurchaseV2 {
  if (!isRecord(value)) throw new Error("Google Play Developer API returned an invalid purchase object.");
  const purchase: ProductPurchaseV2 = {};
  if (isRecord(value.purchaseStateContext) && typeof value.purchaseStateContext.purchaseState === "string") {
    purchase.purchaseStateContext = { purchaseState: value.purchaseStateContext.purchaseState };
  }
  if (value.testPurchaseContext !== undefined) {
    purchase.testPurchaseContext = isRecord(value.testPurchaseContext)
      && typeof value.testPurchaseContext.fopType === "string"
      ? { fopType: value.testPurchaseContext.fopType }
      : {};
  }
  if (typeof value.purchaseCompletionTime === "string") purchase.purchaseCompletionTime = value.purchaseCompletionTime;
  if (Array.isArray(value.productLineItem)) {
    purchase.productLineItem = value.productLineItem.map((candidate) => {
      if (!isRecord(candidate)) return {};
      const line: NonNullable<ProductPurchaseV2["productLineItem"]>[number] = {};
      if (typeof candidate.productId === "string") line.productId = candidate.productId;
      if (isRecord(candidate.productOfferDetails)) {
        const source = candidate.productOfferDetails;
        const offer: NonNullable<typeof line.productOfferDetails> = {};
        if (typeof source.quantity === "number") offer.quantity = source.quantity;
        if (typeof source.refundableQuantity === "number") offer.refundableQuantity = source.refundableQuantity;
        if (typeof source.consumptionState === "string") offer.consumptionState = source.consumptionState;
        line.productOfferDetails = offer;
      }
      return line;
    });
  }
  return purchase;
}

function normalizeApiOrigin(value: string): string {
  const url = new URL(value);
  if (url.protocol !== "https:"
    || !url.hostname
    || url.username
    || url.password
    || url.pathname !== "/"
    || url.search
    || url.hash) {
    throw new Error("Google Play API origin must be a clean HTTPS origin.");
  }
  return url.origin;
}

export async function readBoundedJson(response: Response, maximumBytes: number, provider: string): Promise<unknown> {
  const announced = Number(response.headers.get("content-length"));
  if (Number.isFinite(announced) && announced > maximumBytes) {
    throw new Error(`${provider} response is too large.`);
  }
  const body = await response.text();
  if (new TextEncoder().encode(body).length > maximumBytes) throw new Error(`${provider} response is too large.`);
  try {
    return JSON.parse(body) as unknown;
  } catch {
    throw new Error(`${provider} returned invalid JSON.`);
  }
}
