import type { HostMessage } from "@/protocol/messages";
import { appendMessage, emptyLog } from "./log";
import { describe, expect, it } from "vitest";

describe("appendMessage", () => {
  it("appends messages and counts them", () => {
    const message: HostMessage = {
      type: "info",
      seq: 1,
      payload: { line: "# test" },
    };

    const pulseMessage: HostMessage = {
      type: "pulse",
      seq: 2,
      payload: {
        startMicros: 0,
        endMicros: 1000,
        durationMicros: 1000,
      },
    };
    const newLog = appendMessage(emptyLog, message);
    const newerLog = appendMessage(newLog, message);
    const newestLog = appendMessage(newerLog, pulseMessage);
    expect(newestLog.entries).toEqual([message, message, pulseMessage]);
    expect(newestLog.counts.info).toEqual(2);
    expect(newestLog.counts.pulse).toEqual(1);
  });

  it("keeps the last 200 entries", () => {
    let log = emptyLog;
    for (let i = 0; i < 250; i++) {
      const message: HostMessage = {
        type: "info",
        seq: i,
        payload: { line: `# test ${i}` },
      };
      log = appendMessage(log, message);
    }
    expect(log.entries).toHaveLength(200);
    expect(log.entries[0].seq).toBe(50);
    expect(log.entries[199].seq).toBe(249);
  });

  it("does not mutate the emptyLog", () => {
    const message: HostMessage = {
      type: "info",
      seq: 1,
      payload: { line: "# test" },
    };
    appendMessage(emptyLog, message);
    expect(emptyLog.entries).toHaveLength(0);
  });
});
