import { describe, expect, it } from "vitest";
import { parseRecording } from "./recording";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

describe("parseRecording", () => {
  it("reads one message per line, with its time", () => {
    const text =
      '{"t":40,"type":"info","seq":7,"payload":{"line":"#"}}\r\n' +
      '{"t":140,"type":"ports","seq":8,"payload":{"items":["COM5"]}}\r\n';

    expect(parseRecording(text)).toEqual([
      { t: 40, message: { type: "info", seq: 7, payload: { line: "#" } } },
      {
        t: 140,
        message: { type: "ports", seq: 8, payload: { items: ["COM5"] } },
      },
    ]);
  });

  it("names the line that is not a host message", () => {
    const text =
      '{"t":40,"type":"info","seq":7,"payload":{"line":"#"}}\n' +
      '{"t":50,"type":"info","seq":8,"payload":{}}';

    expect(() => parseRecording(text)).toThrow(/line 2/);
  });

  it("names the line that has no time", () => {
    const text = '{"type":"info","seq":7,"payload":{"line":"#"}}';

    expect(() => parseRecording(text)).toThrow(/line 1/);
  });
});

const read = (name: string): string =>
  readFileSync(
    fileURLToPath(new URL(`./fixtures/${name}`, import.meta.url)),
    "utf8",
  );

describe("the synthetic fixture", () => {
  const recording = parseRecording(read("synthetic-noise-0.jsonl"));
  const messages = recording.map((recorded) => recorded.message);

  it("starts with the host's answer to selectSource", () => {
    expect(messages[0]).toMatchObject({
      type: "sourceStatus",
      payload: { source: "synthetic", state: "running" },
    });
  });

  it("covers at least three minutes of signal", () => {
    const micros = messages.flatMap((message) =>
      message.type === "edges"
        ? message.payload.items.map((edge) => edge.micros)
        : [],
    );

    expect(micros.at(-1)! - micros[0]).toBeGreaterThanOrEqual(180_000_000);
  });

  it("reaches a locked clock", () => {
    const statuses = messages.flatMap((message) =>
      message.type === "clock" ? [message.payload.status] : [],
    );

    expect(statuses).toContain("locked");
  });

  it("is in arrival order", () => {
    const times = recording.map((recorded) => recorded.t);

    expect(times).toEqual(times.toSorted((a, b) => a - b));
  });
});

describe("the noisy synthetic fixture", () => {
  const recording = parseRecording(read("synthetic-noise-0.06.jsonl"));
  const messages = recording.map((recorded) => recorded.message);

  it("starts with the host's answer to selectSource, then its reset", () => {
    expect(messages[0]).toMatchObject({
      type: "sourceStatus",
      payload: { source: "synthetic", state: "running", detail: "" },
    });
    // The reset is what clears the page each time the simulator starts over.
    expect(messages[1]).toMatchObject({
      type: "sourceStatus",
      payload: { detail: "reset: start" },
    });
  });

  it("locks the clock, holds it through a rejected frame, locks again, and holds again", () => {
    const statuses = messages.flatMap((message) =>
      message.type === "clock" ? [message.payload.status] : [],
    );

    expect(statuses).toEqual([
      "acquiring",
      "locked",
      "holding",
      "locked",
      "holding",
    ]);
  });

  it("has two frames rejected for different checks, with an accepted one between", () => {
    const results = messages.flatMap((message) =>
      message.type === "validation"
        ? [message.payload.failures.map((failure) => failure.check)]
        : [],
    );

    expect(results).toEqual([[], [], ["consecutive"], [], ["unusedBits"]]);
  });

  it("rejects the frame with a flipped bit that follows the lost one, and keeps the clock right", () => {
    const frames = messages.flatMap((message) =>
      message.type === "validation" ? [message.payload] : [],
    );
    const clocks = messages.flatMap((message) =>
      message.type === "clock" ? [message.payload.frameUtc] : [],
    );

    // Sent as 19:33, two minutes after the last accepted frame, and read as 19:32.
    expect(frames[2].frame.frame.startMicros).toBe(181_000_000);
    expect(frames[2].frame.minute).toBe(32);
    expect(frames[2].failures).toEqual([
      {
        check: "consecutive",
        detail: "expected 2026-12-10T19:33Z, got 2026-12-10T19:32Z",
      },
    ]);
    expect(clocks).toEqual([
      "2026-12-10T19:30:00Z",
      "2026-12-10T19:31:00Z",
      "2026-12-10T19:31:00Z",
      "2026-12-10T19:34:00Z",
      "2026-12-10T19:34:00Z",
    ]);
  });

  it("loses frame sync twice, once before a frame is rejected and once after", () => {
    const events = messages.flatMap((message) => {
      if (message.type === "validation") {
        return [message.payload.accepted ? "accepted" : "rejected"];
      }
      return message.type === "frameSync" && message.payload.kind === "syncLost"
        ? [message.payload.reason]
        : [];
    });

    expect(events).toEqual([
      "accepted",
      "accepted",
      "gap 2000 ms before second 52",
      "rejected",
      "accepted",
      "rejected",
      "gap 2000 ms before second 15",
    ]);
  });

  it("holds an error symbol in an unused second of its last frame", () => {
    const last = messages.findLast((message) => message.type === "validation");

    expect(last?.payload.frame.frame.symbols[44]).toBe("error");
    expect(last?.payload.frame.errorSeconds).toEqual([]);
  });

  it("is in arrival order", () => {
    const times = recording.map((recorded) => recorded.t);

    expect(times).toEqual(times.toSorted((a, b) => a - b));
  });
});
