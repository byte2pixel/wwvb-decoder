import { describe, expect, it } from "vitest";
import {
  captureLabel,
  captureName,
  formatLength,
  formatRecorded,
} from "./captures";

describe("formatRecorded", () => {
  it("shows the UTC date with the month as a word, and the time to the minute", () => {
    expect(formatRecorded("2026-11-09T04:34:28Z")).toBe(
      "9 Nov 2026, 04:34 UTC",
    );
  });

  it("converts a time with an offset to UTC", () => {
    expect(formatRecorded("2026-11-08T22:34:28-06:00")).toBe(
      "9 Nov 2026, 04:34 UTC",
    );
  });

  it.each([
    ["2026-01-01T00:00:00Z", "1 Jan 2026, 00:00 UTC"],
    ["2026-12-31T23:59:59Z", "31 Dec 2026, 23:59 UTC"],
  ])("names the month of %s", (recorded, text) => {
    expect(formatRecorded(recorded)).toBe(text);
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

describe("captureName", () => {
  it.each([
    [String.raw`C:\captures\2026-11-09T0434Z-overnight.edges.txt`, "overnight"],
    ["captures/2026-11-08T2300Z-desk.edges.txt", "desk"],
    ["2026-11-02T0310Z-home-west-window.edges.txt", "home-west-window"],
  ])("reads the label of %s", (path, label) => {
    expect(captureName(path)).toBe(label);
  });

  it("gives the file's name when it is not named as a capture is", () => {
    expect(captureName(String.raw`C:\captures\three-minutes.edges.txt`)).toBe(
      "three-minutes",
    );
  });

  it("gives nothing for a path that names no file", () => {
    expect(captureName("")).toBe("");
  });
});

describe("captureLabel", () => {
  it("joins the label, the time recorded, and the length", () => {
    expect(
      captureLabel({
        path: "captures/2026-11-09T0434Z-overnight.edges.txt",
        recorded: "2026-11-09T04:34:28Z",
        minutes: 524.9,
      }),
    ).toBe("overnight · 9 Nov 2026, 04:34 UTC · 8 h 45 min");
  });

  it("leaves the label out when the path names no file", () => {
    expect(
      captureLabel({
        path: "",
        recorded: "2026-11-09T04:34:28Z",
        minutes: 3,
      }),
    ).toBe("9 Nov 2026, 04:34 UTC · 3 min");
  });
});
