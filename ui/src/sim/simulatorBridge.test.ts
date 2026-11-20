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

  /** The time of the first frame the clock reports, a little over a minute into a run at this noise. */
  const firstFrameUtc = (noise: number): string | null | undefined => {
    const bridge = createSimulatorBridge("");
    const received: HostMessage[] = [];
    bridge.onMessage((message) => received.push(message));

    bridge.send({ command: "selectSource", source: "synthetic", noise });
    vi.advanceTimersByTime(65_000);

    return received.find((message) => message.type === "clock")?.payload
      .frameUtc;
  };

  it("plays the clean fixture at noise 0 and the noisy one above it", () => {
    // The two were recorded on different days, which is how a test can tell them apart.
    expect(firstFrameUtc(0)).toBe("2026-11-14T04:39:00Z");
    expect(firstFrameUtc(0.05)).toBe("2026-12-10T19:30:00Z");
  });
});
