import { resolve } from "node:path";
import { defineConfig } from "vite";

export default defineConfig({
  root: resolve(import.meta.dirname),
  build: {
    outDir: resolve(import.meta.dirname, "../artifacts/public-site"),
    emptyOutDir: true,
    rollupOptions: {
      input: {
        overview: resolve(import.meta.dirname, "index.html"),
        privacy: resolve(import.meta.dirname, "privacy.html"),
        support: resolve(import.meta.dirname, "support.html"),
        notFound: resolve(import.meta.dirname, "404.html")
      }
    }
  }
});
