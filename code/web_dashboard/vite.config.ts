import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Khi dev, Vite đóng vai proxy cùng nguồn giống Nginx trong Docker: trình duyệt chỉ gọi
// một địa chỉ, nên không vướng CORS (Zone Service không bật CORS).
const backend = process.env.BACKEND_URL ?? "http://localhost:5250";
const zone = process.env.ZONE_URL ?? "http://localhost:8010";
const ai = process.env.AI_URL ?? "http://localhost:8003";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 3000,
    proxy: {
      "/api/v1": { target: backend, changeOrigin: true },
      "/hubs": { target: backend, changeOrigin: true, ws: true },
      "/zone": { target: zone, changeOrigin: true, rewrite: (p) => p.replace(/^\/zone/, "") },
      "/ai": { target: ai, changeOrigin: true, rewrite: (p) => p.replace(/^\/ai/, "") },
    },
  },
});
