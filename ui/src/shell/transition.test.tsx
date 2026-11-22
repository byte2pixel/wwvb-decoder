import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useAppStore } from "@/state/store";
import {
  focusStage,
  prefersReducedMotion,
  withViewTransition,
} from "./transition";

/** Makes the page answer the reduced-motion query this way. jsdom has no matchMedia of its own. */
const reducedMotion = (matches: boolean) =>
  vi.stubGlobal(
    "matchMedia",
    vi.fn((query: string) => ({
      matches: matches && query === "(prefers-reduced-motion: reduce)",
    })),
  );

/**
 * Gives the document a View Transitions API that runs its callback at once, as the browser
 * does a moment later. Returns the spy.
 */
const viewTransitions = (ready: Promise<void> = Promise.resolve()) => {
  const start = vi.fn((callback: () => void) => {
    callback();
    return { ready };
  });
  Object.defineProperty(document, "startViewTransition", {
    value: start,
    configurable: true,
  });
  return start;
};

afterEach(() => {
  vi.unstubAllGlobals();
  Reflect.deleteProperty(document, "startViewTransition");
});

describe("prefersReducedMotion", () => {
  it("is false where the page cannot ask", () => {
    expect(prefersReducedMotion()).toBe(false);
  });

  it("is the system's answer where it can", () => {
    reducedMotion(true);
    expect(prefersReducedMotion()).toBe(true);

    reducedMotion(false);
    expect(prefersReducedMotion()).toBe(false);
  });
});

describe("withViewTransition", () => {
  it("makes the change at once where there is no View Transitions API", () => {
    const update = vi.fn();

    withViewTransition(update);

    expect(update).toHaveBeenCalledTimes(1);
  });

  it("makes the change inside a view transition where there is one", () => {
    const start = viewTransitions();
    const update = vi.fn();

    withViewTransition(update);

    expect(start).toHaveBeenCalledTimes(1);
    expect(update).toHaveBeenCalledTimes(1);
  });

  it("makes the change at once, with no transition, under reduced motion", () => {
    const start = viewTransitions();
    reducedMotion(true);
    const update = vi.fn();

    withViewTransition(update);

    expect(start).not.toHaveBeenCalled();
    expect(update).toHaveBeenCalledTimes(1);
  });

  it("does not report a transition that a newer one skipped", async () => {
    const unhandled = vi.fn();
    process.on("unhandledRejection", unhandled);
    viewTransitions(Promise.reject(new Error("Transition was skipped")));

    withViewTransition(vi.fn());
    // A rejection nobody handled is reported after the microtasks and a turn of the timers.
    await new Promise((resolve) => setTimeout(resolve, 0));
    process.off("unhandledRejection", unhandled);

    expect(unhandled).not.toHaveBeenCalled();
  });
});

describe("focusStage", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("opens a stage's focus view, and the overview again", () => {
    focusStage(4);
    expect(useAppStore.getState().focus).toBe(4);

    focusStage(null);
    expect(useAppStore.getState().focus).toBeNull();
  });

  it("changes the view inside a view transition", () => {
    const start = viewTransitions();

    focusStage(4);

    expect(start).toHaveBeenCalledTimes(1);
    expect(useAppStore.getState().focus).toBe(4);
  });

  it("runs no transition to the view that is already on show", () => {
    const start = viewTransitions();
    useAppStore.setState({ focus: 4 });

    focusStage(4);

    expect(start).not.toHaveBeenCalled();
    expect(useAppStore.getState().focus).toBe(4);
  });
});
