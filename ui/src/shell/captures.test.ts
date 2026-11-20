import { describe, expect, it } from "vitest";
import { captureLabel, formatLength, formatRecorded } from "./captures";

describe("formatRecorded", () => {
  it("shows the UTC date and time to the minute", () => {
    expect(formatRecorded("2026-11-09T04:34:28Z")).toBe("2026-11-09 04:34 UTC");
  });

  it("converts a time with an offset to UTC", () => {
    expect(formatRecorded("2026-11-08T22:34:28-06:00")).toBe(
      "2026-11-09 04:34 UTC",
    );
  });

  it("returns what it was given when that is not a time", () => {
    expect(formatRecorded("yesterday")).toBe("yesterday");
  });
});

describe("formatLength", () => {
  it.each([
    [0.4, "0 min"],
    [3.16, "3 min"],
    [59.4, "59 min"],
    [59.6, "1 h 00 min"],
    [487.5, "8 h 08 min"],
    [524.9, "8 h 45 min"],
  ])("shows %s minutes as %s", (minutes, text) => {
    expect(formatLength(minutes)).toBe(text);
  });
});

describe("captureLabel", () => {
  it("joins the time recorded and the length", () => {
    expect(
      captureLabel({
        path: "captures/2026-11-09T0434Z-overnight.edges.txt",
        recorded: "2026-11-09T04:34:28Z",
        minutes: 524.9,
      }),
    ).toBe("2026-11-09 04:34 UTC · 8 h 45 min");
  });
});
