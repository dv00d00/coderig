import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = fileURLToPath(new URL(".", import.meta.url));

export default defineConfig({
  plugins: [react()],
  define: {
    "process.env.NODE_ENV": JSON.stringify("production"),
  },
  build: {
    outDir: resolve(here, "../wwwroot/assets"),
    emptyOutDir: false,
    cssCodeSplit: false,
    lib: {
      entry: resolve(here, "src/file-diff.tsx"),
      formats: ["es"],
      fileName: () => "file-diff.js",
    },
    rollupOptions: {
      output: {
        assetFileNames: (asset) =>
          asset.name?.endsWith(".css") ? "file-diff.css" : "[name][extname]",
      },
    },
  },
});
