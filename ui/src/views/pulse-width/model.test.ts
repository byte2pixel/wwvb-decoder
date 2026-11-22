import { describe, expect, it } from "vitest";
import type { Symbol as WwvbSymbol, SymbolKind } from "@/protocol/messages";
import {
  CLASS_ZONES,
  landingZone,
  lastPulse,
  lastPulseText,
  pulseWidthHistogram,
} from "./model";

const symbol = (durationMicros: number, kind: SymbolKind): WwvbSymbol => ({
  startMicros: 0,
  durationMicros,
  kind,
});

describe("pulseWidthHistogram", () => {
  it("has 40 empty bins, 25 ms apart from 0 ms, before any pulse", () => {
    const { bins } = pulseWidthHistogram([]);

    expect(bins).toHaveLength(40);
    expect(bins.slice(0, 3).map((bin) => bin.fromMs)).toEqual([0, 25, 50]);
    expect(bins.at(-1)?.fromMs).toBe(975);
    expect(bins.every((bin) => bin.count === 0 && bin.kind === null)).toBe(
      true,
    );
  });

  it.each([
    [99_999, "error", 75],
    [100_000, "zero", 100],
    [349_999, "zero", 325],
    [350_000, "one", 350],
    [649_999, "one", 625],
    [650_000, "marker", 650],
    [900_000, "marker", 900],
    [900_001, "error", 900],
  ] as const)(
    "counts %d microseconds, classified %s, in the bin from %d ms",
    (durationMicros, kind, fromMs) => {
      const { bins } = pulseWidthHistogram([symbol(durationMicros, kind)]);

      expect(bins.filter((bin) => bin.count > 0)).toMatchObject([
        { fromMs, count: 1, kind },
      ]);
    },
  );

  it.each([1_000_000, 3_400_000])(
    "counts %d microseconds, past the range, in the last bin",
    (durationMicros) => {
      const { bins } = pulseWidthHistogram([symbol(durationMicros, "error")]);

      expect(bins.filter((bin) => bin.count > 0)).toMatchObject([
        { fromMs: 975, count: 1, kind: "error" },
      ]);
    },
  );

  it("gives each bin its count against the fullest bin as its height", () => {
    const { bins } = pulseWidthHistogram([
      symbol(200_000, "zero"),
      symbol(205_000, "zero"),
      symbol(210_000, "zero"),
      symbol(220_000, "zero"),
      symbol(500_000, "one"),
      symbol(800_000, "marker"),
      symbol(810_000, "marker"),
    ]);

    expect(bins.filter((bin) => bin.count > 0)).toMatchObject([
      { fromMs: 200, count: 4, height: 1 },
      { fromMs: 500, count: 1, height: 0.25 },
      { fromMs: 800, count: 2, height: 0.5 },
    ]);
    expect(bins.at(0)?.height).toBe(0);
  });

  it("has no height in any bin before any pulse", () => {
    const { bins } = pulseWidthHistogram([]);

    expect(new Set(bins.map((bin) => bin.height))).toEqual(new Set([0]));
  });

  it("gives a bin the class of the newest pulse in it", () => {
    const { bins } = pulseWidthHistogram([
      symbol(900_000, "marker"),
      symbol(910_000, "error"),
    ]);

    expect(bins.filter((bin) => bin.count > 0)).toMatchObject([
      { fromMs: 900, count: 2, kind: "error" },
    ]);
  });

  it("reports the newest pulse to the millisecond", () => {
    const { last } = pulseWidthHistogram([
      symbol(200_000, "zero"),
      symbol(501_600, "one"),
    ]);

    expect(last).toMatchObject({ durationMs: 502, kind: "one" });
  });

  it("has no last pulse before any pulse", () => {
    expect(pulseWidthHistogram([]).last).toBeNull();
  });
});

describe("lastPulseText", () => {
  it.each([
    [201, "zero", "Last pulse 201 ms classified as 0"],
    [502, "one", "Last pulse 502 ms classified as 1"],
    [798, "marker", "Last pulse 798 ms classified as M"],
    [1400, "error", "Last pulse 1400 ms classified as error"],
  ] as const)("reads %d ms, a %s, as '%s'", (durationMs, kind, text) => {
    expect(lastPulseText({ durationMs, kind })).toBe(text);
  });

  it("says there are no pulses yet before the first", () => {
    expect(lastPulseText(null)).toBe("No pulses yet");
  });
});

describe("lastPulse", () => {
  it("is nothing before the first pulse", () => {
    expect(lastPulse([])).toBeNull();
  });

  it("is the newest symbol, with its start and its duration both ways", () => {
    const newest = { startMicros: 7_000_000, durationMicros: 501_600 };

    expect(
      lastPulse([symbol(200_000, "zero"), { ...newest, kind: "one" }]),
    ).toEqual({ ...newest, durationMs: 502, kind: "one" });
  });
});

describe("CLASS_ZONES", () => {
  it("covers the axis from end to end, a zone beginning where the one before ends", () => {
    expect(CLASS_ZONES.map(({ fromMs, toMs }) => [fromMs, toMs])).toEqual([
      [0, 100],
      [100, 350],
      [350, 650],
      [650, 900],
      [900, 1000],
    ]);
  });

  it("names each zone's class and where it begins", () => {
    expect(CLASS_ZONES.map(({ kind, rule }) => [kind, rule])).toEqual([
      ["error", "under 100 ms"],
      ["zero", "from 100 ms"],
      ["one", "from 350 ms"],
      ["marker", "from 650 ms"],
      ["error", "over 900 ms"],
    ]);
  });
});

describe("landingZone", () => {
  const landed = (durationMicros: number, kind: SymbolKind): number =>
    landingZone(lastPulse([symbol(durationMicros, kind)])!);

  it.each<[number, SymbolKind, number]>([
    [200_000, "zero", 1],
    [500_000, "one", 2],
    [800_000, "marker", 3],
  ])("puts a pulse of %d µs that is a %s in zone %d", (micros, kind, zone) => {
    expect(landed(micros, kind)).toBe(zone);
  });

  it("goes by the host's class, not by the duration", () => {
    // Exactly 900 ms is still a marker to the host, though the last zone begins there.
    expect(landed(900_000, "marker")).toBe(3);
    // A class the duration does not suggest is still the host's to give.
    expect(landed(200_000, "one")).toBe(2);
  });

  it("puts an error under the valid range in the first zone and one over it in the last", () => {
    expect(landed(93_000, "error")).toBe(0);
    // 99.6 ms reads 100 ms when rounded, and is still under the limit.
    expect(landed(99_600, "error")).toBe(0);
    expect(landed(1_200_000, "error")).toBe(4);
  });
});
