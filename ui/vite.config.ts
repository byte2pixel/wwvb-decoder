import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // The WPF host probes this exact port in Debug; fail rather than drift to another.
    port: 5173,
    strictPort: true,
  },
});
