import type { MediaCenterCredentialBinding } from "@orbitalvue/media-centers";

export interface ProtectedMediaCredential {
  schemaVersion: 1;
  binding: MediaCenterCredentialBinding;
  accessToken: string;
}

export type CredentialPersistence = "device-secure" | "session-only";

export interface TelevisionCredentialVault {
  readonly persistence: CredentialPersistence;
  readonly securityLabel: string;
  read(credentialId: string): Promise<ProtectedMediaCredential | null>;
  write(record: ProtectedMediaCredential): Promise<void>;
  remove(credentialId: string): Promise<void>;
}

const TIZEN_PREFIX = "OrbitalVueMedia_";
const WEBOS_KEY_NAME = "OrbitalVueMediaVaultAes256";
const WEBOS_SERVICE_URI = "luna://com.webos.service.keymanager3";

export function createTelevisionCredentialVault(): TelevisionCredentialVault {
  if (window.tizen?.keymanager) return new TizenCredentialVault(window.tizen.keymanager);
  if (window.webOS?.service) {
    return new ResilientCredentialVault(
      new WebOsCredentialVault(window.webOS.service),
      new SessionCredentialVault()
    );
  }
  return new SessionCredentialVault();
}

export class SessionCredentialVault implements TelevisionCredentialVault {
  readonly persistence = "session-only" as const;
  readonly securityLabel = "Protected for this app session; reconnect after restarting this television.";
  private readonly records = new Map<string, ProtectedMediaCredential>();

  async read(credentialId: string): Promise<ProtectedMediaCredential | null> {
    const record = this.records.get(credentialId);
    return record ? cloneAndValidate(record, credentialId) : null;
  }

  async write(record: ProtectedMediaCredential): Promise<void> {
    const validated = cloneAndValidate(record, record.binding.credentialId);
    this.records.set(validated.binding.credentialId, validated);
  }

  async remove(credentialId: string): Promise<void> {
    this.records.delete(credentialId);
  }
}

class TizenCredentialVault implements TelevisionCredentialVault {
  readonly persistence = "device-secure" as const;
  readonly securityLabel = "Protected by Samsung Tizen KeyManager on this television.";

  constructor(private readonly keyManager: NonNullable<SamsungTizen["keymanager"]>) {}

  async read(credentialId: string): Promise<ProtectedMediaCredential | null> {
    const name = tizenName(credentialId);
    try {
      return parseCredential(this.keyManager.getData({ name }, null), credentialId);
    } catch (error) {
      if (isMissingVaultEntry(error)) return null;
      throw new Error("Samsung secure storage could not open the media-server credential.");
    }
  }

  async write(record: ProtectedMediaCredential): Promise<void> {
    const validated = cloneAndValidate(record, record.binding.credentialId);
    const name = tizenName(validated.binding.credentialId);
    try {
      this.keyManager.removeData({ name });
    } catch (error) {
      if (!isMissingVaultEntry(error)) {
        throw new Error("Samsung secure storage could not replace the media-server credential.");
      }
    }
    await new Promise<void>((resolve, reject) => {
      this.keyManager.saveData(
        name,
        JSON.stringify(validated),
        null,
        resolve,
        () => reject(new Error("Samsung secure storage could not save the media-server credential."))
      );
    });
  }

  async remove(credentialId: string): Promise<void> {
    try {
      this.keyManager.removeData({ name: tizenName(credentialId) });
    } catch (error) {
      if (!isMissingVaultEntry(error)) {
        throw new Error("Samsung secure storage could not remove the media-server credential.");
      }
    }
  }
}

class ResilientCredentialVault implements TelevisionCredentialVault {
  private usingFallback = false;

  constructor(
    private readonly primary: TelevisionCredentialVault,
    private readonly fallback: TelevisionCredentialVault
  ) {}

  get persistence(): CredentialPersistence {
    return this.usingFallback ? this.fallback.persistence : this.primary.persistence;
  }

  get securityLabel(): string {
    return this.usingFallback ? this.fallback.securityLabel : this.primary.securityLabel;
  }

  async read(credentialId: string): Promise<ProtectedMediaCredential | null> {
    if (this.usingFallback) return this.fallback.read(credentialId);
    try {
      return await this.primary.read(credentialId);
    } catch {
      this.usingFallback = true;
      return this.fallback.read(credentialId);
    }
  }

  async write(record: ProtectedMediaCredential): Promise<void> {
    if (!this.usingFallback) {
      try {
        await this.primary.write(record);
        return;
      } catch {
        this.usingFallback = true;
      }
    }
    await this.fallback.write(record);
  }

  async remove(credentialId: string): Promise<void> {
    await Promise.allSettled([
      this.primary.remove(credentialId),
      this.fallback.remove(credentialId)
    ]);
  }
}

interface SealedWebOsRecord {
  credentialId: string;
  iv: string;
  ciphertext: string;
  savedAt: string;
}

class WebOsCredentialVault implements TelevisionCredentialVault {
  readonly persistence = "device-secure" as const;
  readonly securityLabel = "Protected by the LG webOS trusted-execution key manager.";
  private readonly store = new WebOsSealedRecordStore();

  constructor(private readonly service: WebOsService) {}

  async read(credentialId: string): Promise<ProtectedMediaCredential | null> {
    const sealed = await this.store.read(credentialId);
    if (!sealed) return null;
    await this.ensureKey();
    const begin = await this.call("begin", {
      name: WEBOS_KEY_NAME,
      params: {
        type: "AES",
        mode: ["GCM"],
        purpose: ["decrypt"],
        padding: ["NONE"],
        iv: sealed.iv
      }
    });
    const handle = requiredResponseString(begin.handle, "LG secure-storage operation handle");
    try {
      const finished = await this.call("finish", {
        handle,
        data: sealed.ciphertext,
        aad: base64Encode(credentialId)
      });
      return parseCredential(
        base64Decode(requiredResponseString(finished.output, "LG secure-storage plaintext")),
        credentialId
      );
    } catch (error) {
      await this.abort(handle);
      throw error;
    }
  }

  async write(record: ProtectedMediaCredential): Promise<void> {
    const validated = cloneAndValidate(record, record.binding.credentialId);
    await this.ensureKey();
    const begin = await this.call("begin", {
      name: WEBOS_KEY_NAME,
      params: {
        type: "AES",
        mode: ["GCM"],
        purpose: ["encrypt"],
        padding: ["NONE"]
      }
    });
    const handle = requiredResponseString(begin.handle, "LG secure-storage operation handle");
    try {
      const finished = await this.call("finish", {
        handle,
        data: base64Encode(JSON.stringify(validated)),
        aad: base64Encode(validated.binding.credentialId)
      });
      await this.store.write({
        credentialId: validated.binding.credentialId,
        iv: requiredResponseString(begin.iv, "LG secure-storage initialization vector"),
        ciphertext: requiredResponseString(finished.output, "LG secure-storage ciphertext"),
        savedAt: new Date().toISOString()
      });
    } catch (error) {
      await this.abort(handle);
      throw error;
    }
  }

  async remove(credentialId: string): Promise<void> {
    await this.store.remove(credentialId);
  }

  private async ensureKey(): Promise<void> {
    try {
      await this.call("generateKey", {
        name: WEBOS_KEY_NAME,
        params: {
          type: "AES",
          size: 256,
          mode: ["GCM"],
          purpose: ["encrypt", "decrypt"],
          padding: ["NONE"]
        }
      });
    } catch (error) {
      if (!isExistingWebOsKey(error)) throw error;
    }
  }

  private call(method: string, parameters: Record<string, unknown>): Promise<WebOsServiceResponse> {
    return new Promise((resolve, reject) => {
      this.service.request(WEBOS_SERVICE_URI, {
        method,
        parameters,
        onSuccess: (response) => {
          if (response.returnValue === false) reject(webOsError(response));
          else resolve(response);
        },
        onFailure: (error) => reject(webOsError(error))
      });
    });
  }

  private async abort(handle: string): Promise<void> {
    try {
      await this.call("abort", { handle });
    } catch {
      // The handle is already invalid after a successful finish.
    }
  }
}

class WebOsSealedRecordStore {
  private databasePromise: Promise<IDBDatabase> | undefined;

  async read(credentialId: string): Promise<SealedWebOsRecord | null> {
    const database = await this.open();
    return new Promise((resolve, reject) => {
      const transaction = database.transaction("sealed", "readonly");
      const request = transaction.objectStore("sealed").get(credentialId);
      request.onsuccess = () => resolve((request.result as SealedWebOsRecord | undefined) ?? null);
      request.onerror = () => reject(request.error ?? new Error("LG protected storage could not be read."));
    });
  }

  async write(record: SealedWebOsRecord): Promise<void> {
    const database = await this.open();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction("sealed", "readwrite");
      transaction.objectStore("sealed").put(record);
      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error ?? new Error("LG protected storage could not be saved."));
      transaction.onabort = () => reject(transaction.error ?? new Error("LG protected storage was interrupted."));
    });
  }

  async remove(credentialId: string): Promise<void> {
    const database = await this.open();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction("sealed", "readwrite");
      transaction.objectStore("sealed").delete(credentialId);
      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error ?? new Error("LG protected storage could not be cleared."));
    });
  }

  private open(): Promise<IDBDatabase> {
    if (this.databasePromise) return this.databasePromise;
    this.databasePromise = new Promise((resolve, reject) => {
      if (!("indexedDB" in window)) {
        reject(new Error("LG protected storage is unavailable on this television."));
        return;
      }
      const request = indexedDB.open("orbitalvue-tv-vault-v1", 1);
      request.onupgradeneeded = () => {
        if (!request.result.objectStoreNames.contains("sealed")) {
          request.result.createObjectStore("sealed", { keyPath: "credentialId" });
        }
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error ?? new Error("LG protected storage is unavailable."));
      request.onblocked = () => reject(new Error("LG protected storage is temporarily locked."));
    });
    return this.databasePromise;
  }
}

function tizenName(credentialId: string): string {
  return `${TIZEN_PREFIX}${safeCredentialId(credentialId)}`;
}

function cloneAndValidate(
  record: ProtectedMediaCredential,
  expectedCredentialId: string
): ProtectedMediaCredential {
  return parseCredential(JSON.stringify(record), expectedCredentialId);
}

function parseCredential(serialized: string, expectedCredentialId: string): ProtectedMediaCredential {
  const parsed = JSON.parse(serialized) as Partial<ProtectedMediaCredential>;
  const binding = parsed.binding;
  const accessToken = parsed.accessToken;
  if (parsed.schemaVersion !== 1 || !binding || binding.credentialId !== expectedCredentialId) {
    throw new TypeError("The protected media-server credential is incomplete or belongs to another source.");
  }
  if (typeof accessToken !== "string" || !accessToken.trim() || /[\r\n]/.test(accessToken) || accessToken.length > 16_384) {
    throw new TypeError("The protected media-server token is invalid.");
  }
  return JSON.parse(JSON.stringify(parsed)) as ProtectedMediaCredential;
}

function safeCredentialId(value: string): string {
  const trimmed = value.trim();
  if (!/^[A-Za-z0-9._:-]{1,256}$/.test(trimmed)) {
    throw new TypeError("The secure credential reference is invalid.");
  }
  return trimmed;
}

function isMissingVaultEntry(error: unknown): boolean {
  const message = typeof error === "object" && error !== null
    ? `${"name" in error ? String(error.name) : ""} ${"message" in error ? String(error.message) : ""}`
    : String(error);
  return /NotFound|not found|does not exist/i.test(message);
}

function webOsError(response: WebOsServiceResponse): Error & { code?: number } {
  const error = new Error(response.errorText || "LG secure storage is not available on this television.") as Error & { code?: number };
  if (response.errorCode !== undefined) error.code = response.errorCode;
  return error;
}

function isExistingWebOsKey(error: unknown): boolean {
  return typeof error === "object" && error !== null && "code" in error && error.code === -10002;
}

function requiredResponseString(value: string | undefined, label: string): string {
  if (!value) throw new Error(`${label} was not returned.`);
  return value;
}

function base64Encode(value: string): string {
  const bytes = new TextEncoder().encode(value);
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary);
}

function base64Decode(value: string): string {
  const binary = atob(value);
  const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
  return new TextDecoder().decode(bytes);
}
