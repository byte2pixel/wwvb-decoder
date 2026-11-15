import { describe, expect, it } from "vitest";
import type { Edge, PinLevel, Pulse } from "@/protocol/messages";
import { signalLanes, type SignalInput } from "./lanes";

const edge = (micros: number, level: PinLevel): Edge => ({ micros, level });

const pulse = (startMicros: number, durationMicros: number): Pulse => ({
  startMicros,
  durationMicros,
  endMicros: startMicros + durationMicros,
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

  it("cuts a bar at the window's left edge and keeps its duration", () => {
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
      { startMicros: 1_500_000, endMicros: 1_800_000, durationMs: 800 },
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
