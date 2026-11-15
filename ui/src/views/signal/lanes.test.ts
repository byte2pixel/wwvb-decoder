import { describe, expect, it } from "vitest";
import type { Edge, PinLevel } from "@/protocol/messages";
import { signalLanes, type SignalInput } from "./lanes";

const edge = (micros: number, level: PinLevel): Edge => ({ micros, level });

/** A marker second, then a zero, as the synthetic source sends them: high is reduced carrier. */
const markerThenZero = [
  edge(1_000_000, "high"),
  edge(1_800_000, "low"),
  edge(2_000_000, "high"),
  edge(2_200_000, "low"),
];

const input = (over: Partial<SignalInput> = {}): SignalInput => ({
  edges: [],
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
