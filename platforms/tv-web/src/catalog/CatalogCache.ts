import type { OrbitalVueCatalog } from "@orbitalvue/catalog";
import type { MediaCenterSnapshot } from "@orbitalvue/media-centers";

export interface SavedCatalogRecord {
  key: "active";
  sourceUrl: string | null;
  catalog: OrbitalVueCatalog;
  savedAt: string;
  sourceKind?: "playlist-url" | "playlist-file" | "media-center";
  mediaCenterSnapshot?: MediaCenterSnapshot;
}

const DATABASE_NAME = "orbitalvue-tv-catalog-v1";
const STORE_NAME = "catalogs";

export interface CatalogStore {
  read(): Promise<SavedCatalogRecord | null>;
  write(sourceUrl: string | null, catalog: OrbitalVueCatalog): Promise<void>;
  writeMediaCenter(snapshot: MediaCenterSnapshot, catalog: OrbitalVueCatalog): Promise<void>;
  clear(): Promise<void>;
}

export class CatalogCache implements CatalogStore {
  private databasePromise: Promise<IDBDatabase> | undefined;

  async read(): Promise<SavedCatalogRecord | null> {
    if (!("indexedDB" in window)) return null;
    const database = await this.open();
    return new Promise((resolve, reject) => {
      const transaction = database.transaction(STORE_NAME, "readonly");
      const request = transaction.objectStore(STORE_NAME).get("active");
      request.onsuccess = () => resolve((request.result as SavedCatalogRecord | undefined) ?? null);
      request.onerror = () => reject(request.error ?? new Error("The saved playlist could not be read."));
    });
  }

  async write(sourceUrl: string | null, catalog: OrbitalVueCatalog): Promise<void> {
    await this.writeRecord({
      key: "active",
      sourceUrl,
      catalog,
      savedAt: new Date().toISOString(),
      sourceKind: sourceUrl ? "playlist-url" : "playlist-file"
    });
  }

  async writeMediaCenter(
    snapshot: MediaCenterSnapshot,
    catalog: OrbitalVueCatalog
  ): Promise<void> {
    await this.writeRecord({
      key: "active",
      sourceUrl: null,
      catalog,
      savedAt: new Date().toISOString(),
      sourceKind: "media-center",
      mediaCenterSnapshot: snapshot
    });
  }

  private async writeRecord(record: SavedCatalogRecord): Promise<void> {
    if (!("indexedDB" in window)) return;
    const database = await this.open();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(STORE_NAME, "readwrite");
      transaction.objectStore(STORE_NAME).put(record);
      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error ?? new Error("The playlist could not be saved."));
      transaction.onabort = () => reject(transaction.error ?? new Error("Saving the playlist was interrupted."));
    });
  }

  async clear(): Promise<void> {
    if (!("indexedDB" in window)) return;
    const database = await this.open();
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(STORE_NAME, "readwrite");
      transaction.objectStore(STORE_NAME).delete("active");
      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error ?? new Error("The saved playlist could not be removed."));
    });
  }

  private open(): Promise<IDBDatabase> {
    if (this.databasePromise) return this.databasePromise;
    this.databasePromise = new Promise((resolve, reject) => {
      const request = indexedDB.open(DATABASE_NAME, 1);
      request.onupgradeneeded = () => {
        const database = request.result;
        if (!database.objectStoreNames.contains(STORE_NAME)) database.createObjectStore(STORE_NAME, { keyPath: "key" });
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error ?? new Error("Private television storage is unavailable."));
      request.onblocked = () => reject(new Error("Private television storage is temporarily locked."));
    });
    return this.databasePromise;
  }
}
