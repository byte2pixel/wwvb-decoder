import { describe, expect, it } from "vitest";
import type { SignalLanes } from "./lanes";
import { drawSignal, type SignalTokens, type SignalView } from "./model";

/** Token names stand in for colors, so an assertion reads as the design does. */
const tokens: SignalTokens = {
  line: "line",
  lineStrong: "lineStrong",
  signal: "signal",
  onSignal: "onSignal",
  zero: "zero",
  zeroSurface: "zeroSurface",
  marker: "marker",
  markerSurface: "markerSurface",
  bad: "bad",
  textSoft: "textSoft",
  textMuted: "textMuted",
};

/** A 12 s window that starts at zero. */
const lanes = (over: Partial<SignalLanes> = {}): SignalLanes => ({
  window: { startMicros: 0, endMicros: 12_000_000 },
  carrier: [],
  pulses: [],
  symbols: [],
  seconds: [],
  ticks: [],
  cursorMicros: null,
  ...over,
});

/**
 * 12 px stay free right of the cursor, which leaves 1,200 px for 12 s: 100 px a second.
 * With all four lanes in 100 px, the carrier has 0 to 40, the pulses 40 to 60,
 * the symbols 60 to 80, and the seconds 80 to 100.
 */
const view = (over: Partial<SignalView> = {}): SignalView => ({
  width: 1212,
  height: 100,
  rem: 16,
  show: ["carrier", "pulse", "symbol", "second"],
  labels: true,
  tokens,
  ...over,
});

describe("drawSignal", () => {
  it("maps the window onto the width, leaving room right of the cursor", () => {
    const drawing = drawSignal(
      lanes({ ticks: [0, 1_000_000, 12_000_000] }),
      view(),
    );

    expect(drawing.ticks).toEqual([
      { x1: 0, y1: 0, x2: 0, y2: 100, stroke: "line", width: 1 },
      { x1: 100, y1: 0, x2: 100, y2: 100, stroke: "line", width: 1 },
      { x1: 1200, y1: 0, x2: 1200, y2: 100, stroke: "line", width: 1 },
    ]);
  });

  it("draws nothing when the lanes have no window", () => {
    const drawing = drawSignal(lanes({ window: null }), view());

    expect(drawing).toEqual({
      ticks: [],
      carrier: null,
      pulses: [],
      symbols: [],
      seconds: [],
      cursor: null,
    });
  });

  it("draws the cursor at the present", () => {
    const drawing = drawSignal(lanes({ cursorMicros: 12_000_000 }), view());

    expect(drawing.cursor).toEqual({
      x1: 1200,
      y1: 0,
      x2: 1200,
      y2: 100,
      stroke: "signal",
      width: 1,
    });
  });

  it("has no cursor in a still picture", () => {
    expect(drawSignal(lanes(), view()).cursor).toBeNull();
  });
});

describe("carrier", () => {
  const twoLevels = lanes({
    carrier: [
      { startMicros: 1_000_000, endMicros: 1_800_000, reduced: true },
      { startMicros: 1_800_000, endMicros: 2_000_000, reduced: false },
    ],
  });

  it("is one line, with reduced carrier low", () => {
    const { carrier } = drawSignal(twoLevels, view());

    expect(carrier?.stroke).toBe("signal");
    const points = carrier?.points ?? [];
    expect(points.map((p) => p.x)).toEqual([100, 180, 180, 200]);
    const [reducedY, , fullY] = points.map((p) => p.y);
    expect(points.map((p) => p.y)).toEqual([reducedY, reducedY, fullY, fullY]);
    // Canvas y grows downward, so low on screen is the larger y. The carrier lane is the top 40 px.
    expect(reducedY).toBeGreaterThan(fullY);
    expect(fullY).toBeGreaterThanOrEqual(0);
    expect(reducedY).toBeLessThanOrEqual(40);
  });

  it("is absent when there are no edges", () => {
    expect(drawSignal(lanes(), view()).carrier).toBeNull();
  });
});

describe("pulses", () => {
  const measured = { startMicros: 1_000_000, endMicros: 1_800_000 };

  it("draws a bar as long as the pulse, with its duration under it", () => {
    const { pulses } = drawSignal(
      lanes({ pulses: [{ ...measured, durationMs: 800 }] }),
      view(),
    );

    expect(pulses).toHaveLength(1);
    const { bar, label } = pulses[0];
    expect(bar).toMatchObject({ x: 100, w: 80, fill: "signal", stroke: null });
    expect(label).toMatchObject({
      x: 100,
      text: "800 ms",
      fill: "textSoft",
      align: "left",
    });
    // Both sit in the pulse lane, 40 to 60, the label below the bar.
    expect(bar.y).toBeGreaterThanOrEqual(40);
    expect(label?.y).toBeGreaterThan(bar.y + bar.h);
    expect(label?.y).toBeLessThan(60);
  });

  it("outlines a pending bar and says so", () => {
    const { pulses } = drawSignal(
      lanes({
        pulses: [
          { startMicros: 2_000_000, endMicros: 2_200_000, durationMs: null },
        ],
      }),
      view(),
    );

    expect(pulses[0].bar).toMatchObject({
      x: 200,
      w: 20,
      fill: null,
      stroke: "lineStrong",
    });
    expect(pulses[0].label).toMatchObject({
      text: "pending",
      fill: "textMuted",
    });
  });

  it("leaves the word off a pending sliver, which is how a glitch looks", () => {
    // 5 ms is half a pixel here.
    const { pulses } = drawSignal(
      lanes({
        pulses: [
          { startMicros: 2_000_000, endMicros: 2_005_000, durationMs: null },
        ],
      }),
      view(),
    );

    expect(pulses[0].bar.w).toBe(0.5);
    expect(pulses[0].label).toBeNull();
  });

  it("has no labels when they are turned off", () => {
    const { pulses } = drawSignal(
      lanes({ pulses: [{ ...measured, durationMs: 800 }] }),
      view({ labels: false }),
    );

    expect(pulses[0].bar).toMatchObject({ x: 100, w: 80 });
    expect(pulses[0].label).toBeNull();
  });

  it("lets a bar the window cuts run off the left edge, its label with it", () => {
    // The window starts at 0 and the pulse half a second before it.
    const { pulses } = drawSignal(
      lanes({
        pulses: [
          { startMicros: -500_000, endMicros: 300_000, durationMs: 800 },
        ],
      }),
      view(),
    );

    expect(pulses[0].bar).toMatchObject({ x: -50, w: 80 });
    expect(pulses[0].label).toMatchObject({ x: -50, text: "800 ms" });
  });
});

describe("symbols", () => {
  it("draws a chip for each symbol in its class's colors", () => {
    const { symbols } = drawSignal(
      lanes({
        symbols: [
          { startMicros: 1_000_000, kind: "zero" },
          { startMicros: 2_000_000, kind: "one" },
          { startMicros: 3_000_000, kind: "marker" },
          { startMicros: 4_000_000, kind: "error" },
        ],
      }),
      view(),
    );

    expect(symbols.map((chip) => chip.box.x)).toEqual([100, 200, 300, 400]);
    expect(symbols.map((chip) => chip.text.text)).toEqual(["0", "1", "M", "×"]);
    expect(symbols.map((chip) => chip.box.fill)).toEqual([
      "zeroSurface",
      "signal",
      "markerSurface",
      null,
    ]);
    expect(symbols.map((chip) => chip.box.stroke)).toEqual([
      null,
      null,
      null,
      "bad",
    ]);
    expect(symbols.map((chip) => chip.text.fill)).toEqual([
      "zero",
      "onSignal",
      "marker",
      "bad",
    ]);
  });

  it("centers each character in its chip, inside the symbol lane", () => {
    const { symbols } = drawSignal(
      lanes({ symbols: [{ startMicros: 1_000_000, kind: "one" }] }),
      view(),
    );

    const { box, text } = symbols[0];
    expect(text.align).toBe("center");
    expect(text.x).toBe(box.x + box.w / 2);
    expect(text.y).toBe(box.y + box.h / 2);
    // The symbol lane is 60 to 80.
    expect(box.y).toBeGreaterThanOrEqual(60);
    expect(box.y + box.h).toBeLessThanOrEqual(80);
  });

  it("spells out a pending chip, wider than a one-character chip", () => {
    const { symbols } = drawSignal(
      lanes({
        symbols: [
          { startMicros: 1_000_000, kind: "one" },
          { startMicros: 2_000_000, kind: "pending" },
        ],
      }),
      view(),
    );

    const [one, pending] = symbols;
    expect(pending.text).toMatchObject({ text: "pending", fill: "textMuted" });
    expect(pending.box).toMatchObject({ fill: null, stroke: "lineStrong" });
    expect(pending.box.w).toBeGreaterThan(one.box.w);
  });

  it("shortens a pending chip to an ellipsis when labels are off", () => {
    const { symbols } = drawSignal(
      lanes({ symbols: [{ startMicros: 1_000_000, kind: "pending" }] }),
      view({ labels: false }),
    );

    expect(symbols[0].text.text).toBe("…");
  });

  it("keeps neighbouring chips apart in the 60 s window", () => {
    // 1,200 px for 60 s is 20 px a second.
    const { symbols } = drawSignal(
      lanes({
        window: { startMicros: 0, endMicros: 60_000_000 },
        symbols: [
          { startMicros: 1_000_000, kind: "one" },
          { startMicros: 2_000_000, kind: "zero" },
        ],
      }),
      view({ labels: false }),
    );

    const [first, second] = symbols;
    expect(second.box.x - first.box.x).toBe(20);
    expect(first.box.x + first.box.w).toBeLessThan(second.box.x);
  });
});

describe("seconds", () => {
  const filled = lanes({
    seconds: [
      { startMicros: 1_000_000, second: 5 },
      { startMicros: 2_000_000, second: 17 },
      { startMicros: 3_000_000, second: null },
    ],
  });

  it("writes each second with two digits, and a dash for none", () => {
    const { seconds } = drawSignal(filled, view());

    expect(seconds.map((label) => label.text)).toEqual([":05", ":17", "—"]);
    expect(seconds.map((label) => label.x)).toEqual([100, 200, 300]);
    expect(seconds.map((label) => label.fill)).toEqual([
      "textSoft",
      "textSoft",
      "textMuted",
    ]);
    // The second lane is 80 to 100.
    for (const label of seconds) {
      expect(label.y).toBeGreaterThan(80);
      expect(label.y).toBeLessThan(100);
    }
  });

  it("is empty when labels are off", () => {
    expect(drawSignal(filled, view({ labels: false })).seconds).toEqual([]);
  });
});

describe("lanes shown", () => {
  const everything = lanes({
    carrier: [{ startMicros: 1_000_000, endMicros: 1_800_000, reduced: true }],
    pulses: [{ startMicros: 1_000_000, endMicros: 1_800_000, durationMs: 800 }],
    symbols: [{ startMicros: 1_000_000, kind: "marker" }],
    seconds: [{ startMicros: 1_000_000, second: 0 }],
    ticks: [1_000_000],
    cursorMicros: 2_000_000,
  });

  it("draws only the lanes it is asked for, and still the lines and the cursor", () => {
    const drawing = drawSignal(everything, view({ show: ["carrier"] }));

    expect(drawing.carrier).not.toBeNull();
    expect(drawing.pulses).toEqual([]);
    expect(drawing.symbols).toEqual([]);
    expect(drawing.seconds).toEqual([]);
    expect(drawing.ticks).toHaveLength(1);
    expect(drawing.cursor).not.toBeNull();
  });

  it("leaves the carrier out when its lane is not shown", () => {
    const drawing = drawSignal(everything, view({ show: ["pulse", "symbol"] }));

    expect(drawing.carrier).toBeNull();
    expect(drawing.pulses).toHaveLength(1);
  });

  it("shares the height between the lanes shown, the carrier taking double", () => {
    // Two lanes in 90 px: the carrier has 0 to 60 and the pulses 60 to 90.
    const drawing = drawSignal(
      everything,
      view({ height: 90, show: ["carrier", "pulse"] }),
    );

    const reducedY = drawing.carrier?.points[0].y ?? 0;
    expect(reducedY).toBeGreaterThan(40);
    expect(reducedY).toBeLessThanOrEqual(60);
    expect(drawing.pulses[0].bar.y).toBeGreaterThanOrEqual(60);
    expect(drawing.pulses[0].label?.y).toBeLessThan(90);
  });

  it("sizes text from its lane, up to a limit", () => {
    const small = drawSignal(everything, view()).seconds[0].size;
    const large = drawSignal(everything, view({ height: 1000 })).seconds[0]
      .size;

    expect(small).toBeLessThan(20);
    expect(large).toBeGreaterThan(small);
    // 1.25 rem at a 16 px root.
    expect(large).toBe(20);
  });
});
