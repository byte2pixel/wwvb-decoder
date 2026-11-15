import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { HostMessage } from "@/protocol/messages";
import { createSimulatorBridge, speedFrom } from "./simulatorBridge";

describe("speedFrom", () => {
  it.each([
    ["?speed=10", 10],
    ["?speed=0.5", 0.5],
    ["", 1],
    ["?speed=fast", 1],
    ["?speed=0", 1],
    ["?speed=-2", 1],
  ])("reads %j as %d", (search, expected) => {
    expect(speedFrom(search)).toBe(expected);
  });
});

describe("createSimulatorBridge", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("plays the recorded fixture", () => {
    const bridge = createSimulatorBridge("");
    const received: HostMessage[] = [];
    bridge.onMessage((message) => received.push(message));

    bridge.send({ command: "selectSource", source: "synthetic", noise: 0 });
    vi.advanceTimersByTime(0);

    expect(received[0]).toMatchObject({
      type: "sourceStatus",
      payload: { source: "synthetic", state: "running" },
    });
  });
});
