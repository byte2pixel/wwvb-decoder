import { render } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Edge } from "@/protocol/messages";
import { initialState } from "@/state/appState";
import { useAppStore } from "@/state/store";
import type { SourceClock } from "@/time/sourceClock";
import { recordingContext } from "./recordingContext";
import { SignalView } from "./SignalView";

/** A clock stopped at one moment. */
const clockAt = (micros: number): SourceClock => ({
  now: () => micros,
  speed: () => 1,
  sync: () => {},
  setSpeed: () => {},
  reset: () => {},
});

/** 0.8 s of reduced carrier, one second into a window that ends at 12 s. High is reduced. */
const edges: Edge[] = [
  { micros: 1_000_000, level: "high" },
  { micros: 1_800_000, level: "low" },
];

/**
 * jsdom lays nothing out, so the test plays the browser: reportSize tells the view's
 * observer how big its canvas is, in CSS pixels.
 */
let reportSize: (width: number, height: number) => void = () => {};

class FakeResizeObserver {
  callback: ResizeObserverCallback;

  constructor(callback: ResizeObserverCallback) {
    this.callback = callback;
  }

  observe(target: Element): void {
    reportSize = (width, height) =>
      this.callback(
        [{ target, contentRect: { width, height } } as ResizeObserverEntry],
        this,
      );
  }

  unobserve(): void {}

  disconnect(): void {
    reportSize = () => {};
  }
}

/** Renders the view over a recording context. */
function mount(clock: SourceClock = clockAt(12_000_000)) {
  const { ctx, painted } = recordingContext();
  vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(ctx);
  const view = render(<SignalView clock={clock} />);
  return {
    canvas: view.container.querySelector("canvas")!,
    unmount: view.unmount,
    /** Runs one animation frame and returns what it painted. */
    frame: () => {
      painted.length = 0;
      vi.advanceTimersToNextFrame();
      return [...painted];
    },
  };
}

describe("SignalView", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.stubGlobal("ResizeObserver", FakeResizeObserver);
    useAppStore.setState(initialState);
    // 16 px to the rem, and each color the view asserts on holds its variable's name.
    const sheet = document.createElement("style");
    sheet.textContent = `
      :root { font-size: 16px; --signal: signal; }
      .dark { --signal: dark-signal; }
    `;
    document.head.append(sheet);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    document.head.replaceChildren();
    document.documentElement.removeAttribute("class");
    document.documentElement.removeAttribute("style");
  });

  it("clears the canvas at its size on every animation frame", () => {
    const { frame } = mount();
    reportSize(1212, 100);

    const clear = { op: "clear", x: 0, y: 0, w: 1212, h: 100 };
    expect(frame()).toContainEqual(clear);
    expect(frame()).toContainEqual(clear);
  });

  it("paints the store's signal up to the present", () => {
    useAppStore.setState({ edges });
    const { frame } = mount();
    reportSize(1212, 100);

    const painted = frame();

    // 1,200 px for 12 s. The dip runs from 100 to 180, low in a carrier lane 40 px high.
    expect(painted).toContainEqual({
      op: "stroke",
      path: ["M 100 34", "L 180 34", "L 180 6", "L 1200 6"],
      strokeStyle: "signal",
      lineWidth: 1.5,
    });
    // The cursor at the present goes on last.
    expect(painted.at(-1)).toEqual({
      op: "stroke",
      path: ["M 1200 0", "L 1200 100"],
      strokeStyle: "signal",
      lineWidth: 1,
    });
  });
});
