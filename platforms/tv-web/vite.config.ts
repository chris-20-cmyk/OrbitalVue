import { defineConfig } from "vite";

export default defineConfig({
  base: "./",
  build: {
    target: "es2017",
    outDir: "dist/web",
    emptyOutDir: true,
    cssCodeSplit: false,
    sourcemap: true,
    reportCompressedSize: true
  },
  server: {
    strictPort: true
  }
});
