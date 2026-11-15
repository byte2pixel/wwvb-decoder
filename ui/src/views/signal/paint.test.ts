import { describe, expect, it } from "vitest";
import type { ChipMark, Line, Rect, SignalDrawing, Text } from "./model";
import { paintSignal } from "./paint";
import { recordingContext, type Painted } from "./recordingContext";

/** A drawing with nothing in it. */
const drawing = (over: Partial<SignalDrawing> = {}): SignalDrawing => ({
  ticks: [],
  carrier: null,
  pulses: [],
  symbols: [],
  seconds: [],
  cursor: null,
  ...over,
});

/** A line down an 80 px canvas. */
const vertical = (x: number, stroke: string): Line => ({
  x1: x,
  y1: 0,
  x2: x,
  y2: 80,
  stroke,
  width: 1,
});

/** A measured pulse's bar. */
const bar: Rect = {
  x: 100,
  y: 45,
  w: 50,
  h: 12,
  fill: "signal",
  stroke: null,
  radius: 2,
};

/** One short line per painted thing: a text's string, the op and where its path starts, or the op alone. */
const summary = (painted: Painted[]): string[] =>
  painted.map((p) => {
    if (p.op === "text") return p.text;
    return "path" in p ? `${p.op} ${p.path[0]}` : p.op;
  });

describe("paintSignal", () => {
  it("strokes each tick as a line of its own", () => {
    const { ctx, painted } = recordingContext();

    paintSignal(
      ctx,
      drawing({ ticks: [vertical(100, "line"), vertical(200, "line")] }),
      "Test Mono",
    );

    expect(painted).toEqual([
      {
        op: "stroke",
        path: ["M 100 0", "L 100 80"],
        strokeStyle: "line",
        lineWidth: 1,
      },
      {
        op: "stroke",
        path: ["M 200 0", "L 200 80"],
        strokeStyle: "line",
        lineWidth: 1,
      },
    ]);
  });

  it("strokes the carrier as one line through its points", () => {
    const { ctx, painted } = recordingContext();

    paintSignal(
      ctx,
      drawing({
        carrier: {
          points: [
            { x: 0, y: 6 },
            { x: 100, y: 6 },
            { x: 100, y: 34 },
            { x: 180, y: 34 },
          ],
          stroke: "signal",
          width: 1.5,
        },
      }),
      "Test Mono",
    );

    expect(painted).toEqual([
      {
        op: "stroke",
        path: ["M 0 6", "L 100 6", "L 100 34", "L 180 34"],
        strokeStyle: "signal",
        lineWidth: 1.5,
      },
    ]);
  });

  it("fills a measured bar and outlines a pending one, 1 px wide", () => {
    const { ctx, painted } = recordingContext();
    const pendingBar: Rect = {
      x: 200,
      y: 45,
      w: 30,
      h: 12,
      fill: null,
      stroke: "lineStrong",
      radius: 2,
    };

    paintSignal(
      ctx,
      drawing({
        // The carrier leaves a 1.5 px pen behind. An outline is 1 px all the same.
        carrier: {
          points: [
            { x: 0, y: 6 },
            { x: 300, y: 6 },
          ],
          stroke: "signal",
          width: 1.5,
        },
        pulses: [
          { bar, label: null },
          { bar: pendingBar, label: null },
        ],
      }),
      "Test Mono",
    );

    expect(painted).toEqual([
      {
        op: "stroke",
        path: ["M 0 6", "L 300 6"],
        strokeStyle: "signal",
        lineWidth: 1.5,
      },
      { op: "fill", path: ["R 100 45 50 12 2"], fillStyle: "signal" },
      {
        op: "stroke",
        path: ["R 200 45 30 12 2"],
        strokeStyle: "lineStrong",
        lineWidth: 1,
      },
    ]);
  });

  it("writes a pulse's label after its bar, in the monospaced face", () => {
    const { ctx, painted } = recordingContext();
    const label: Text = {
      x: 100,
      y: 70,
      text: "502 ms",
      fill: "textSoft",
      size: 13,
      align: "left",
    };

    paintSignal(ctx, drawing({ pulses: [{ bar, label }] }), "Test Mono");

    expect(painted).toEqual([
      { op: "fill", path: ["R 100 45 50 12 2"], fillStyle: "signal" },
      {
        op: "text",
        text: "502 ms",
        x: 100,
        y: 70,
        font: "13px Test Mono",
        fillStyle: "textSoft",
        textAlign: "left",
        textBaseline: "middle",
      },
    ]);
  });

  it("paints a chip's box and then its text", () => {
    const { ctx, painted } = recordingContext();
    const chip: ChipMark = {
      box: {
        x: 100,
        y: 62,
        w: 14,
        h: 14,
        fill: "zeroSurface",
        stroke: null,
        radius: 5,
      },
      text: {
        x: 107,
        y: 69,
        text: "0",
        fill: "zero",
        size: 8,
        align: "center",
      },
    };

    paintSignal(ctx, drawing({ symbols: [chip] }), "Test Mono");

    expect(painted).toEqual([
      { op: "fill", path: ["R 100 62 14 14 5"], fillStyle: "zeroSurface" },
      {
        op: "text",
        text: "0",
        x: 107,
        y: 69,
        font: "8px Test Mono",
        fillStyle: "zero",
        textAlign: "center",
        textBaseline: "middle",
      },
    ]);
  });

  it("paints the groups in the drawing's order, the cursor last", () => {
    const { ctx, painted } = recordingContext();
    const text = (label: string, y: number): Text => ({
      x: 100,
      y,
      text: label,
      fill: "textSoft",
      size: 8,
      align: "left",
    });

    paintSignal(
      ctx,
      {
        ticks: [vertical(100, "line")],
        carrier: {
          points: [
            { x: 0, y: 6 },
            { x: 300, y: 6 },
          ],
          stroke: "signal",
          width: 1.5,
        },
        pulses: [{ bar, label: text("502 ms", 55) }],
        symbols: [{ box: { ...bar, y: 62 }, text: text("1", 69) }],
        seconds: [text(":17", 90)],
        cursor: vertical(300, "signal"),
      },
      "Test Mono",
    );

    expect(summary(painted)).toEqual([
      "stroke M 100 0",
      "stroke M 0 6",
      "fill R 100 45 50 12 2",
      "502 ms",
      "fill R 100 62 50 12 2",
      "1",
      ":17",
      "stroke M 300 0",
    ]);
    expect(painted.at(-1)).toEqual({
      op: "stroke",
      path: ["M 300 0", "L 300 80"],
      strokeStyle: "signal",
      lineWidth: 1,
    });
  });
});
