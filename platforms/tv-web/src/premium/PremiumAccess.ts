export type OrbitalVueDistributionMode = "personal" | "store" | "unknown";
export type PremiumAccessState = "included" | "verified" | "unavailable";

export interface PremiumAccessSnapshot {
  contractVersion: "1.0";
  featureId: "personal-media-centers";
  distributionMode: OrbitalVueDistributionMode;
  accessState: PremiumAccessState;
  acquisition: "included" | "one-time";
  receiptVerification: "not-required" | "verified" | "unavailable";
  productId?: string;
  canUseMediaCenters: boolean;
  badgeText: string;
  explanation: string;
}

export function currentPremiumAccess(): PremiumAccessSnapshot {
  return evaluatePremiumAccess(
    import.meta.env.VITE_ORBITALVUE_DISTRIBUTION_MODE ?? "personal",
    false
  );
}

export function evaluatePremiumAccess(
  distributionMode: string | undefined,
  hasVerifiedStorePurchase: boolean,
  productId?: string
): PremiumAccessSnapshot {
  const normalizedMode = distributionMode?.trim().toLowerCase();
  const mode: OrbitalVueDistributionMode = normalizedMode === "personal"
    ? "personal"
    : normalizedMode === "store"
      ? "store"
      : "unknown";
  if (mode === "personal") {
    return decision(mode, "included", "included", "not-required");
  }

  const normalizedProductId = normalizeProductId(productId);
  if (mode === "store" && hasVerifiedStorePurchase && normalizedProductId) {
    return decision(mode, "verified", "one-time", "verified", normalizedProductId);
  }
  return decision(mode, "unavailable", "one-time", "unavailable");
}

export function requireMediaCenterAccess(access: PremiumAccessSnapshot): void {
  if (!access.canUseMediaCenters) throw new Error(access.explanation);
}

function decision(
  distributionMode: OrbitalVueDistributionMode,
  accessState: PremiumAccessState,
  acquisition: "included" | "one-time",
  receiptVerification: "not-required" | "verified" | "unavailable",
  productId?: string
): PremiumAccessSnapshot {
  const canUseMediaCenters = accessState === "included" || accessState === "verified";
  const badgeText = accessState === "included"
    ? "PERSONAL BUILD • INCLUDED"
    : accessState === "verified"
      ? "PREMIUM • VERIFIED"
      : "PREMIUM • STORE LOCKED";
  const explanation = accessState === "included"
    ? "Plex and Emby are included in this personal build."
    : accessState === "verified"
      ? "A one-time store purchase was verified for this device account."
      : "A verified one-time store purchase is required. Store purchase verification is not connected in this build.";
  return {
    contractVersion: "1.0",
    featureId: "personal-media-centers",
    distributionMode,
    accessState,
    acquisition,
    receiptVerification,
    ...(productId ? { productId } : {}),
    canUseMediaCenters,
    badgeText,
    explanation
  };
}

function normalizeProductId(value: string | undefined): string | undefined {
  const candidate = value?.trim();
  return candidate && candidate.length >= 3 && candidate.length <= 256 && /^[A-Za-z0-9._-]+$/.test(candidate)
    ? candidate
    : undefined;
}
