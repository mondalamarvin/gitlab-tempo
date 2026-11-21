import path from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// Dev: Vite on :5173 proxies /api to the .NET API on :5080.
// Build: output goes straight into the API's wwwroot so one `dotnet publish` ships everything.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { "@": path.resolve(__dirname, "src") },
  },
  server: {
    port: 5173,
    // The .NET SPA proxy waits for exactly this port, so fail instead of picking another one.
    strictPort: true,
    proxy: {
      "/api": { target: "http://localhost:5080", changeOrigin: true },
      // Sign in with GitLab. The Host header is kept so the OAuth redirect URI is
      // http://localhost:5173/signin-gitlab and the whole flow stays on the dev server.
      "/auth": { target: "http://localhost:5080", changeOrigin: false },
      "/signin-gitlab": { target: "http://localhost:5080", changeOrigin: false },
    },
  },
  build: {
    outDir: "../server/Tempo.Api/wwwroot",
    emptyOutDir: true,
  },
});
