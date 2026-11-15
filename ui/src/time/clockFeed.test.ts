import { describe, expect, it } from "vitest";
import type { HostMessage, SourceState } from "@/protocol/messages";
import { feedClock } from "./clockFeed";
import { createSourceClock } from "./sourceClock";

const edges = (...micros: number[]): HostMessage => ({
  type: "edges",
  seq: 1,
  payload: {
    items: micros.map((m, i) => ({
      micros: m,
      level: i % 2 === 0 ? "high" : "low",
    })),
  },
});

const status = (state: SourceState, detail = ""): HostMessage => ({
  type: "sourceStatus",
  seq: 2,
  payload: { source: "synthetic", name: "synthetic", state, detail },
});

/** A clock whose wall time stands still, so its reading is its anchor. */
const start = () => {
  const clock = createSourceClock(() => 0);
  return { clock, feed: feedClock(clock) };
};

describe("feedClock", () => {
  it("anchors the clock on the newest edge of a batch", () => {
    const { clock, feed } = start();

    feed(edges(12_000_000, 12_200_412, 13_000_003));

    expect(clock.now()).toBe(13_000_003);
  });

  it("discards the anchor on a source reset", () => {
    const { clock, feed } = start();
    feed(edges(12_000_000));

    feed(status("running", "reset: loop"));

    expect(clock.now()).toBeNull();
  });

  it.each<SourceState>(["stopped", "error"])(
    "discards the anchor when the source is %s",
    (state) => {
      const { clock, feed } = start();
      feed(edges(12_000_000));

      feed(status(state));

      expect(clock.now()).toBeNull();
    },
  );

  it("keeps the anchor through a running status and another stage's message", () => {
    const { clock, feed } = start();
    feed(edges(12_000_000));

    feed(status("running", "# wwvb-edge-logger 0.1.0 pin=2"));
    feed({
      type: "pulse",
      seq: 3,
      payload: {
        startMicros: 11_000_000,
        durationMicros: 200_412,
        endMicros: 11_200_412,
      },
    });

    expect(clock.now()).toBe(12_000_000);
  });

  it("ignores an empty batch", () => {
    const { clock, feed } = start();

    feed(edges());

    expect(clock.now()).toBeNull();
  });
});
