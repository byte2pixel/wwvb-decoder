import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import {
  frameLayoutSchema,
  hostMessageSchema,
  type DecodedFrame,
  type FrameSyncEvent,
  type SymbolKind,
  type SecondRole,
} from "@/protocol/messages";
import type { FilledSlot } from "@/state/appState";
import fixture from "@/sim/fixtures/frameLayout.json";
import {
  cellState,
  decodedFrameLine,
  decodedRecord,
  fieldBrackets,
  fieldValues,
  frameCells,
  frameHeader,
  frameSymbols,
  placedSymbols,
  receivingSecond,
  ROW_SECONDS,
  setWeights,
  SYNC_MOVES,
  syncDiagram,
  type CellState,
} from "./model";

/** The host's layout, from the copy of its message example. */
const layout = frameLayoutSchema.parse(fixture.payload);

/** The host's decodedFrame example: 19:30 on day 344 of 2026. */
const decoded = ((): DecodedFrame => {
  const file = new URL(
    "../../../../docs/design/message-examples/decodedFrame.json",
    import.meta.url,
  );
  const message = hostMessageSchema.parse(
    JSON.parse(readFileSync(file, "utf8")),
  );
  if (message.type !== "decodedFrame") throw new Error("not a decoded frame");
  return message.payload;
})();

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

describe("frameSymbols", () => {
  it("has 60 empty seconds before the first complete frame", () => {
    const symbols = frameSymbols(null);

    expect(symbols).toHaveLength(60);
    expect(symbols.every((symbol) => symbol === null)).toBe(true);
  });

  it("is the frame's own symbols, indexed by second", () => {
    expect(frameSymbols(decoded)).toEqual(decoded.frame.symbols);
  });
});

describe("fieldValues", () => {
  it("has no values before the first complete frame", () => {
    expect(fieldValues(null)).toBeNull();
  });

  it("writes what the host decoded for each field of its example", () => {
    expect(fieldValues(decoded)).toEqual({
      minute: "30",
      hour: "19",
      dayOfYear: "344",
      ut1Sign: "+",
      ut1Correction: "0.0 s",
      year: "26",
      leapYear: "no",
      leapSecondPending: "no",
      dst: "standard",
    });
  });

  it("writes a sign, a correction, the two flags, and the DST status in words", () => {
    const values = fieldValues({
      ...decoded,
      ut1Sign: "minus",
      ut1Tenths: 7,
      leapYear: true,
      leapSecondPending: true,
      dst: "beginsToday",
    });

    expect(values).toMatchObject({
      ut1Sign: "-",
      ut1Correction: "0.7 s",
      leapYear: "yes",
      leapSecondPending: "yes",
      dst: "begins today",
    });
  });

  it("has a value for every field the layout brackets", () => {
    const values = fieldValues(decoded);

    expect(Object.keys(values ?? {}).toSorted()).toEqual(
      fieldBrackets(layout)
        .map((bracket) => bracket.role)
        .toSorted(),
    );
  });
});

/** The example with an error symbol in each of these seconds. */
const withErrors = (...seconds: number[]): DecodedFrame => ({
  ...decoded,
  frame: {
    ...decoded.frame,
    symbols: decoded.frame.symbols.map((symbol, second) =>
      seconds.includes(second) ? "error" : symbol,
    ),
  },
});

describe("setWeights", () => {
  it("lists nothing before the first complete frame", () => {
    expect(setWeights(null, layout)).toBeNull();
  });

  it("lists nothing before the layout arrives", () => {
    expect(setWeights(decoded, null)).toBeNull();
  });

  it("lists the weights of each field's bits that are 1, in the order they are sent", () => {
    // 19:30 on day 344 of year 26, with no UT1 correction.
    expect(setWeights(decoded, layout)).toEqual({
      minute: "20 + 10",
      hour: "10 + 8 + 1",
      dayOfYear: "200 + 100 + 40 + 4",
      ut1Correction: "none",
      year: "20 + 4 + 2",
    });
  });

  it("has no entry for a field whose seconds carry no weight", () => {
    const weights = setWeights(decoded, layout);

    expect(weights).not.toHaveProperty("ut1Sign");
    expect(weights).not.toHaveProperty("leapYear");
    expect(weights).not.toHaveProperty("leapSecondPending");
    expect(weights).not.toHaveProperty("dst");
  });

  it("leaves out the weight of a second that holds an error symbol, as the host reads it as 0", () => {
    // Second 2 carries the minute's 20.
    expect(setWeights(withErrors(2), layout)).toMatchObject({ minute: "10" });
  });

  it("lists a weight only where the layout's second holds a 1", () => {
    const allOnes: DecodedFrame = {
      ...decoded,
      frame: {
        ...decoded.frame,
        symbols: decoded.frame.symbols.map(() => "one"),
      },
    };

    // The unused second 4 and the marker in second 9 add nothing.
    expect(setWeights(allOnes, layout)).toMatchObject({
      minute: "40 + 20 + 10 + 8 + 4 + 2 + 1",
      ut1Correction: "8 + 4 + 2 + 1",
    });
  });
});

describe("decodedRecord", () => {
  it("has no rows before the first complete frame", () => {
    expect(decodedRecord(null)).toBeNull();
  });

  it("writes the host's record, a row for each property under its C# name", () => {
    expect(decodedRecord(decoded)).toEqual([
      { name: "Frame", value: "60 symbols" },
      { name: "Minute", value: "30" },
      { name: "Hour", value: "19" },
      { name: "DayOfYear", value: "344" },
      { name: "Year", value: "26" },
      { name: "Ut1Sign", value: "Plus" },
      { name: "Ut1Tenths", value: "0" },
      { name: "LeapYear", value: "false" },
      { name: "LeapSecondPending", value: "false" },
      { name: "Dst", value: "Standard" },
      { name: "ErrorSeconds", value: "none" },
    ]);
  });

  it("writes an enum's value as C# names it, and lists the seconds in error", () => {
    const rows = decodedRecord({
      ...decoded,
      ut1Sign: "invalid",
      ut1Tenths: 7,
      leapSecondPending: true,
      dst: "endsToday",
      errorSeconds: [6, 31],
    });

    expect(rows).toEqual(
      expect.arrayContaining([
        { name: "Ut1Sign", value: "Invalid" },
        { name: "Ut1Tenths", value: "7" },
        { name: "LeapSecondPending", value: "true" },
        { name: "Dst", value: "EndsToday" },
        { name: "ErrorSeconds", value: "6, 31" },
      ]),
    );
  });
});

describe("decodedFrameLine", () => {
  it.each<[string, DecodedFrame | null, string]>([
    ["before the first frame", null, "No frame yet"],
    ["for a frame with no error symbol", decoded, "No error symbols"],
    ["for one", withErrors(17), "Error symbol in second 17"],
    ["for several", withErrors(17, 42), "Error symbols in seconds 17, 42"],
  ])("reads %s", (_, frame, text) => {
    expect(decodedFrameLine(frame)).toBe(text);
  });

  it("names an error in an unused second, which the host's list leaves out", () => {
    // Second 44 is unused, so the host does not list it among the field seconds in error.
    const frame = withErrors(44);

    expect(frame.errorSeconds).toEqual([]);
    expect(decodedFrameLine(frame)).toBe("Error symbol in second 44");
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

describe("syncDiagram", () => {
  const running = {
    source: "synthetic",
    name: "",
    state: "running",
    detail: "",
  } as const;
  const quiet = { frameSync: null, syncLoss: null, sourceStatus: null };

  it("is in no state while no source has run", () => {
    expect(syncDiagram(quiet)).toEqual({
      state: null,
      count: "",
      lose: "sync lost",
      lost: false,
    });
  });

  it("is searching once a source runs, before frame sync reports", () => {
    expect(syncDiagram({ ...quiet, sourceStatus: running })).toMatchObject({
      state: "searching",
      count: "0 of 2 markers",
    });
  });

  it("counts the markers in a row while searching", () => {
    expect(
      syncDiagram({
        ...quiet,
        frameSync: { kind: "searching", consecutiveMarkers: 1 },
      }),
    ).toMatchObject({ state: "searching", count: "1 of 2 markers" });
  });

  it("counts the symbols placed while synced", () => {
    expect(
      syncDiagram({
        ...quiet,
        frameSync: {
          kind: "slotFilled",
          frameStartMicros: 1_000_000,
          second: 36,
          symbol: "one",
        },
      }),
    ).toMatchObject({ state: "synced", count: "37 of 60 symbols" });
  });

  it("is synced with every symbol placed when the frame is complete", () => {
    expect(
      syncDiagram({
        ...quiet,
        frameSync: { kind: "frameComplete", frame: decoded.frame },
      }),
    ).toMatchObject({ state: "synced", count: "60 of 60 symbols" });
  });

  it("is searching from no markers at the moment sync is lost", () => {
    expect(
      syncDiagram({
        ...quiet,
        frameSync: { kind: "syncLost", reason: "gap 2000 ms before second 15" },
      }),
    ).toMatchObject({ state: "searching", count: "0 of 2 markers" });
  });

  it("names the host's reason on the arrow for as long as the store holds the loss", () => {
    expect(
      syncDiagram({
        ...quiet,
        frameSync: { kind: "searching", consecutiveMarkers: 0 },
        syncLoss: { reason: "gap 2000 ms before second 15", seq: 7 },
      }),
    ).toMatchObject({
      state: "searching",
      lose: "sync lost: gap 2000 ms before second 15",
      lost: true,
    });
  });
});

describe("SYNC_MOVES", () => {
  it("says what starts a frame, with the host's two markers", () => {
    expect(SYNC_MOVES.sync).toBe("2 markers a second apart");
  });
});
