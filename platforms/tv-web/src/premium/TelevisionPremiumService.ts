import { detectPlatform, type TvPlatform } from "../platform/platform.js";
import {
  evaluatePremiumAccess,
  type PremiumAccessSnapshot
} from "./PremiumAccess.js";

export type TelevisionPremiumProvider =
  | "personal-build"
  | "samsung-checkout"
  | "lg-third-party"
  | "unsupported";

export type TelevisionPremiumStatus =
  | "included"
  | "checking"
  | "available"
  | "purchasing"
  | "verified"
  | "unavailable"
  | "error";

export interface TelevisionPremiumSnapshot {
  access: PremiumAccessSnapshot;
  provider: TelevisionPremiumProvider;
  status: TelevisionPremiumStatus;
  canBuy: boolean;
  canRestore: boolean;
  productTitle?: string;
  localizedPrice?: string;
  message: string;
}

export interface TelevisionPremiumService {
  readonly snapshot: TelevisionPremiumSnapshot;
  start(): Promise<void>;
  refresh(): Promise<void>;
  purchase(): Promise<void>;
  subscribe(listener: (snapshot: TelevisionPremiumSnapshot) => void): () => void;
  destroy(): void;
}

export interface SamsungCheckoutConfig {
  appId: string;
  productId: string;
  verificationUrl: string;
}

export interface SamsungCheckoutApis {
  billing?: Pick<SamsungBilling, "buyItem"> & Partial<Pick<SamsungBilling, "isServiceAvailable">>;
  productinfo?: Pick<SamsungProductInfo, "getSystemConfig" | "ProductInfoConfigKey">;
  sso?: Pick<SamsungSso, "getLoginUid">;
}

export interface TelevisionPremiumServiceOptions {
  platform?: TvPlatform;
  distributionMode?: string;
  samsungConfig?: Partial<SamsungCheckoutConfig>;
  samsungApis?: SamsungCheckoutApis;
  fetcher?: typeof fetch;
}

interface SamsungProductOffer {
  title: string;
  localizedPrice: string;
  orderTotal: string;
  currencyId: string;
}

interface SamsungVerifierDecision {
  verified: boolean;
  checkoutAvailable: boolean;
  product: SamsungProductOffer | null;
}

const SAMSUNG_PRODUCT_PATTERN = /^[A-Za-z0-9_-]{1,20}$/;
const APP_ID_PATTERN = /^[A-Za-z0-9._-]{3,30}$/;
const COUNTRY_PATTERN = /^[A-Z]{2}$/;
const CURRENCY_PATTERN = /^[A-Z]{3}$/;
const ORDER_TOTAL_PATTERN = /^(?:0|[1-9]\d{0,11})(?:\.\d{1,2})?$/;
const VERIFIER_TIMEOUT_MS = 12_000;
const MAX_VERIFIER_RESPONSE_CHARS = 64 * 1024;

export function createTelevisionPremiumService(
  options: TelevisionPremiumServiceOptions = {}
): TelevisionPremiumService {
  const distributionMode = options.distributionMode
    ?? import.meta.env.VITE_STREAMVUE_DISTRIBUTION_MODE
    ?? "personal";
  const normalizedMode = distributionMode.trim().toLowerCase();
  if (normalizedMode === "personal") return new StaticPremiumService(personalSnapshot());
  if (normalizedMode !== "store") {
    return new StaticPremiumService(lockedSnapshot(
      "unsupported",
      "Premium access is locked because this television build has an invalid distribution mode."
    ));
  }

  const platform = options.platform ?? detectPlatform();
  if (platform === "lg-webos") {
    return new StaticPremiumService(lockedSnapshot(
      "lg-third-party",
      "LG no longer provides a native TV billing service. Plex and Emby stay locked until a reviewed third-party billing contract and server-side entitlement verifier are connected."
    ));
  }
  if (platform !== "samsung-tizen") {
    return new StaticPremiumService(lockedSnapshot(
      "unsupported",
      "This browser preview has no television seller account. Use the Samsung or LG package to verify a premium purchase."
    ));
  }

  const config = normalizeSamsungCheckoutConfig(options.samsungConfig ?? {
    appId: import.meta.env.VITE_STREAMVUE_SAMSUNG_APP_ID,
    productId: import.meta.env.VITE_STREAMVUE_SAMSUNG_PRODUCT_ID,
    verificationUrl: import.meta.env.VITE_STREAMVUE_SAMSUNG_VERIFICATION_URL
  });
  const fetcher = options.fetcher ?? (typeof globalThis.fetch === "function"
    ? globalThis.fetch.bind(globalThis)
    : null);
  if (!fetcher) {
    return new StaticPremiumService(lockedSnapshot(
      "samsung-checkout",
      "This Samsung television does not provide the secure network API required for purchase verification."
    ));
  }
  return new SamsungCheckoutPremiumService(
    config,
    options.samsungApis ?? window.webapis,
    fetcher
  );
}

export function normalizeSamsungCheckoutConfig(
  value: Partial<SamsungCheckoutConfig>
): SamsungCheckoutConfig | null {
  const appId = value.appId?.trim() ?? "";
  const productId = value.productId?.trim() ?? "";
  const verificationUrl = normalizeHttpsEndpoint(value.verificationUrl);
  if (!APP_ID_PATTERN.test(appId) || !SAMSUNG_PRODUCT_PATTERN.test(productId) || !verificationUrl) {
    return null;
  }
  return { appId, productId, verificationUrl };
}

export class SamsungCheckoutPremiumService implements TelevisionPremiumService {
  private readonly listeners = new Set<(snapshot: TelevisionPremiumSnapshot) => void>();
  private current: TelevisionPremiumSnapshot;
  private product: SamsungProductOffer | null = null;
  private inFlight: Promise<void> | null = null;
  private purchaseInProgress = false;

  constructor(
    private readonly config: SamsungCheckoutConfig | null,
    private readonly apis: SamsungCheckoutApis | undefined,
    private readonly fetcher: typeof fetch
  ) {
    this.current = lockedSnapshot(
      "samsung-checkout",
      config
        ? "Samsung Checkout is preparing the one-time premium product."
        : "Samsung Checkout needs an exact Seller Office app ID, non-consumable product ID, and HTTPS StreamVue verifier before purchases can be offered."
    );
  }

  get snapshot(): TelevisionPremiumSnapshot {
    return this.current;
  }

  async start(): Promise<void> {
    await this.refresh();
  }

  async refresh(): Promise<void> {
    if (this.purchaseInProgress) return;
    if (this.inFlight) return this.inFlight;
    this.inFlight = this.refreshCore().finally(() => {
      this.inFlight = null;
    });
    return this.inFlight;
  }

  async purchase(): Promise<void> {
    if (this.inFlight) await this.inFlight;
    const config = this.config;
    const billing = this.apis?.billing;
    const product = this.product;
    if (!config || !billing || !product || !this.current.canBuy) {
      throw new Error(this.current.message);
    }
    if (!await samsungBillingServiceAvailable(billing)) {
      const message = "Samsung Checkout became unavailable before purchase. Existing purchases can still be restored.";
      this.update({
        ...this.current,
        status: "unavailable",
        canBuy: false,
        canRestore: true,
        message
      });
      throw new Error(message);
    }
    const identity = samsungIdentity(this.apis);
    this.purchaseInProgress = true;
    this.update({
      ...this.current,
      status: "purchasing",
      canBuy: false,
      canRestore: false,
      message: "Samsung Checkout is handling the one-time purchase."
    });
    await new Promise<void>((resolve, reject) => {
      const paymentDetails = JSON.stringify({
        OrderItemID: config.productId,
        OrderTitle: product.title,
        OrderTotal: product.orderTotal,
        OrderCurrencyID: product.currencyId,
        OrderCustomID: identity.customId
      });
      try {
        billing.buyItem(
          config.appId,
          "PRD",
          paymentDetails,
          () => resolve(),
          (error) => reject(new Error(samsungErrorMessage(error, "Samsung Checkout could not open the purchase screen.")))
        );
      } catch (error) {
        reject(error);
      }
    }).catch((error: unknown) => {
      this.purchaseInProgress = false;
      this.update(this.errorSnapshot(error, "Samsung Checkout could not finish the purchase."));
      throw error;
    });

    // A native callback is never entitlement proof. Only a fresh server-side DPI
    // purchase-history decision below can unlock the feature.
    this.purchaseInProgress = false;
    await this.refreshAfterPurchase();
  }

  subscribe(listener: (snapshot: TelevisionPremiumSnapshot) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  destroy(): void {
    this.listeners.clear();
  }

  private async refreshAfterPurchase(): Promise<void> {
    if (this.inFlight) await this.inFlight;
    await this.refresh();
    if (!this.current.access.canUseMediaCenters && this.current.status === "available") {
      this.update({
        ...this.current,
        message: "Checkout closed, but Samsung purchase history has not verified this product. You can retry or restore after the transaction completes."
      });
    }
  }

  private async refreshCore(): Promise<void> {
    const config = this.config;
    if (!config) return;
    if (!this.apis?.billing || !this.apis.productinfo || !this.apis.sso) {
      this.update(lockedSnapshot(
        "samsung-checkout",
        "Samsung Checkout APIs are unavailable. Install the signed Samsung TV package on a supported television."
      ));
      return;
    }

    let identity: SamsungIdentity;
    try {
      identity = samsungIdentity(this.apis);
    } catch (error) {
      this.update(this.errorSnapshot(error, "Sign in to a Samsung Account on this television to buy or restore premium access."));
      return;
    }

    this.update({
      ...this.current,
      status: "checking",
      canBuy: false,
      canRestore: false,
      message: "Checking Samsung purchase history securely…"
    });
    try {
      const decision = await requestSamsungDecision(this.fetcher, config, identity);
      this.product = decision.product;
      if (decision.verified) {
        this.update({
          access: evaluatePremiumAccess("store", true, config.productId),
          provider: "samsung-checkout",
          status: "verified",
          canBuy: false,
          canRestore: true,
          ...(decision.product ? {
            productTitle: decision.product.title,
            localizedPrice: decision.product.localizedPrice
          } : {}),
          message: "Samsung purchase history verified the one-time premium unlock."
        });
        return;
      }
      if (!decision.product) {
        throw new Error("The verifier did not return the exact Samsung Checkout product offer.");
      }
      if (!decision.checkoutAvailable) {
        this.update({
          access: evaluatePremiumAccess("store", false),
          provider: "samsung-checkout",
          status: "unavailable",
          canBuy: false,
          canRestore: true,
          productTitle: decision.product.title,
          localizedPrice: decision.product.localizedPrice,
          message: "Samsung Checkout is not available in this television's service country. Existing purchases can still be restored."
        });
        return;
      }
      if (!await samsungBillingServiceAvailable(this.apis.billing)) {
        this.update({
          access: evaluatePremiumAccess("store", false),
          provider: "samsung-checkout",
          status: "unavailable",
          canBuy: false,
          canRestore: true,
          productTitle: decision.product.title,
          localizedPrice: decision.product.localizedPrice,
          message: "Samsung Checkout is temporarily unavailable on this television. Existing purchases can still be restored."
        });
        return;
      }
      this.update({
        access: evaluatePremiumAccess("store", false),
        provider: "samsung-checkout",
        status: "available",
        canBuy: true,
        canRestore: true,
        productTitle: decision.product.title,
        localizedPrice: decision.product.localizedPrice,
        message: "Buy once through Samsung Checkout, or restore a purchase already owned by this Samsung Account."
      });
    } catch (error) {
      this.product = null;
      this.update(this.errorSnapshot(error, "Samsung purchase history could not be verified. Premium access remains locked."));
    }
  }

  private errorSnapshot(error: unknown, fallback: string): TelevisionPremiumSnapshot {
    const message = error instanceof Error && error.message ? error.message : fallback;
    return {
      ...lockedSnapshot("samsung-checkout", message),
      status: "error"
    };
  }

  private update(snapshot: TelevisionPremiumSnapshot): void {
    this.current = snapshot;
    for (const listener of this.listeners) listener(snapshot);
  }
}

class StaticPremiumService implements TelevisionPremiumService {
  constructor(readonly snapshot: TelevisionPremiumSnapshot) {}
  async start(): Promise<void> {}
  async refresh(): Promise<void> {}
  async purchase(): Promise<void> { throw new Error(this.snapshot.message); }
  subscribe(_listener: (snapshot: TelevisionPremiumSnapshot) => void): () => void { return () => {}; }
  destroy(): void {}
}

interface SamsungIdentity {
  customId: string;
  countryCode: string;
}

function samsungIdentity(apis: SamsungCheckoutApis | undefined): SamsungIdentity {
  const rawCustomId = apis?.sso?.getLoginUid();
  const customId = typeof rawCustomId === "string" ? rawCustomId.trim() : "";
  const countryKey = apis?.productinfo?.ProductInfoConfigKey.CONFIG_KEY_SERVICE_COUNTRY;
  const rawCountryCode = countryKey ? apis?.productinfo?.getSystemConfig(countryKey) : "";
  const countryCode = typeof rawCountryCode === "string" ? rawCountryCode.trim().toUpperCase() : "";
  if (!customId || customId.length > 512 || /[\u0000-\u001F\u007F]/.test(customId)) {
    throw new Error("Sign in to a Samsung Account on this television to buy or restore premium access.");
  }
  if (!COUNTRY_PATTERN.test(countryCode)) {
    throw new Error("Samsung Checkout could not determine this television's service country.");
  }
  return { customId, countryCode };
}

async function requestSamsungDecision(
  fetcher: typeof fetch,
  config: SamsungCheckoutConfig,
  identity: SamsungIdentity
): Promise<SamsungVerifierDecision> {
  const controller = new AbortController();
  const timeout = globalThis.setTimeout(() => controller.abort(), VERIFIER_TIMEOUT_MS);
  let response: Response;
  try {
    response = await fetcher(config.verificationUrl, {
      method: "POST",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      cache: "no-store",
      credentials: "omit",
      referrerPolicy: "no-referrer",
      signal: controller.signal,
      body: JSON.stringify({
        schemaVersion: 1,
        platform: "samsung",
        action: "status",
        appId: config.appId,
        productId: config.productId,
        customId: identity.customId,
        countryCode: identity.countryCode
      })
    });
  } catch (error) {
    if (controller.signal.aborted) {
      throw new Error("Samsung purchase verification timed out.");
    }
    throw error;
  } finally {
    globalThis.clearTimeout(timeout);
  }
  if (!response.ok) throw new Error(`Samsung entitlement verifier returned HTTP ${response.status}.`);
  const body = await response.text();
  if (body.length === 0 || body.length > MAX_VERIFIER_RESPONSE_CHARS) {
    throw new Error("Samsung entitlement verifier returned an invalid response size.");
  }
  let raw: unknown;
  try {
    raw = JSON.parse(body) as unknown;
  } catch {
    throw new Error("Samsung entitlement verifier returned invalid JSON.");
  }
  if (!isRecord(raw)
    || !hasOnlyKeys(raw, ["schemaVersion", "verified", "checkoutAvailable", "productId", "product"])
    || raw.schemaVersion !== 1
    || typeof raw.verified !== "boolean"
    || typeof raw.checkoutAvailable !== "boolean"
    || raw.productId !== config.productId) {
    throw new Error("Samsung entitlement verifier returned an invalid or mismatched decision.");
  }
  const product = raw.product === undefined || raw.product === null
    ? null
    : parseSamsungProduct(raw.product, config.productId);
  return { verified: raw.verified, checkoutAvailable: raw.checkoutAvailable, product };
}

async function samsungBillingServiceAvailable(billing: SamsungCheckoutApis["billing"]): Promise<boolean> {
  if (!billing) return false;
  // Samsung deprecated this device-level probe in favor of the authenticated
  // DPI country check performed by our verifier. Use it as a second gate on
  // televisions that still expose it, but do not reject newer implementations
  // that rely only on the required server-side country decision.
  const probe = billing.isServiceAvailable;
  if (typeof probe !== "function") return true;
  return new Promise<boolean>((resolve, reject) => {
    try {
      probe.call(
        billing,
        "PRD",
        (data) => {
          try {
            const value = JSON.parse(data.apiResult) as unknown;
            if (!isRecord(value)
              || !hasOnlyKeys(value, ["status", "result", "serviceYn"])
              || typeof value.status !== "string"
              || typeof value.result !== "string"
              || (value.serviceYn !== "Y" && value.serviceYn !== "N")) {
              reject(new Error("Samsung Checkout returned an invalid service-availability response."));
              return;
            }
            resolve(value.status === "100000" && value.serviceYn === "Y");
          } catch {
            reject(new Error("Samsung Checkout returned invalid service-availability JSON."));
          }
        },
        (error) => reject(new Error(samsungErrorMessage(
          error,
          "Samsung Checkout could not confirm billing-service availability."
        )))
      );
    } catch (error) {
      reject(error);
    }
  });
}

function parseSamsungProduct(value: unknown, productId: string): SamsungProductOffer {
  if (!isRecord(value)
    || !hasOnlyKeys(value, ["productId", "title", "localizedPrice", "orderTotal", "currencyId"])
    || value.productId !== productId
    || !safeDisplayText(value.title, 120)
    || !safeDisplayText(value.localizedPrice, 64)
    || typeof value.orderTotal !== "string"
    || !ORDER_TOTAL_PATTERN.test(value.orderTotal)
    || typeof value.currencyId !== "string"
    || !CURRENCY_PATTERN.test(value.currencyId)) {
    throw new Error("Samsung entitlement verifier returned invalid product metadata.");
  }
  return {
    title: value.title,
    localizedPrice: value.localizedPrice,
    orderTotal: value.orderTotal,
    currencyId: value.currencyId
  };
}

function personalSnapshot(): TelevisionPremiumSnapshot {
  return {
    access: evaluatePremiumAccess("personal", false),
    provider: "personal-build",
    status: "included",
    canBuy: false,
    canRestore: false,
    message: "Plex and Emby are included in this personal build."
  };
}

function lockedSnapshot(
  provider: TelevisionPremiumProvider,
  message: string
): TelevisionPremiumSnapshot {
  return {
    access: evaluatePremiumAccess("store", false),
    provider,
    status: "unavailable",
    canBuy: false,
    canRestore: false,
    message
  };
}

function normalizeHttpsEndpoint(value: string | undefined): string | null {
  try {
    const url = new URL(value?.trim() ?? "");
    if (url.protocol !== "https:" || url.username || url.password || url.search || url.hash) return null;
    return url.toString();
  } catch {
    return null;
  }
}

function safeDisplayText(value: unknown, maximumLength: number): value is string {
  return typeof value === "string"
    && value.trim() === value
    && value.length > 0
    && value.length <= maximumLength
    && !/[\u0000-\u001F\u007F]/.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function hasOnlyKeys(value: Record<string, unknown>, allowed: readonly string[]): boolean {
  const allowedKeys = new Set(allowed);
  return Object.keys(value).every((key) => allowedKeys.has(key));
}

function samsungErrorMessage(error: { name?: string; message?: string }, fallback: string): string {
  return error.message?.trim() || error.name?.trim() || fallback;
}
