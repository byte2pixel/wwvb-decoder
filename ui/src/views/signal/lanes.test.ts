import { describe, expect, it } from "vitest";
import type {
  Edge,
  PinLevel,
  Pulse,
  Symbol as WwvbSymbol,
  SymbolKind,
} from "@/protocol/messages";
import type { FilledSlot } from "@/state/appState";
import { signalLanes, type SignalInput } from "./lanes";

const edge = (micros: number, level: PinLevel): Edge => ({ micros, level });

const pulse = (startMicros: number, durationMicros: number): Pulse => ({
  startMicros,
  durationMicros,
  endMicros: startMicros + durationMicros,
});

const symbol = (
  startMicros: number,
  durationMicros: number,
  kind: SymbolKind,
): WwvbSymbol => ({ startMicros, durationMicros, kind });

/** A filled slot for the symbol that starts at startMicros. The lane reads only the start and the second. */
const slot = (startMicros: number, second: number): FilledSlot => ({
  startMicros,
  frameStartMicros: 2_000_000,
  second,
  symbol: "zero",
});

/** A marker second, then a zero, as the synthetic source sends them: high is reduced carrier. */
const markerThenZero = [
  edge(1_000_000, "high"),
  edge(1_800_000, "low"),
  edge(2_000_000, "high"),
  edge(2_200_000, "low"),
];

const input = (over: Partial<SignalInput> = {}): SignalInput => ({
  edges: [],
  pulses: [],
  symbols: [],
  slots: [],
  nowMicros: null,
  windowMicros: 12_000_000,
  reducedLevel: "high",
  ...over,
});

describe("carrier lane", () => {
  it("is a square wave from the edges up to the present", () => {
    const lanes = signalLanes(
      input({ edges: markerThenZero, nowMicros: 2_500_000 }),
    );

    expect(lanes.carrier).toEqual([
      { startMicros: 1_000_000, endMicros: 1_800_000, reduced: true },
      { startMicros: 1_800_000, endMicros: 2_000_000, reduced: false },
      { startMicros: 2_000_000, endMicros: 2_200_000, reduced: true },
      { startMicros: 2_200_000, endMicros: 2_500_000, reduced: false },
    ]);
  });

  it("starts at the window's left edge", () => {
    // 12 s back from 13.9 s is 1.9 s: inside the gap between the two pulses.
    const lanes = signalLanes(
      input({ edges: markerThenZero, nowMicros: 13_900_000 }),
    );

    expect(lanes.carrier).toEqual([
      { startMicros: 1_900_000, endMicros: 2_000_000, reduced: false },
      { startMicros: 2_000_000, endMicros: 2_200_000, reduced: true },
      { startMicros: 2_200_000, endMicros: 13_900_000, reduced: false },
    ]);
  });

  it("ends at the newest edge when there is no present", () => {
    const lanes = signalLanes(input({ edges: markerThenZero }));

    expect(lanes.window).toEqual({
      startMicros: -9_800_000,
      endMicros: 2_200_000,
    });
    expect(lanes.carrier).toEqual([
      { startMicros: 1_000_000, endMicros: 1_800_000, reduced: true },
      { startMicros: 1_800_000, endMicros: 2_000_000, reduced: false },
      { startMicros: 2_000_000, endMicros: 2_200_000, reduced: true },
    ]);
  });

  it("is empty with no present and no edges", () => {
    const lanes = signalLanes(input());

    expect(lanes.window).toBeNull();
    expect(lanes.carrier).toEqual([]);
  });

  it("does not draw past the present", () => {
    // The clock can trail an edge that has already arrived, here by 100 ms.
    const lanes = signalLanes(
      input({ edges: markerThenZero, nowMicros: 2_100_000 }),
    );

    expect(lanes.carrier).toEqual([
      { startMicros: 1_000_000, endMicros: 1_800_000, reduced: true },
      { startMicros: 1_800_000, endMicros: 2_000_000, reduced: false },
      { startMicros: 2_000_000, endMicros: 2_100_000, reduced: true },
    ]);
  });

  it("takes the reduced-carrier level from the setting", () => {
    const lanes = signalLanes(
      input({
        edges: markerThenZero,
        nowMicros: 2_500_000,
        reducedLevel: "low",
      }),
    );

    expect(lanes.carrier.map((segment) => segment.reduced)).toEqual([
      false,
      true,
      false,
      true,
    ]);
  });
});

describe("pulse lane", () => {
  it("has a bar for each measured pulse, with its duration in milliseconds", () => {
    const lanes = signalLanes(
      input({
        pulses: [pulse(1_000_000, 800_000), pulse(2_000_000, 200_612)],
        nowMicros: 3_000_000,
      }),
    );

    expect(lanes.pulses).toEqual([
      { startMicros: 1_000_000, endMicros: 1_800_000, durationMs: 800 },
      { startMicros: 2_000_000, endMicros: 2_200_612, durationMs: 201 },
    ]);
  });

  it("keeps a bar's own start and its duration when the window cuts it", () => {
    // 12 s back from 13.5 s is 1.5 s: inside the marker pulse.
    const lanes = signalLanes(
      input({
        pulses: [
          pulse(0, 500_000),
          pulse(1_000_000, 800_000),
          pulse(2_000_000, 200_612),
        ],
        nowMicros: 13_500_000,
      }),
    );

    expect(lanes.pulses).toEqual([
      { startMicros: 1_000_000, endMicros: 1_800_000, durationMs: 800 },
      { startMicros: 2_000_000, endMicros: 2_200_612, durationMs: 201 },
    ]);
  });

  it("shows a reduced interval as pending until its pulse is measured", () => {
    const lanes = signalLanes(
      input({
        edges: markerThenZero,
        pulses: [pulse(1_000_000, 800_000)],
        nowMicros: 2_500_000,
      }),
    );

    expect(lanes.pulses).toEqual([
      { startMicros: 1_000_000, endMicros: 1_800_000, durationMs: 800 },
      { startMicros: 2_000_000, endMicros: 2_200_000, durationMs: null },
    ]);
  });

  it("grows a pending bar to the present while the carrier is still reduced", () => {
    const lanes = signalLanes(
      input({
        edges: markerThenZero.slice(0, 3),
        pulses: [pulse(1_000_000, 800_000)],
        nowMicros: 2_100_000,
      }),
    );

    expect(lanes.pulses.at(-1)).toEqual({
      startMicros: 2_000_000,
      endMicros: 2_100_000,
      durationMs: null,
    });
  });

  it("shows every reduced interval after the last measured pulse as pending", () => {
    // The edges batch for the next second can arrive before this second's pulse message.
    const lanes = signalLanes(
      input({
        edges: [...markerThenZero, edge(3_000_000, "high")],
        pulses: [pulse(1_000_000, 800_000)],
        nowMicros: 3_100_000,
      }),
    );

    expect(lanes.pulses).toEqual([
      { startMicros: 1_000_000, endMicros: 1_800_000, durationMs: 800 },
      { startMicros: 2_000_000, endMicros: 2_200_000, durationMs: null },
      { startMicros: 3_000_000, endMicros: 3_100_000, durationMs: null },
    ]);
  });

  it("shows the first second as pending before any pulse is measured", () => {
    const lanes = signalLanes(
      input({ edges: [edge(0, "high")], nowMicros: 300_000 }),
    );

    expect(lanes.pulses).toEqual([
      { startMicros: 0, endMicros: 300_000, durationMs: null },
    ]);
  });

  it("leaves a glitch between two measured pulses to the carrier lane", () => {
    const lanes = signalLanes(
      input({
        edges: [
          edge(1_000_000, "high"),
          edge(1_800_000, "low"),
          edge(1_900_000, "high"), // a 5 ms dip, too short to be a pulse
          edge(1_905_000, "low"),
          edge(2_000_000, "high"),
          edge(2_200_000, "low"),
        ],
        pulses: [pulse(1_000_000, 800_000), pulse(2_000_000, 200_000)],
        nowMicros: 2_500_000,
      }),
    );

    expect(lanes.carrier).toContainEqual({
      startMicros: 1_900_000,
      endMicros: 1_905_000,
      reduced: true,
    });
    expect(lanes.pulses).toEqual([
      { startMicros: 1_000_000, endMicros: 1_800_000, durationMs: 800 },
      { startMicros: 2_000_000, endMicros: 2_200_000, durationMs: 200 },
    ]);
  });
});

describe("symbol lane", () => {
  it("has a chip at the start of each classified pulse", () => {
    const lanes = signalLanes(
      input({
        pulses: [pulse(1_000_000, 800_000), pulse(2_000_000, 200_000)],
        symbols: [
          symbol(1_000_000, 800_000, "marker"),
          symbol(2_000_000, 200_000, "zero"),
        ],
        nowMicros: 3_000_000,
      }),
    );

    expect(lanes.symbols).toEqual([
      { startMicros: 1_000_000, kind: "marker" },
      { startMicros: 2_000_000, kind: "zero" },
    ]);
  });

  it("reads pending where the pulse lane does", () => {
    const lanes = signalLanes(
      input({
        edges: markerThenZero,
        pulses: [pulse(1_000_000, 800_000)],
        symbols: [symbol(1_000_000, 800_000, "marker")],
        nowMicros: 2_500_000,
      }),
    );

    expect(lanes.symbols).toEqual([
      { startMicros: 1_000_000, kind: "marker" },
      { startMicros: 2_000_000, kind: "pending" },
    ]);
  });

  it("reads pending for a measured pulse until its symbol arrives", () => {
    const lanes = signalLanes(
      input({
        pulses: [pulse(1_000_000, 800_000), pulse(2_000_000, 200_000)],
        symbols: [symbol(1_000_000, 800_000, "marker")],
        nowMicros: 3_000_000,
      }),
    );

    expect(lanes.symbols).toEqual([
      { startMicros: 1_000_000, kind: "marker" },
      { startMicros: 2_000_000, kind: "pending" },
    ]);
  });

  it("keeps a chip's own start when the window cuts its bar", () => {
    // 12 s back from 13.5 s is 1.5 s: inside the marker pulse, past the first one.
    const lanes = signalLanes(
      input({
        pulses: [pulse(0, 500_000), pulse(1_000_000, 800_000)],
        symbols: [
          symbol(0, 500_000, "one"),
          symbol(1_000_000, 800_000, "marker"),
        ],
        nowMicros: 13_500_000,
      }),
    );

    expect(lanes.symbols).toEqual([{ startMicros: 1_000_000, kind: "marker" }]);
  });

  it("keeps a pending bar's own start, and its chip's, when the window cuts it", () => {
    // A carrier stuck at the reduced level: one edge, then nothing for 12.5 s.
    const lanes = signalLanes(
      input({ edges: [edge(500_000, "high")], nowMicros: 13_000_000 }),
    );

    expect(lanes.pulses).toEqual([
      { startMicros: 500_000, endMicros: 13_000_000, durationMs: null },
    ]);
    expect(lanes.symbols).toEqual([{ startMicros: 500_000, kind: "pending" }]);
  });
});

describe("second lane", () => {
  it("labels each symbol with the second it filled", () => {
    const lanes = signalLanes(
      input({
        pulses: [pulse(2_000_000, 800_000), pulse(3_000_000, 200_000)],
        symbols: [
          symbol(2_000_000, 800_000, "marker"),
          symbol(3_000_000, 200_000, "zero"),
        ],
        slots: [slot(2_000_000, 0), slot(3_000_000, 1)],
        nowMicros: 4_000_000,
      }),
    );

    expect(lanes.seconds).toEqual([
      { startMicros: 2_000_000, second: 0 },
      { startMicros: 3_000_000, second: 1 },
    ]);
  });

  it("has no second for a symbol that arrived while frame sync was searching", () => {
    // Two markers in a row start a frame: the first is seen while searching, the second fills second 0.
    const lanes = signalLanes(
      input({
        pulses: [
          pulse(1_000_000, 800_000),
          pulse(2_000_000, 800_000),
          pulse(3_000_000, 200_000),
        ],
        symbols: [
          symbol(1_000_000, 800_000, "marker"),
          symbol(2_000_000, 800_000, "marker"),
          symbol(3_000_000, 200_000, "zero"),
        ],
        slots: [slot(2_000_000, 0), slot(3_000_000, 1)],
        nowMicros: 4_000_000,
      }),
    );

    expect(lanes.seconds).toEqual([
      { startMicros: 1_000_000, second: null },
      { startMicros: 2_000_000, second: 0 },
      { startMicros: 3_000_000, second: 1 },
    ]);
  });

  it("says nothing for the newest symbol until its slot arrives", () => {
    // A symbol's slotFilled arrives a few milliseconds after it; a dash must not flash meanwhile.
    const lanes = signalLanes(
      input({
        pulses: [pulse(1_000_000, 800_000), pulse(2_000_000, 800_000)],
        symbols: [
          symbol(1_000_000, 800_000, "marker"),
          symbol(2_000_000, 800_000, "marker"),
        ],
        nowMicros: 3_000_000,
      }),
    );

    expect(lanes.seconds).toEqual([{ startMicros: 1_000_000, second: null }]);
  });

  it("has no entry for a pending chip", () => {
    const lanes = signalLanes(
      input({
        edges: markerThenZero,
        pulses: [pulse(1_000_000, 800_000)],
        symbols: [symbol(1_000_000, 800_000, "marker")],
        slots: [slot(1_000_000, 0)],
        nowMicros: 2_500_000,
      }),
    );

    expect(lanes.symbols).toHaveLength(2);
    expect(lanes.seconds).toEqual([{ startMicros: 1_000_000, second: 0 }]);
  });

  it("keeps a label's own start when the window cuts its bar", () => {
    // 12 s back from 13.5 s is 1.5 s: inside the marker pulse, past the first one.
    const lanes = signalLanes(
      input({
        pulses: [pulse(0, 500_000), pulse(1_000_000, 800_000)],
        symbols: [
          symbol(0, 500_000, "one"),
          symbol(1_000_000, 800_000, "marker"),
        ],
        slots: [slot(0, 58), slot(1_000_000, 59)],
        nowMicros: 13_500_000,
      }),
    );

    expect(lanes.seconds).toEqual([{ startMicros: 1_000_000, second: 59 }]);
  });
});

describe("second lines", () => {
  it("has a line at every second of the frame, whatever the pulses' own starts", () => {
    // The slot builder's frame starts at 2 s; the pulse is 300 µs late.
    const lanes = signalLanes(
      input({
        pulses: [pulse(4_000_300, 200_000)],
        symbols: [symbol(4_000_300, 200_000, "zero")],
        slots: [slot(4_000_300, 2)],
        nowMicros: 5_500_000,
        windowMicros: 3_000_000,
      }),
    );

    expect(lanes.ticks).toEqual([3_000_000, 4_000_000, 5_000_000]);
  });

  it("takes the seconds from the newest pulse while frame sync is searching", () => {
    // The window is 9.5 s to 12.5 s, so the lines run back from the pulse as well as on from it.
    const lanes = signalLanes(
      input({
        pulses: [pulse(10_000_250, 800_000), pulse(11_000_400, 200_000)],
        nowMicros: 12_500_000,
        windowMicros: 3_000_000,
      }),
    );

    expect(lanes.ticks).toEqual([10_000_400, 11_000_400, 12_000_400]);
  });

  it("has no lines before anything marks a second", () => {
    const lanes = signalLanes(
      input({ edges: [edge(0, "high")], nowMicros: 300_000 }),
    );

    expect(lanes.ticks).toEqual([]);
  });

  it("has a line on each edge of the window when a second falls there", () => {
    const lanes = signalLanes(
      input({
        pulses: [pulse(2_000_000, 200_000)],
        nowMicros: 5_000_000,
        windowMicros: 3_000_000,
      }),
    );

    expect(lanes.ticks).toEqual([2_000_000, 3_000_000, 4_000_000, 5_000_000]);
  });
});

describe("cursor", () => {
  it("is at the present", () => {
    const lanes = signalLanes(
      input({ edges: markerThenZero, nowMicros: 2_500_000 }),
    );

    expect(lanes.cursorMicros).toBe(2_500_000);
  });

  it("is absent from a still picture, which has no present", () => {
    expect(
      signalLanes(input({ edges: markerThenZero })).cursorMicros,
    ).toBeNull();
    expect(signalLanes(input()).cursorMicros).toBeNull();
  });
});

describe("glitches", () => {
  /** A marker, a 5 ms dip 100 ms after it, and a zero. */
  const withDip = [
    edge(1_000_000, "high"),
    edge(1_800_000, "low"),
    edge(1_900_000, "high"),
    edge(1_905_000, "low"),
    edge(2_000_000, "high"),
    edge(2_200_000, "low"),
  ];

  it("has none in a clean signal", () => {
    const lanes = signalLanes(
      input({
        edges: markerThenZero,
        pulses: [pulse(1_000_000, 800_000), pulse(2_000_000, 200_000)],
        nowMicros: 2_500_000,
      }),
    );

    expect(lanes.glitches).toEqual([]);
  });

  it("marks reduced carrier that no measured pulse covers as dropped", () => {
    const lanes = signalLanes(
      input({
        edges: withDip,
        pulses: [pulse(1_000_000, 800_000), pulse(2_000_000, 200_000)],
        nowMicros: 2_500_000,
      }),
    );

    expect(lanes.glitches).toEqual([
      { startMicros: 1_900_000, endMicros: 1_905_000, kind: "dropped" },
    ]);
  });

  it("leaves a dip pending, not dropped, until a newer pulse is measured", () => {
    const lanes = signalLanes(
      input({
        edges: withDip,
        // The zero at 2 s is not measured yet, so stage 2 has not decided about the dip.
        pulses: [pulse(1_000_000, 800_000)],
        nowMicros: 2_500_000,
      }),
    );

    expect(lanes.glitches).toEqual([]);
    expect(lanes.pulses).toContainEqual({
      startMicros: 1_900_000,
      endMicros: 1_905_000,
      durationMs: null,
    });
  });

  it("marks full carrier inside a measured pulse as bridged", () => {
    const lanes = signalLanes(
      input({
        edges: [
          edge(1_000_000, "high"),
          edge(1_300_000, "low"), // the carrier comes back for 10 ms
          edge(1_310_000, "high"),
          edge(1_500_000, "low"),
          edge(2_000_000, "high"),
          edge(2_200_000, "low"),
        ],
        // Stage 2 measured one pulse of 500 ms across the gap.
        pulses: [pulse(1_000_000, 500_000), pulse(2_000_000, 200_000)],
        nowMicros: 2_500_000,
      }),
    );

    expect(lanes.glitches).toEqual([
      { startMicros: 1_300_000, endMicros: 1_310_000, kind: "bridged" },
    ]);
    // The pulse is still one bar with its measured duration.
    expect(lanes.pulses[0]).toEqual({
      startMicros: 1_000_000,
      endMicros: 1_500_000,
      durationMs: 500,
    });
  });

  it("does not mark the full carrier between two pulses", () => {
    const lanes = signalLanes(
      input({
        edges: markerThenZero,
        pulses: [pulse(1_000_000, 800_000), pulse(2_000_000, 200_000)],
        nowMicros: 2_500_000,
      }),
    );

    expect(
      lanes.glitches.filter((glitch) => glitch.kind === "bridged"),
    ).toEqual([]);
  });

  it("marks nothing older than the oldest pulse the page holds", () => {
    const lanes = signalLanes(
      input({
        edges: withDip,
        // The marker's pulse has left the buffer. The dip may have been part of anything.
        pulses: [pulse(2_000_000, 200_000), pulse(3_000_000, 200_000)],
        nowMicros: 3_500_000,
      }),
    );

    expect(lanes.glitches).toEqual([]);
  });

  it("goes by the reduced-carrier level of the setting", () => {
    const flipped = (level: PinLevel): PinLevel =>
      level === "high" ? "low" : "high";
    const lanes = signalLanes(
      input({
        edges: withDip.map(({ micros, level }) => edge(micros, flipped(level))),
        pulses: [pulse(1_000_000, 800_000), pulse(2_000_000, 200_000)],
        nowMicros: 2_500_000,
        reducedLevel: "low",
      }),
    );

    expect(lanes.glitches).toEqual([
      { startMicros: 1_900_000, endMicros: 1_905_000, kind: "dropped" },
    ]);
  });

  it("keeps a glitch's own start when the window cuts it, and drops one outside the window", () => {
    const pulses = [pulse(1_000_000, 800_000), pulse(2_000_000, 200_000)];
    // 12 s back from 13.902 s is 1.902 s: inside the dip.
    const cut = signalLanes(
      input({ edges: withDip, pulses, nowMicros: 13_902_000 }),
    );
    // And from 13.95 s it is 1.95 s: past the dip.
    const gone = signalLanes(
      input({ edges: withDip, pulses, nowMicros: 13_950_000 }),
    );

    expect(cut.glitches).toEqual([
      { startMicros: 1_900_000, endMicros: 1_905_000, kind: "dropped" },
    ]);
    expect(gone.glitches).toEqual([]);
  });

  it("has none in a picture with no edges", () => {
    expect(signalLanes(input()).glitches).toEqual([]);
  });
});
