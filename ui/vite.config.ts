import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import path from "node:path";
import { defineConfig } from "vitest/config";

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { "@": path.resolve(import.meta.dirname, "./src") } },
  server: {
    // The WPF host probes this exact port in Debug; fail rather than drift to another.
    port: 5173,
    strictPort: true,
  },
  test: {
    // The file extension picks the environment. Pure logic (*.test.ts) runs in Node; jsdom
    // would swap globals such as URL under it. Component tests (*.test.tsx) get a DOM.
    projects: [
      {
        extends: true,
        test: {
          name: "unit",
          environment: "node",
          include: ["src/**/*.test.ts"],
        },
      },
      {
        extends: true,
        test: {
          name: "dom",
          environment: "jsdom",
          include: ["src/**/*.test.tsx"],
          setupFiles: ["src/test-setup.ts"],
        },
      },
    ],
    // junit for the CI test-results check; test-results/ and coverage/ are git-ignored.
    reporters: ["default", "junit"],
    outputFile: { junit: "test-results/junit.xml" },
    coverage: {
      provider: "v8",
      include: ["src/**"],
      exclude: [
        "src/**/*.test.*",
        "src/components/ui/**",
        "src/main.tsx",
        "src/test-setup.ts",
      ],
      reporter: ["text", "lcov", "cobertura"],
      reportsDirectory: "coverage",
      // A ratchet, a few points under what the suite covers today; raise it as tests land.
      thresholds: { lines: 65, statements: 65, branches: 40, functions: 40 },
    },
  },
});
