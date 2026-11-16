import { act, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Edge, SourceStatus } from "@/protocol/messages";
import { initialState } from "@/state/appState";
import { useAppStore } from "@/state/store";
import type { SourceClock } from "@/time/sourceClock";
import type { LaneName } from "./model";
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

/** A source's status message in the given state. */
const status = (state: SourceStatus["state"]): SourceStatus => ({
  source: "synthetic",
  name: "Synthetic",
  state,
  detail: "",
});

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

/** Renders the view over a recording context. Without `show` it draws every lane. */
function mount(
  clock: SourceClock = clockAt(12_000_000),
  show?: readonly LaneName[],
) {
  const { ctx, painted } = recordingContext();
  vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(ctx);
  const view = render(<SignalView clock={clock} show={show} />);
  return {
    canvas: view.container.querySelector("canvas")!,
    unmount: view.unmount,
    /** What was painted since the mount, or since the last frame() began. */
    painted,
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

  it("sizes the canvas in device pixels and scales the drawing to match", () => {
    vi.stubGlobal("devicePixelRatio", 2);
    const { canvas, frame } = mount();
    reportSize(612, 50);

    expect(canvas.width).toBe(1224);
    expect(canvas.height).toBe(100);
    // The drawing stays in CSS pixels; the transform is what doubles it.
    expect(frame().slice(0, 2)).toEqual([
      { op: "transform", matrix: [2, 0, 0, 2, 0, 0] },
      { op: "clear", x: 0, y: 0, w: 612, h: 50 },
    ]);
  });

  it("keeps the scale the canvas was sized for until the next resize", () => {
    vi.stubGlobal("devicePixelRatio", 2);
    const { frame } = mount();
    reportSize(612, 50);

    // The window moves to another display, and no resize follows.
    vi.stubGlobal("devicePixelRatio", 1);

    expect(frame()[0]).toEqual({ op: "transform", matrix: [2, 0, 0, 2, 0, 0] });
  });

  it("follows a resize, and the rem that changes with it", () => {
    const { frame } = mount();
    reportSize(1212, 100);
    expect(frame().at(-1)).toMatchObject({ path: ["M 1200 0", "L 1200 100"] });

    document.documentElement.style.fontSize = "32px";
    reportSize(624, 50);

    // The gap right of the cursor is in rems: 12 px before, 24 px now.
    expect(frame().at(-1)).toMatchObject({ path: ["M 600 0", "L 600 50"] });
  });

  it("reads the colors again when the theme changes", async () => {
    const { frame } = mount();
    reportSize(1212, 100);
    expect(frame().at(-1)).toMatchObject({ strokeStyle: "signal" });

    document.documentElement.classList.add("dark");
    // A MutationObserver reports in a microtask.
    await Promise.resolve();

    expect(frame().at(-1)).toMatchObject({ strokeStyle: "dark-signal" });
  });

  it("stops painting and stops watching the page when it unmounts", () => {
    const stopWatchingTheme = vi.spyOn(
      MutationObserver.prototype,
      "disconnect",
    );
    const { canvas, frame, unmount } = mount();
    reportSize(1212, 100);

    unmount();

    expect(frame()).toEqual([]);
    // A size reported now no longer reaches the canvas.
    reportSize(10, 10);
    expect(canvas.width).toBe(1212);
    expect(stopWatchingTheme).toHaveBeenCalledOnce();
  });

  it("renders without a context, as in jsdom, and starts no loop", () => {
    const requestFrame = vi.spyOn(globalThis, "requestAnimationFrame");

    render(<SignalView clock={clockAt(12_000_000)} />);

    expect(requestFrame).not.toHaveBeenCalled();
  });

  it("names the lanes beside the canvas, each as tall as its lane", () => {
    render(<SignalView clock={clockAt(12_000_000)} />);

    const names = within(
      screen.getByRole("list", { name: "Lanes" }),
    ).getAllByRole("listitem");

    expect(names.map((name) => name.textContent)).toEqual([
      "Carrier",
      "Pulse",
      "Symbol",
      "Second",
    ]);
    // The carrier's lane is twice as tall as the others, on the canvas and here.
    expect(names.map((name) => name.style.flexGrow)).toEqual([
      "2",
      "1",
      "1",
      "1",
    ]);
  });

  it("draws and names only the lanes it is asked to show", () => {
    useAppStore.setState({ edges });
    const { frame } = mount(clockAt(12_000_000), ["carrier"]);
    reportSize(1212, 100);

    // The carrier has the whole height: the dip is at 94, where four lanes put it at 34.
    expect(frame()).toContainEqual({
      op: "stroke",
      path: ["M 100 94", "L 180 94", "L 180 6", "L 1200 6"],
      strokeStyle: "signal",
      lineWidth: 1.5,
    });
    const names = within(
      screen.getByRole("list", { name: "Lanes" }),
    ).getAllByRole("listitem");
    expect(names.map((name) => name.textContent)).toEqual(["Carrier"]);
  });

  it("asks for a source until one runs, and not over a picture a source left", () => {
    render(<SignalView clock={clockAt(12_000_000)} />);
    const hint = () => screen.queryByText("Pick a source and press Start");
    expect(hint()).toBeInTheDocument();

    act(() => useAppStore.setState({ sourceStatus: status("running") }));
    expect(hint()).not.toBeInTheDocument();

    // The source stops and its last picture stays, so nothing is written over it.
    act(() => useAppStore.setState({ edges, sourceStatus: status("stopped") }));
    expect(hint()).not.toBeInTheDocument();
  });

  it("paints at once on a resize, without waiting for the next animation frame", () => {
    useAppStore.setState({ edges });
    const { painted } = mount();

    reportSize(1212, 100);

    // Sizing a canvas clears it, and the browser shows that frame before the loop runs again.
    expect(painted).toContainEqual({
      op: "clear",
      x: 0,
      y: 0,
      w: 1212,
      h: 100,
    });
    expect(painted.at(-1)).toEqual({
      op: "stroke",
      path: ["M 1200 0", "L 1200 100"],
      strokeStyle: "signal",
      lineWidth: 1,
    });
  });
});
