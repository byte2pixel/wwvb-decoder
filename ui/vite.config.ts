import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // The WPF host probes this exact port in Debug; fail rather than drift to another.
    port: 5173,
    strictPort: true,
  },
  test: {
    // junit for the CI test-results check; test-results/ and coverage/ are git-ignored.
    reporters: ["default", "junit"],
    outputFile: { junit: "test-results/junit.xml" },
    coverage: {
      provider: "v8",
      include: ["src/**"],
      exclude: ["src/**/*.test.*", "src/main.tsx"],
      reporter: ["text", "lcov", "cobertura"],
      reportsDirectory: "coverage",
      // A ratchet, a few points under what the suite covers today; raise it as tests land.
      thresholds: { lines: 65, statements: 65, branches: 40, functions: 40 },
    },
  },
});
