export function asRecord(value: unknown): Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown>
    : {};
}

export function asArray(value: unknown): unknown[] {
  return Array.isArray(value) ? value : [];
}

export function asString(value: unknown): string | undefined {
  if (typeof value === "string" && value.trim()) return value.trim();
  if (typeof value === "number" && Number.isFinite(value)) return String(value);
  return undefined;
}

export function asNumber(value: unknown): number | undefined {
  const parsed = typeof value === "number" ? value : Number(value);
  return Number.isFinite(parsed) ? parsed : undefined;
}

export function asBoolean(value: unknown): boolean {
  return value === true || value === 1 || value === "1" || value === "true";
}

export function clampPage(start: number, size: number): { start: number; size: number } {
  const safeStart = Number.isFinite(start) ? Math.floor(start) : 0;
  const safeSize = Number.isFinite(size) ? Math.floor(size) : 200;
  return {
    start: Math.max(0, safeStart),
    size: Math.min(1_000, Math.max(1, safeSize))
  };
}
