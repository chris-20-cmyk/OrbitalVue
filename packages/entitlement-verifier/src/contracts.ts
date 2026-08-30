export const GOOGLE_PLAY_ROUTE = "/google-play/verify";
export const SAMSUNG_ROUTE = "/samsung/status";

const PACKAGE_PATTERN = /^[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)+$/;
const PRODUCT_PATTERN = /^[A-Za-z0-9._-]{3,256}$/;
const SAMSUNG_PRODUCT_PATTERN = /^[A-Za-z0-9_-]{1,20}$/;
const SAMSUNG_APP_PATTERN = /^[A-Za-z0-9._-]{3,30}$/;
const COUNTRY_PATTERN = /^[A-Z]{2}$/;
const SAFE_VALUE_PATTERN = /^[^\u0000-\u001F\u007F]+$/;

export interface GooglePlayVerificationRequest {
  schemaVersion: 1;
  platform: "google-play";
  packageName: string;
  productId: string;
  purchaseToken: string;
}

export interface GooglePlayVerificationResponse {
  schemaVersion: 1;
  verified: boolean;
  productId: string;
}

export interface SamsungStatusRequest {
  schemaVersion: 1;
  platform: "samsung";
  action: "status";
  appId: string;
  productId: string;
  customId: string;
  countryCode: string;
}

export interface SamsungProductOffer {
  productId: string;
  title: string;
  localizedPrice: string;
  orderTotal: string;
  currencyId: string;
}

export interface SamsungStatusResponse {
  schemaVersion: 1;
  verified: boolean;
  checkoutAvailable: boolean;
  productId: string;
  product?: SamsungProductOffer;
}

export class VerifierContractError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "VerifierContractError";
  }
}

export function parseGooglePlayRequest(value: unknown): GooglePlayVerificationRequest {
  const record = exactRecord(value, [
    "schemaVersion",
    "platform",
    "packageName",
    "productId",
    "purchaseToken"
  ], "Google Play verification request");
  if (record.schemaVersion !== 1 || record.platform !== "google-play") {
    throw new VerifierContractError("Google Play verification request has an unsupported contract version or platform.");
  }
  const packageName = stringField(record.packageName, 3, 255, PACKAGE_PATTERN, "packageName");
  const productId = stringField(record.productId, 3, 256, PRODUCT_PATTERN, "productId");
  const purchaseToken = stringField(record.purchaseToken, 1, 4096, SAFE_VALUE_PATTERN, "purchaseToken");
  return { schemaVersion: 1, platform: "google-play", packageName, productId, purchaseToken };
}

export function parseSamsungStatusRequest(value: unknown): SamsungStatusRequest {
  const record = exactRecord(value, [
    "schemaVersion",
    "platform",
    "action",
    "appId",
    "productId",
    "customId",
    "countryCode"
  ], "Samsung status request");
  if (record.schemaVersion !== 1 || record.platform !== "samsung" || record.action !== "status") {
    throw new VerifierContractError("Samsung status request has an unsupported contract version, platform, or action.");
  }
  const appId = stringField(record.appId, 3, 30, SAMSUNG_APP_PATTERN, "appId");
  const productId = stringField(record.productId, 1, 20, SAMSUNG_PRODUCT_PATTERN, "productId");
  const customId = stringField(record.customId, 1, 512, SAFE_VALUE_PATTERN, "customId");
  const countryCode = stringField(record.countryCode, 2, 2, COUNTRY_PATTERN, "countryCode");
  return { schemaVersion: 1, platform: "samsung", action: "status", appId, productId, customId, countryCode };
}

export function assertConfiguredGoogleIdentity(
  request: GooglePlayVerificationRequest,
  packageName: string,
  productId: string
): void {
  if (request.packageName !== packageName || request.productId !== productId) {
    throw new VerifierContractError("Google Play request does not match the configured application and product.");
  }
}

export function assertConfiguredSamsungIdentity(
  request: SamsungStatusRequest,
  appId: string,
  productId: string
): void {
  if (request.appId !== appId || request.productId !== productId) {
    throw new VerifierContractError("Samsung request does not match the configured application and product.");
  }
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

export function requiredString(value: unknown, name: string, maximumLength = 4096): string {
  if (typeof value !== "string" || value.length === 0 || value.length > maximumLength || !SAFE_VALUE_PATTERN.test(value)) {
    throw new Error(`Provider response field ${name} is invalid.`);
  }
  return value;
}

function exactRecord(value: unknown, keys: readonly string[], label: string): Record<string, unknown> {
  if (!isRecord(value)) throw new VerifierContractError(`${label} must be a JSON object.`);
  const actual = Object.keys(value).sort();
  const expected = [...keys].sort();
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    throw new VerifierContractError(`${label} fields are not exact.`);
  }
  return value;
}

function stringField(
  value: unknown,
  minimumLength: number,
  maximumLength: number,
  pattern: RegExp,
  name: string
): string {
  if (typeof value !== "string"
    || value.trim() !== value
    || value.length < minimumLength
    || value.length > maximumLength
    || !pattern.test(value)) {
    throw new VerifierContractError(`${name} is invalid.`);
  }
  return value;
}
