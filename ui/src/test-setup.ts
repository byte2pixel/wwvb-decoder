import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

// No vitest globals in this repo, so Testing Library cannot register its own cleanup.
afterEach(cleanup);
