import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

// No vitest globals in this repo, so Testing Library cannot register its own cleanup.
afterEach(cleanup);

// jsdom has no ResizeObserver. A view that sizes itself gets one that never reports;
// a test that needs sizes stubs its own.
globalThis.ResizeObserver ??= class {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
};

// jsdom has no canvas either, and prints "Not implemented" on every getContext call.
// A canvas view gets no context here; a test that needs painting stubs its own.
HTMLCanvasElement.prototype.getContext = () => null;
