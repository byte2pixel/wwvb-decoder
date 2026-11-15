import type { SymbolKind } from "@/protocol/messages";
import type { SignalLanes } from "./lanes";

export type LaneName = "carrier" | "pulse" | "symbol" | "second";

/** The colors the drawing uses, read from the page's CSS variables. */
export interface SignalTokens {
  /** The lines at every second: `--border`. */
  line: string;
  /** The outline of a mark that is not final yet: `--input`. */
  lineStrong: string;
  /** The carrier, a measured pulse, a 1, and the cursor: `--signal`. */
  signal: string;
  /** Text on the signal color: `--on-signal`. */
  onSignal: string;
  /** A 0: `--zero` on `--zero-surface`. */
  zero: string;
  zeroSurface: string;
  /** A marker: `--marker` on `--marker-surface`. */
  marker: string;
  markerSurface: string;
  /** An error symbol: `--bad`. */
  bad: string;
  /** Values: `--foreground-soft`. */
  textSoft: string;
  /** What is missing or not final: `--muted-foreground`. */
  textMuted: string;
}

export interface SignalView {
  /** The canvas's size in CSS pixels. */
  width: number;
  height: number;
  /** The root font size in pixels. Gaps and text sizes are multiples of it, so the drawing scales with the page. */
  rem: number;
  /** The lanes to draw, top to bottom. A focus view shows fewer than four. */
  show: readonly LaneName[];
  /** False in the 60 s window, where the pulse and second labels would be unreadable. */
  labels: boolean;
  tokens: SignalTokens;
}

export interface Line {
  x1: number;
  y1: number;
  x2: number;
  y2: number;
  stroke: string;
  width: number;
}

export interface Point {
  x: number;
  y: number;
}

export interface Polyline {
  points: Point[];
  stroke: string;
  width: number;
}

export interface Rect {
  x: number;
  y: number;
  w: number;
  h: number;
  fill: string | null;
  /** Drawn 1 px wide, like every border. */
  stroke: string | null;
  radius: number;
}

/** Text in the monospaced face, centered vertically on y. */
export interface Text {
  x: number;
  y: number;
  text: string;
  fill: string;
  size: number;
  align: "left" | "center";
}

export interface PulseMark {
  bar: Rect;
  label: Text | null;
}

export interface ChipMark {
  box: Rect;
  text: Text;
}

/** Everything the canvas paints, in painting order. */
export interface SignalDrawing {
  ticks: Line[];
  carrier: Polyline | null;
  pulses: PulseMark[];
  symbols: ChipMark[];
  seconds: Text[];
  cursor: Line | null;
}

const SECOND_MICROS = 1_000_000;

/** The room kept free right of the cursor, in rem. */
const CURSOR_INSET = 0.75;
/** How the lanes share the height: the carrier gets twice the room of the others. */
const LANE_WEIGHTS: Record<LaneName, number> = {
  carrier: 2,
  pulse: 1,
  symbol: 1,
  second: 1,
};
/** The gap between the carrier's two levels and the edges of its lane, in rem. */
const CARRIER_PAD = 0.375;
/** 1.5 px at the baseline. */
const CARRIER_STROKE = 0.09375;
/** Text height as a share of its lane, capped in rem so an enlarged lane does not shout. */
const TEXT_SHARE = 0.42;
const MAX_TEXT = 1.25;
/** A pending bar narrower than this, in rem, gets no label: a glitch is a sliver and the word would outweigh it. */
const MIN_LABELLED_BAR = 0.25;
/** One character's width as a share of the text size, in the monospaced face. */
const MONO_ADVANCE = 0.6;
/** The most of a second a chip may cover, so neighbours never touch. */
const CHIP_MAX_SHARE = 0.8;
const CHIP_RADIUS = 0.3125;
const BAR_RADIUS = 0.125;

type ChipKind = SymbolKind | "pending";

const CHIP_STYLES: Record<
  ChipKind,
  {
    text: string;
    fill: keyof SignalTokens | null;
    stroke: keyof SignalTokens | null;
    ink: keyof SignalTokens;
  }
> = {
  zero: { text: "0", fill: "zeroSurface", stroke: null, ink: "zero" },
  one: { text: "1", fill: "signal", stroke: null, ink: "onSignal" },
  marker: { text: "M", fill: "markerSurface", stroke: null, ink: "marker" },
  error: { text: "×", fill: null, stroke: "bad", ink: "bad" },
  pending: {
    text: "pending",
    fill: null,
    stroke: "lineStrong",
    ink: "textMuted",
  },
};

interface LaneBox {
  top: number;
  height: number;
}

/** Stacks the shown lanes down the height by weight. A lane that is not shown has no box. */
function layoutLanes(
  show: readonly LaneName[],
  height: number,
): Partial<Record<LaneName, LaneBox>> {
  const total = show.reduce((sum, lane) => sum + LANE_WEIGHTS[lane], 0);
  const boxes: Partial<Record<LaneName, LaneBox>> = {};
  let top = 0;
  for (const lane of show) {
    const laneHeight = (height * LANE_WEIGHTS[lane]) / total;
    boxes[lane] = { top, height: laneHeight };
    top += laneHeight;
  }
  return boxes;
}

export function drawSignal(
  lanes: SignalLanes,
  view: SignalView,
): SignalDrawing {
  if (!lanes.window) {
    return {
      ticks: [],
      carrier: null,
      pulses: [],
      symbols: [],
      seconds: [],
      cursor: null,
    };
  }
  const { height, width, rem, show, labels, tokens } = view;
  const { startMicros, endMicros } = lanes.window;
  const plotWidth = width - CURSOR_INSET * rem;
  /** Where a moment in source time lands across the canvas, in pixels. */
  const x = (micros: number) =>
    ((micros - startMicros) * plotWidth) / (endMicros - startMicros);
  const secondWidth = x(startMicros + SECOND_MICROS);
  const boxes = layoutLanes(show, height);
  const textSize = (box: LaneBox) =>
    Math.min(TEXT_SHARE * box.height, MAX_TEXT * rem);

  const vertical = (micros: number, stroke: string): Line => {
    const xPos = x(micros);
    return { x1: xPos, y1: 0, x2: xPos, y2: height, stroke, width: 1 };
  };

  const drawCarrier = (box: LaneBox): Polyline | null => {
    const fullY = box.top + CARRIER_PAD * rem;
    const reducedY = box.top + box.height - CARRIER_PAD * rem;
    const points = lanes.carrier.flatMap((segment) => {
      const y = segment.reduced ? reducedY : fullY;
      return [
        { x: x(segment.startMicros), y },
        { x: x(segment.endMicros), y },
      ];
    });
    return points.length > 0
      ? { points, stroke: tokens.signal, width: CARRIER_STROKE * rem }
      : null;
  };

  // The bar sits in the upper part of the lane and its label under it.
  const drawPulses = (box: LaneBox): PulseMark[] =>
    lanes.pulses.map((pulse) => {
      const left = x(pulse.startMicros);
      const barWidth = x(pulse.endMicros) - left;
      const pending = pulse.durationMs === null;
      const labelled =
        labels && (!pending || barWidth >= MIN_LABELLED_BAR * rem);
      return {
        bar: {
          x: left,
          y: box.top + 0.12 * box.height,
          w: barWidth,
          h: 0.3 * box.height,
          fill: pending ? null : tokens.signal,
          stroke: pending ? tokens.lineStrong : null,
          radius: BAR_RADIUS * rem,
        },
        label: labelled
          ? {
              x: left,
              y: box.top + 0.74 * box.height,
              text: pending ? "pending" : `${pulse.durationMs} ms`,
              fill: pending ? tokens.textMuted : tokens.textSoft,
              size: textSize(box),
              align: "left",
            }
          : null,
      };
    });

  const drawSymbols = (box: LaneBox): ChipMark[] =>
    lanes.symbols.map((chip) => {
      const style = CHIP_STYLES[chip.kind];
      // With the labels off there is room for one character only.
      const text = chip.kind === "pending" && !labels ? "…" : style.text;
      const size = textSize(box);
      const boxHeight = 0.72 * box.height;
      // A square for one character, wider for a word, never as wide as a second.
      const natural = Math.max(
        boxHeight,
        text.length * MONO_ADVANCE * size + size,
      );
      const boxWidth = Math.min(natural, CHIP_MAX_SHARE * secondWidth);
      const left = x(chip.startMicros);
      const top = box.top + (box.height - boxHeight) / 2;
      return {
        box: {
          x: left,
          y: top,
          w: boxWidth,
          h: boxHeight,
          fill: style.fill ? tokens[style.fill] : null,
          stroke: style.stroke ? tokens[style.stroke] : null,
          radius: CHIP_RADIUS * rem,
        },
        text: {
          x: left + boxWidth / 2,
          y: top + boxHeight / 2,
          text,
          fill: tokens[style.ink],
          size,
          align: "center",
        },
      };
    });

  const drawSeconds = (box: LaneBox): Text[] =>
    lanes.seconds.map((label) => ({
      x: x(label.startMicros),
      y: box.top + box.height / 2,
      text:
        label.second === null
          ? "—"
          : `:${String(label.second).padStart(2, "0")}`,
      fill: label.second === null ? tokens.textMuted : tokens.textSoft,
      size: textSize(box),
      align: "left",
    }));

  return {
    ticks: lanes.ticks.map((tick) => vertical(tick, tokens.line)),
    carrier: boxes.carrier ? drawCarrier(boxes.carrier) : null,
    pulses: boxes.pulse ? drawPulses(boxes.pulse) : [],
    symbols: boxes.symbol ? drawSymbols(boxes.symbol) : [],
    seconds: boxes.second && labels ? drawSeconds(boxes.second) : [],
    cursor:
      lanes.cursorMicros === null
        ? null
        : vertical(lanes.cursorMicros, tokens.signal),
  };
}
