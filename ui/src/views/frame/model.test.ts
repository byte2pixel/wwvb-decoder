import { describe, expect, it } from "vitest";
import {
  frameLayoutSchema,
  type FrameSyncEvent,
  type SymbolKind,
  type SecondRole,
} from "@/protocol/messages";
import type { FilledSlot } from "@/state/appState";
import fixture from "@/sim/fixtures/frameLayout.json";
import {
  cellState,
  fieldBrackets,
  frameCells,
  frameHeader,
  placedSymbols,
  receivingSecond,
  ROW_SECONDS,
  type CellState,
} from "./model";

/** The host's layout, from the copy of its message example. */
const layout = frameLayoutSchema.parse(fixture.payload);

describe("frameCells", () => {
  it("has 60 cells with no role and no label before the layout arrives", () => {
    const cells = frameCells(null);

    expect(cells).toHaveLength(60);
    expect(cells.map((cell) => cell.second)).toEqual(
      Array.from({ length: 60 }, (_, second) => second),
    );
    expect(cells.every((cell) => cell.role === null && cell.label === "")).toBe(
      true,
    );
  });

  it("gives each cell the role the layout names for its second", () => {
    const cells = frameCells(layout);

    expect(cells.map((cell) => cell.role)).toEqual(layout.roles);
  });

  it("labels a bit with its weight", () => {
    const cells = frameCells(layout);

    // The minute's tens and ones, and the day of the year's hundreds.
    expect(cells.slice(1, 9).map((cell) => cell.label)).toEqual([
      "40",
      "20",
      "10",
      "",
      "8",
      "4",
      "2",
      "1",
    ]);
    expect(cells[22].label).toBe("200");
  });

  it("names the position markers in the order they come", () => {
    const cells = frameCells(layout);

    expect(
      cells
        .filter((cell) => cell.role === "marker")
        .map((cell) => [cell.second, cell.label]),
    ).toEqual([
      [0, "Pr"],
      [9, "P1"],
      [19, "P2"],
      [29, "P3"],
      [39, "P4"],
      [49, "P5"],
      [59, "P0"],
    ]);
  });

  it("leaves a second that has no weight without a label", () => {
    const cells = frameCells(layout);

    // An unused second, a UT1 sign bit, and the leap year bit.
    expect([cells[4], cells[36], cells[55]].map((cell) => cell.label)).toEqual([
      "",
      "",
      "",
    ]);
  });
});

describe("fieldBrackets", () => {
  it("has no brackets before the layout arrives", () => {
    expect(fieldBrackets(null)).toEqual([]);
  });

  it("runs a bracket from the first to the last second of its field", () => {
    const brackets = fieldBrackets(layout);

    // The day of the year runs across a marker, and the minute across an unused second.
    expect(
      brackets.map(({ name, firstSecond, lastSecond }) => [
        name,
        firstSecond,
        lastSecond,
      ]),
    ).toEqual([
      ["Minute", 1, 8],
      ["Hour", 12, 18],
      ["Day of year", 22, 33],
      ["UT1 sign", 36, 38],
      ["UT1", 40, 43],
      ["Year", 45, 53],
      ["LY", 55, 55],
      ["LS", 56, 56],
      ["DST", 57, 58],
    ]);
  });

  it("finds every field of the host's layout inside one row", () => {
    const rows = fieldBrackets(layout).map(({ firstSecond, lastSecond }) => [
      Math.floor(firstSecond / ROW_SECONDS),
      Math.floor(lastSecond / ROW_SECONDS),
    ]);

    // The row each field starts in and the row it ends in: two fields, two, then five.
    expect(rows).toEqual([
      [0, 0],
      [0, 0],
      [1, 1],
      [1, 1],
      [2, 2],
      [2, 2],
      [2, 2],
      [2, 2],
      [2, 2],
    ]);
  });
});

/** A slot of the frame that started at the given second of source time. */
const slot = (
  frameStartSeconds: number,
  second: number,
  symbol: SymbolKind,
): FilledSlot => ({
  startMicros: (frameStartSeconds + second) * 1_000_000,
  frameStartMicros: frameStartSeconds * 1_000_000,
  second,
  symbol,
});

describe("placedSymbols", () => {
  it("has 60 empty seconds before a symbol is placed", () => {
    const symbols = placedSymbols([]);

    expect(symbols).toHaveLength(60);
    expect(symbols.every((symbol) => symbol === null)).toBe(true);
  });

  it("puts each symbol in its second and leaves the rest empty", () => {
    const symbols = placedSymbols([
      slot(60, 0, "marker"),
      slot(60, 1, "zero"),
      slot(60, 2, "one"),
      slot(60, 3, "error"),
    ]);

    expect(symbols.slice(0, 5)).toEqual([
      "marker",
      "zero",
      "one",
      "error",
      null,
    ]);
    expect(symbols.slice(5).every((symbol) => symbol === null)).toBe(true);
  });

  it("shows only the frame in progress once a new frame starts", () => {
    const symbols = placedSymbols([
      slot(60, 58, "one"),
      slot(60, 59, "marker"),
      slot(120, 0, "marker"),
      slot(120, 1, "zero"),
    ]);

    expect(symbols.slice(0, 2)).toEqual(["marker", "zero"]);
    expect(symbols.slice(2).every((symbol) => symbol === null)).toBe(true);
  });
});

describe("receivingSecond", () => {
  const placed = (second: number): FrameSyncEvent => ({
    kind: "slotFilled",
    frameStartMicros: 60_000_000,
    second,
    symbol: "zero",
  });

  it("is the second after the one just placed", () => {
    expect(receivingSecond(placed(0))).toBe(1);
    expect(receivingSecond(placed(36))).toBe(37);
  });

  it("is nothing after the last second, until the next frame's first symbol", () => {
    expect(receivingSecond(placed(59))).toBeNull();
    expect(
      receivingSecond({
        kind: "frameComplete",
        frame: {
          startMicros: 60_000_000,
          endMicros: 120_000_000,
          symbols: Array.from({ length: 60 }, () => "zero"),
        },
      }),
    ).toBeNull();
  });

  it.each<[string, FrameSyncEvent | null]>([
    ["before any event", null],
    ["while searching", { kind: "searching", consecutiveMarkers: 1 }],
    ["after a loss of sync", { kind: "syncLost", reason: "marker missing" }],
  ])("is nothing %s", (_, event) => {
    expect(receivingSecond(event)).toBeNull();
  });
});

describe("frameHeader", () => {
  /** What the header reads from the store, with nothing running unless a test says so. */
  const state = (
    given: Partial<Parameters<typeof frameHeader>[0]> = {},
  ): Parameters<typeof frameHeader>[0] => ({
    frameSync: null,
    syncLoss: null,
    sourceStatus: null,
    ...given,
  });

  const running = {
    source: "synthetic",
    name: "synthetic",
    state: "running",
    detail: "",
  } as const;

  it("says nothing before a source runs", () => {
    expect(frameHeader(state())).toEqual({ text: "", progress: null });
  });

  it("says Searching once a source runs, before frame sync reports", () => {
    expect(frameHeader(state({ sourceStatus: running }))).toEqual({
      text: "Searching",
      progress: null,
    });
  });

  it("counts the markers while searching", () => {
    const header = frameHeader(
      state({
        sourceStatus: running,
        frameSync: { kind: "searching", consecutiveMarkers: 1 },
      }),
    );

    expect(header).toEqual({
      text: "Searching, 1 of 2 markers",
      progress: null,
    });
  });

  it("counts the symbols placed, with their share of the frame", () => {
    const header = frameHeader(
      state({
        sourceStatus: running,
        frameSync: {
          kind: "slotFilled",
          frameStartMicros: 60_000_000,
          second: 36,
          symbol: "zero",
        },
      }),
    );

    expect(header.text).toBe("Synced, 37 of 60 symbols");
    expect(header.progress).toBeCloseTo(37 / 60);
  });

  it("is full when the frame is complete", () => {
    const header = frameHeader(
      state({
        sourceStatus: running,
        frameSync: {
          kind: "frameComplete",
          frame: {
            startMicros: 60_000_000,
            endMicros: 120_000_000,
            symbols: Array.from({ length: 60 }, () => "zero"),
          },
        },
      }),
    );

    expect(header).toEqual({ text: "Synced, 60 of 60 symbols", progress: 1 });
  });

  it("shows the host's reason for a loss for as long as the store holds it", () => {
    // The host sends searching in the same push, so that is the latest event.
    const header = frameHeader(
      state({
        sourceStatus: running,
        frameSync: { kind: "searching", consecutiveMarkers: 0 },
        syncLoss: { reason: "expected marker at second 9, got Zero", seq: 12 },
      }),
    );

    expect(header).toEqual({
      text: "Sync lost: expected marker at second 9, got Zero",
      progress: null,
    });
  });
});

describe("cellState", () => {
  it.each<[SecondRole | null, SymbolKind | null, CellState]>([
    ["minute", null, "empty"],
    [null, null, "empty"],
    ["marker", null, "emptyMarker"],
    ["unused", null, "emptyUnused"],
    ["minute", "zero", "zero"],
    ["unused", "zero", "unusedZero"],
    ["minute", "one", "one"],
    ["unused", "one", "one"],
    ["marker", "marker", "marker"],
    ["minute", "error", "error"],
  ])("draws a %s second holding %s as %s", (role, symbol, state) => {
    expect(cellState(role, symbol)).toBe(state);
  });
});
