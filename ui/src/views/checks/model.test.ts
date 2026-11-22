import { describe, expect, it } from "vitest";
import type { ValidationFailure } from "@/protocol/messages";
import type { FrameRow } from "@/views/frames-table/model";
import { checkLines, checksLine, frameTime, verdict } from "./model";

/** A row of the table, accepted unless it is given failures. */
const row = (number: number, failures: ValidationFailure[] = []): FrameRow => ({
  number,
  symbols: [],
  minute: 5,
  hour: 9,
  dayOfYear: 344,
  year: 26,
  ut1: "+0.3 s",
  dst: "standard",
  accepted: failures.length === 0,
  result: "",
  failures,
});

describe("checkLines", () => {
  it("lists the ten checks in the host's order, each with a capital", () => {
    expect(checkLines([]).map((line) => [line.check, line.name])).toEqual([
      ["markers", "Markers"],
      ["unusedBits", "Unused bits"],
      ["errorSymbols", "Error symbols"],
      ["bcdDigits", "BCD digits"],
      ["minuteRange", "Minute range"],
      ["hourRange", "Hour range"],
      ["dayOfYearRange", "Day of year range"],
      ["ut1Sign", "UT1 sign"],
      ["ut1Range", "UT1 range"],
      ["consecutive", "Consecutive"],
    ]);
  });

  it("passes every check of a frame with no failure, and says what each asks", () => {
    const lines = checkLines([]);

    expect(lines.every((line) => line.passed)).toBe(true);
    expect(lines[0].text).toBe(
      "A marker in each of the seven marker seconds, and nowhere else",
    );
    expect(lines[9].text).toBe(
      "The time is the last accepted frame's plus the minutes since",
    );
    // Every check has a line of its own to say.
    expect(new Set(lines.map((line) => line.text)).size).toBe(10);
  });

  it("fails the checks the host reports, each with the host's detail", () => {
    const lines = checkLines([
      { check: "errorSymbols", detail: "Error at seconds 6, 31" },
      {
        check: "consecutive",
        detail: "expected 2026-12-10T19:31Z, got 2026-12-10T19:35Z",
      },
    ]);

    expect(
      lines
        .filter((line) => !line.passed)
        .map((line) => [line.name, line.text]),
    ).toEqual([
      ["Error symbols", "Error at seconds 6, 31"],
      ["Consecutive", "expected 2026-12-10T19:31Z, got 2026-12-10T19:35Z"],
    ]);
    expect(lines.filter((line) => line.passed)).toHaveLength(8);
  });

  it("keeps a failure's detail even when it is empty", () => {
    const [markers] = checkLines([{ check: "markers", detail: "" }]);

    expect(markers).toMatchObject({ passed: false, text: "" });
  });
});

describe("frameTime", () => {
  it("writes the frame's hour, minute, and day as they were decoded", () => {
    expect(frameTime(row(1))).toBe("09:05 day 344");
  });
});

describe("verdict", () => {
  it("is Accepted for a frame the host accepted", () => {
    expect(verdict(row(1))).toBe("Accepted");
  });

  it("counts the failed checks of a rejected frame", () => {
    const rejected = row(1, [
      { check: "markers", detail: "second 9 is Zero" },
      { check: "bcdDigits", detail: "minute units digit is 12" },
    ]);

    expect(verdict(rejected)).toBe("Rejected, 2 of 10 checks failed");
  });
});

describe("checksLine", () => {
  it("is empty before the first frame", () => {
    expect(checksLine(null, null)).toBe("");
  });

  it("names the newest frame while none is selected", () => {
    expect(checksLine(row(41), null)).toBe("Frame #41, the newest");
  });

  it("says so when the frame shown is the one selected", () => {
    expect(checksLine(row(39), 39)).toBe("Frame #39, selected");
  });

  it("names the newest again when the selected frame is no longer on show", () => {
    expect(checksLine(row(41), 12)).toBe("Frame #41, the newest");
  });
});
