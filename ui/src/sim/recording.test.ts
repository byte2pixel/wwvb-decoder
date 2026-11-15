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

const fixture = readFileSync(
  fileURLToPath(new URL("./fixtures/synthetic-noise-0.jsonl", import.meta.url)),
  "utf8",
);

describe("the synthetic fixture", () => {
  const recording = parseRecording(fixture);
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
