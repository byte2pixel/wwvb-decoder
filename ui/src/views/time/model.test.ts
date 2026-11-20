import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import {
  hostMessageSchema,
  type ClockState,
  type ClockStatus,
  type DstStatus,
} from "@/protocol/messages";
import {
  clockFace,
  decodedTimeMs,
  localTimeRow,
  lockRows,
  offsetRow,
  statusChips,
} from "./model";

/** The host's clock example: locked on the frame of 19:30, which began 1 s into the source's time. */
const example = ((): ClockState => {
  const file = new URL(
    "../../../../docs/design/message-examples/clock.json",
    import.meta.url,
  );
  const message = hostMessageSchema.parse(
    JSON.parse(readFileSync(file, "utf8")),
  );
  if (message.type !== "clock") throw new Error("not a clock");
  return message.payload;
})();

/** The example, with the frame that began at 1 s reading this time. */
const lockedAt = (frameUtc: string | null): ClockState => ({
  ...example,
  frameUtc,
});

describe("clockFace", () => {
  const noTime = { digits: "--:--:--", date: "", dayOfYear: "" };

  it("reads no time before the clock has any state", () => {
    expect(clockFace(null, 5_000_000)).toEqual(noTime);
  });

  it("reads no time before the first accepted frame", () => {
    const noSignal: ClockState = {
      ...example,
      status: "noSignal",
      frameUtc: null,
      frameStartMicros: null,
      frameEndMicros: null,
      offsetFromPcMs: null,
    };

    expect(clockFace(noSignal, 5_000_000)).toEqual(noTime);
  });

  it("reads the frame's time plus the source time since the frame began", () => {
    // 7.5 s after the frame's start at 1 s. The half second is cut off.
    expect(clockFace(example, 8_500_000)).toEqual({
      digits: "19:30:07",
      date: "2026-12-10",
      dayOfYear: "day 344",
    });
  });

  it("carries on past the frame, the minute, and the hour", () => {
    // 45 minutes and 12 s after the frame's start.
    expect(clockFace(example, 1_000_000 + 2_712_000_000).digits).toBe(
      "20:15:12",
    );
  });

  it("carries over into the next day and the next year", () => {
    // 61.2 s after the frame of 23:59 on the last day of 2026.
    expect(clockFace(lockedAt("2026-12-31T23:59:00Z"), 62_200_000)).toEqual({
      digits: "00:00:01",
      date: "2027-01-01",
      dayOfYear: "day 1",
    });
  });

  it("counts the leap day in the day of the year", () => {
    expect(
      clockFace(lockedAt("2028-12-31T12:00:00Z"), 1_000_000).dayOfYear,
    ).toBe("day 366");
  });

  it("reads the time at the frame's end when there is no present", () => {
    // The example's frame ends 59.8 s after it began.
    expect(clockFace(example, null).digits).toBe("19:30:59");
  });
});

describe("statusChips", () => {
  it("lists the four statuses in order, with no signal before any clock message", () => {
    expect(statusChips(null)).toEqual([
      { status: "noSignal", label: "No signal", current: true },
      { status: "acquiring", label: "Acquiring", current: false },
      { status: "locked", label: "Locked", current: false },
      { status: "holding", label: "Holding", current: false },
    ]);
  });

  it.each<ClockStatus>(["noSignal", "acquiring", "locked", "holding"])(
    "marks %s, and only it, as the current status",
    (status) => {
      const current = statusChips({ ...example, status })
        .filter((chip) => chip.current)
        .map((chip) => chip.status);

      expect(current).toEqual([status]);
    },
  );
});

describe("lockRows", () => {
  it("has no frame and a count of 0 before any clock message", () => {
    expect(lockRows(null)).toEqual([
      { label: "Last accepted frame", value: "—" },
      { label: "Accepted in a row", value: "0" },
    ]);
  });

  it("reads the last accepted frame's time of day and the run of accepted frames", () => {
    // The example: locked on the frame of 19:30, the third accepted in a row.
    expect(lockRows(example)).toEqual([
      { label: "Last accepted frame", value: "19:30 UTC" },
      { label: "Accepted in a row", value: "3" },
    ]);
  });

  it("counts the rejected frames since then against the three that end a lock", () => {
    const holding: ClockState = {
      ...example,
      status: "holding",
      consecutiveAccepted: 0,
      rejectedSinceLastAccepted: 2,
    };

    expect(lockRows(holding)).toEqual([
      { label: "Last accepted frame", value: "19:30 UTC" },
      { label: "Rejected since then", value: "2 of 3" },
    ]);
  });
});

describe("decodedTimeMs", () => {
  it("is the frame's time plus the source time since the frame began", () => {
    expect(decodedTimeMs(example, 8_500_000)).toBe(
      Date.parse("2026-12-10T19:30:07.500Z"),
    );
  });

  it("is null before the first accepted frame", () => {
    expect(decodedTimeMs(null, 8_500_000)).toBeNull();
    expect(decodedTimeMs(lockedAt(null), 8_500_000)).toBeNull();
  });
});

describe("offsetRow", () => {
  it("shows the host's offset from the PC clock for a source that is not a replay", () => {
    expect(offsetRow(example, "synthetic", [])).toEqual({
      label: "Offset from the PC clock",
      value: "+37 ms",
    });
  });

  it("writes an offset behind the PC clock with a minus", () => {
    const behind = { ...example, offsetFromPcMs: -12 };

    expect(offsetRow(behind, "serial", []).value).toBe("-12 ms");
  });

  it("shows a dash before the first accepted frame", () => {
    expect(offsetRow(null, "serial", []).value).toBe("—");
  });

  it("shows the recorded offset under replay, from the newest wall line", () => {
    // The example's frame of 19:30:00 began at 1 s. At 11.04 s the decoder reads 19:30:10.040.
    const row = offsetRow(example, "replay", [
      "# capture v1",
      "# wall 2026-12-10T19:30:05Z 6100000",
      "# dropped 1",
      "# wall 2026-12-10T19:30:10Z 11040000",
    ]);

    expect(row).toEqual({ label: "Recorded offset", value: "+40 ms" });
  });

  it.each([
    ["no wall line has arrived", ["# capture v1"]],
    ["the wall line cannot be read", ["# wall yesterday 11040000"]],
  ])("shows a dash under replay while %s", (_, infoLines) => {
    expect(offsetRow(example, "replay", infoLines)).toEqual({
      label: "Recorded offset",
      value: "—",
    });
  });
});

describe("localTimeRow", () => {
  const at = Date.parse("2026-12-10T19:30:07.500Z");

  it.each<[DstStatus, string]>([
    ["standard", "13:30:07 CST"],
    ["dst", "14:30:07 CDT"],
    ["beginsToday", "13:30:07 CST, approximate"],
    ["endsToday", "14:30:07 CDT, approximate"],
  ])("reads 19:30:07 UTC with DST %s as %s", (dst, value) => {
    expect(localTimeRow(at, dst)).toEqual({
      label: "Local time, US Central",
      value,
    });
  });

  it("goes back over midnight", () => {
    expect(
      localTimeRow(Date.parse("2026-12-10T03:15:00Z"), "standard").value,
    ).toBe("21:15:00 CST");
  });

  it("shows a dash without a time or without a decoded frame", () => {
    expect(localTimeRow(null, "standard").value).toBe("—");
    expect(localTimeRow(at, undefined).value).toBe("—");
  });
});
