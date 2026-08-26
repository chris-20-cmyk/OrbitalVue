import { sha256Hex } from "./sha256.js";
import type {
  CatalogChannel,
  CatchupMetadata,
  ChannelKind,
  GuideMetadata,
  ParseM3uOptions,
  ParsedPlaylist
} from "./types.js";

const PLAYABLE_SCHEMES = new Set(["http:", "https:", "rtsp:", "rtmp:", "udp:", "file:"]);
const GUIDE_SCHEMES = new Set(["http:", "https:", "file:"]);
const DEFAULT_MAX_CHANNELS = 250_000;

interface PendingChannel {
  name: string;
  group?: string | undefined;
  logoUri?: string | undefined;
  tvgId?: string | undefined;
  tvgName?: string | undefined;
  userAgent?: string | undefined;
  referrer?: string | undefined;
  catchupMode?: string | undefined;
  catchupSource?: string | undefined;
  catchupDays: number;
  catchupCorrectionMinutes: number;
}

function clean(value: string | undefined): string | undefined {
  const result = value?.trim();
  return result ? result : undefined;
}

function parseAttributes(value: string): Map<string, string> {
  const attributes = new Map<string, string>();
  const pattern = /([A-Za-z0-9_-]+)=(?:"([^"]*)"|'([^']*)'|([^\s,]+))/g;
  for (const match of value.matchAll(pattern)) {
    const name = match[1];
    if (!name) continue;
    attributes.set(name.toLowerCase(), match[2] ?? match[3] ?? match[4] ?? "");
  }
  return attributes;
}

function findNameSeparator(line: string): number {
  let quote: string | undefined;
  for (let index = 0; index < line.length; index += 1) {
    const character = line[index];
    if (character === '"' || character === "'") {
      quote = quote === undefined ? character : quote === character ? undefined : quote;
    } else if (character === "," && quote === undefined) {
      return index;
    }
  }
  return -1;
}

function parseMetadata(line: string): PendingChannel {
  const separator = findNameSeparator(line);
  const metadata = separator >= 0 ? line.slice(0, separator) : line;
  const listedName = separator >= 0 ? line.slice(separator + 1).trim() : "";
  const attributes = parseAttributes(metadata);
  const tvgName = attributes.get("tvg-name");
  const parsedDays = Number.parseInt(attributes.get("catchup-days") ?? attributes.get("timeshift") ?? "0", 10);
  const parsedCorrection = Number.parseFloat(attributes.get("catchup-correction") ?? "0");

  return {
    name: listedName || tvgName || "",
    group: attributes.get("group-title"),
    logoUri: attributes.get("tvg-logo"),
    tvgId: attributes.get("tvg-id"),
    tvgName,
    userAgent: attributes.get("http-user-agent"),
    referrer: attributes.get("http-referrer"),
    catchupMode: attributes.get("catchup"),
    catchupSource: attributes.get("catchup-source"),
    catchupDays: Number.isFinite(parsedDays) ? Math.max(0, parsedDays) : 0,
    catchupCorrectionMinutes: Number.isFinite(parsedCorrection) ? Math.trunc(parsedCorrection * 60) : 0
  };
}

function parseGuideSources(line: string): string[] {
  const attributes = parseAttributes(line);
  for (const key of ["url-tvg", "x-tvg-url", "tvg-url"]) {
    const value = attributes.get(key);
    if (!value) continue;
    const sources = value.split(",").map((candidate) => candidate.trim()).filter(isGuideUri);
    if (sources.length > 0) return sources;
  }
  return [];
}

function extractReferrer(line: string): string | undefined {
  if (line.includes("=")) return clean(line.slice(line.indexOf("=") + 1));
  const match = /["']Referer["']\s*:\s*["']([^"']+)/i.exec(line);
  return clean(match?.[1]);
}

function hasScheme(value: string, allowed: Set<string>): boolean {
  try {
    return allowed.has(new URL(value).protocol.toLowerCase());
  } catch {
    return false;
  }
}

function isGuideUri(value: string): boolean {
  return hasScheme(value, GUIDE_SCHEMES);
}

function inferKind(group: string, streamUri: string): ChannelKind {
  const value = `${group} ${streamUri}`.toLowerCase();
  if (value.includes("/series/") || value.includes("series") || value.includes("shows")) return "series";
  if (value.includes("/movie/") || value.includes("movie") || value.includes("vod") || value.includes("cinema")) {
    return "movie";
  }
  return "live";
}

function createCatchup(metadata: PendingChannel): CatchupMetadata | undefined {
  const source = clean(metadata.catchupSource);
  if (!source) return undefined;
  return {
    mode: clean(metadata.catchupMode) ?? "default",
    source,
    days: metadata.catchupDays,
    correctionMinutes: metadata.catchupCorrectionMinutes
  };
}

export function stableChannelId(tvgId: string | undefined, name: string, group: string, streamUri: string): string {
  const trimmedUri = streamUri.trim();
  const query = trimmedUri.indexOf("?");
  const fragment = trimmedUri.indexOf("#");
  const cutAt = [query, fragment].filter((index) => index >= 0).sort((left, right) => left - right)[0];
  const endpoint = cutAt === undefined ? trimmedUri : trimmedUri.slice(0, cutAt);
  const identity = clean(tvgId)
    ? `tvg:${clean(tvgId)?.toUpperCase()}|name:${name.trim().toUpperCase()}|group:${group.trim().toUpperCase()}|endpoint:${endpoint}`
    : `name:${name.trim().toUpperCase()}|group:${group.trim().toUpperCase()}|endpoint:${endpoint}`;
  return sha256Hex(identity);
}

export function parseM3u(text: string, options: ParseM3uOptions): ParsedPlaylist {
  if (!options.sourceId.trim()) throw new Error("A source ID is required.");
  if (!options.sourceName.trim()) throw new Error("A source name is required.");
  const maximum = options.maxChannels ?? DEFAULT_MAX_CHANNELS;
  if (!Number.isInteger(maximum) || maximum < 1) throw new Error("The channel safety limit must be positive.");

  const channels: CatalogChannel[] = [];
  let pending: PendingChannel | undefined;
  let guideSources: string[] = [];

  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trim().replace(/^\uFEFF/, "");
    if (!line) continue;

    if (/^#EXTM3U/i.test(line)) {
      if (guideSources.length === 0) guideSources = parseGuideSources(line);
      continue;
    }
    if (/^#EXTINF/i.test(line)) {
      pending = parseMetadata(line);
      continue;
    }
    if (pending && /^#EXTVLCOPT:http-user-agent=/i.test(line)) {
      pending.userAgent = line.slice(line.indexOf("=") + 1).trim();
      continue;
    }
    if (pending && (/^#EXTVLCOPT:http-referrer=/i.test(line) || /^#EXTHTTP:/i.test(line))) {
      pending.referrer = extractReferrer(line);
      continue;
    }
    if (line.startsWith("#") || !hasScheme(line, PLAYABLE_SCHEMES)) continue;

    if (channels.length >= maximum) {
      throw new Error(`The playlist exceeds the ${maximum.toLocaleString()} channel safety limit.`);
    }
    const metadata = pending ?? {
      name: `Channel ${channels.length + 1}`,
      catchupDays: 0,
      catchupCorrectionMinutes: 0
    };
    const name = clean(metadata.name) ?? `Channel ${channels.length + 1}`;
    const group = clean(metadata.group) ?? "Uncategorized";
    const requestHeaders: Record<string, string> = {};
    const userAgent = clean(metadata.userAgent);
    const referrer = clean(metadata.referrer);
    if (userAgent) requestHeaders["User-Agent"] = userAgent;
    if (referrer) requestHeaders.Referer = referrer;

    const guide: GuideMetadata = {};
    const tvgId = clean(metadata.tvgId);
    const tvgName = clean(metadata.tvgName);
    const logoUri = clean(metadata.logoUri);
    if (tvgId) guide.tvgId = tvgId;
    if (tvgName) guide.tvgName = tvgName;
    if (logoUri) guide.logoUri = logoUri;
    const catchup = createCatchup(metadata);
    channels.push({
      id: stableChannelId(metadata.tvgId, name, group, line),
      number: channels.length + 1,
      name,
      group,
      kind: inferKind(group, line),
      sourceId: options.sourceId,
      stream: { uri: line, requestHeaders },
      ...(Object.keys(guide).length > 0 ? { guide } : {}),
      ...(catchup ? { catchup } : {})
    });
    pending = undefined;
  }

  if (channels.length === 0) {
    throw new Error("No playable entries were found. Choose an M3U or M3U8 playlist that contains stream URLs.");
  }
  return { channels, guideSources };
}
