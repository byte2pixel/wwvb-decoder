import { describe, expect, it } from "vitest";
import { timeWindowFor } from "./timeWindow";

describe("timeWindowFor", () => {
  it("shows 12 s with labels at 1x", () => {
    expect(timeWindowFor(1)).toEqual({
      windowMicros: 12_000_000,
      labels: true,
    });
  });

  it("shows 60 s without labels above 1x", () => {
    const fast = { windowMicros: 60_000_000, labels: false };

    expect(timeWindowFor(10)).toEqual(fast);
    expect(timeWindowFor(1.5)).toEqual(fast);
  });

  it("keeps the 12 s window below 1x, and at 0 where the source is unpaced", () => {
    const slow = { windowMicros: 12_000_000, labels: true };

    expect(timeWindowFor(0.5)).toEqual(slow);
    expect(timeWindowFor(0)).toEqual(slow);
  });
});
