import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { HostMessage } from "@/protocol/messages";
import { createSimulator } from "./simulator";
import type { RecordedMessage } from "./recording";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const line = (t: number, text: string): RecordedMessage => ({
  t,
  message: { type: "info", seq: 99, payload: { line: text } },
});

/** What arrived, in short: an info message by its line, anything else by its type. */
const summary = (messages: HostMessage[]) =>
  messages.map((message) =>
    message.type === "info" ? message.payload.line : message.type,
  );

/** The message example. The host's tests hold it equal to the host's layout table. */
const layoutExample: object = JSON.parse(
  readFileSync(
    fileURLToPath(
      new URL(
        "../../../docs/design/message-examples/frameLayout.json",
        import.meta.url,
      ),
    ),
    "utf8",
  ),
);

const select = {
  command: "selectSource",
  source: "synthetic",
  noise: 0,
} as const;

describe("createSimulator", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("answers hello with the source status, the ports, the captures, and the frame layout", () => {
    // Arrange
    const simulator = createSimulator([]);
    const received: HostMessage[] = [];
    simulator.onMessage((message) => received.push(message));

    // Act & Assert
    simulator.send({ command: "hello", protocol: 1 });
    expect(received).toEqual([]); // nothing arrives inside send

    vi.advanceTimersByTime(0);
    expect(received).toEqual([
      {
        type: "sourceStatus",
        seq: 1,
        payload: {
          source: "none",
          name: "",
          state: "stopped",
          detail: "",
          speed: 1,
        },
      },
      { type: "ports", seq: 2, payload: { items: [] } },
      { type: "captures", seq: 3, payload: { items: [] } },
      { ...layoutExample, seq: 4 },
    ]);
  });

  it("plays the recording at its own times, counted from the first line", () => {
    // Arrange
    const simulator = createSimulator([
      line(20_000, "A"),
      line(20_100, "B"),
      line(21_000, "C"),
    ]);
    const received: HostMessage[] = [];
    simulator.onMessage((message) => received.push(message));

    // Act & Assert
    simulator.send(select);
    expect(summary(received)).toEqual([]);

    vi.advanceTimersByTime(0);
    expect(summary(received)).toEqual(["A"]);

    vi.advanceTimersByTime(99);
    expect(summary(received)).toEqual(["A"]);

    vi.advanceTimersByTime(1);
    expect(summary(received)).toEqual(["A", "B"]);

    vi.advanceTimersByTime(900);
    expect(summary(received)).toEqual(["A", "B", "C"]);
  });

  it("numbers every message it sends, in order", () => {
    // Arrange
    const simulator = createSimulator([line(0, "A"), line(10, "B")]);
    const received: HostMessage[] = [];
    simulator.onMessage((message) => received.push(message));

    // Act
    simulator.send({ command: "hello", protocol: 1 });
    simulator.send(select);
    vi.advanceTimersByTime(10);

    // Assert
    expect(received.map((message) => message.seq)).toEqual([1, 2, 3, 4, 5, 6]);
  });

  it("stops playing and reports stopped", () => {
    // Arrange
    const simulator = createSimulator([line(0, "A"), line(100, "B")]);
    const received: HostMessage[] = [];
    simulator.onMessage((message) => received.push(message));

    // Act
    simulator.send(select);
    vi.advanceTimersByTime(0);

    simulator.send({ command: "stop" });
    expect(summary(received)).toEqual(["A"]);
    vi.advanceTimersByTime(1_000);

    // Assert
    expect(summary(received)).toEqual(["A", "sourceStatus"]);
    expect(received.at(-1)).toMatchObject({
      payload: { source: "none", state: "stopped" },
    });
  });

  it("starts over when a source is selected again", () => {
    const simulator = createSimulator([line(0, "A"), line(100, "B")]);
    const received: HostMessage[] = [];
    simulator.onMessage((message) => received.push(message));
    simulator.send(select);
    vi.advanceTimersByTime(50);

    simulator.send(select);
    vi.advanceTimersByTime(50);
    expect(summary(received)).toEqual(["A", "A"]);

    vi.advanceTimersByTime(50);
    expect(summary(received)).toEqual(["A", "A", "B"]);
  });

  it("starts over one second after the last line", () => {
    const simulator = createSimulator([line(0, "A"), line(100, "B")]);
    const received: HostMessage[] = [];
    simulator.onMessage((message) => received.push(message));

    simulator.send(select);
    vi.advanceTimersByTime(1_000); // 900 ms into the gap
    expect(summary(received)).toEqual(["A", "B"]);

    vi.advanceTimersByTime(150); // past the gap
    expect(summary(received)).toEqual(["A", "B", "A"]);

    vi.advanceTimersByTime(100);
    expect(summary(received)).toEqual(["A", "B", "A", "B"]);
  });

  it("plays the noisy recording when the synthetic source is asked for with noise", () => {
    const simulator = createSimulator([line(0, "clean")], {
      noisy: [line(0, "noisy"), line(100, "still noisy")],
    });
    const received: HostMessage[] = [];
    simulator.onMessage((message) => received.push(message));

    simulator.send({ ...select, noise: 0.05 });
    vi.advanceTimersByTime(100);
    expect(summary(received)).toEqual(["noisy", "still noisy"]);

    // One second after its last line it is the noisy recording that starts over.
    vi.advanceTimersByTime(1_050);
    expect(summary(received)).toEqual(["noisy", "still noisy", "noisy"]);
  });

  it.each([
    ["the synthetic source at noise 0", select],
    [
      "a replay",
      {
        command: "selectSource",
        source: "replay",
        path: "a.edges.txt",
        speed: 1,
      },
    ],
    [
      "a serial port",
      { command: "selectSource", source: "serial", port: "COM3" },
    ],
  ] as const)("plays the first recording for %s", (_, command) => {
    const simulator = createSimulator([line(0, "clean")], {
      noisy: [line(0, "noisy")],
    });
    const received: HostMessage[] = [];
    simulator.onMessage((message) => received.push(message));

    simulator.send(command);
    vi.advanceTimersByTime(0);

    expect(summary(received)).toEqual(["clean"]);
  });

  it("plays its one recording at any noise when it has no noisy one", () => {
    const simulator = createSimulator([line(0, "A")]);
    const received: HostMessage[] = [];
    simulator.onMessage((message) => received.push(message));

    simulator.send({ ...select, noise: 0.4 });
    vi.advanceTimersByTime(0);

    expect(summary(received)).toEqual(["A"]);
  });

  it("plays faster at a higher speed", () => {
    const simulator = createSimulator([line(0, "A"), line(100, "B")], {
      speed: 10,
    });
    const received: HostMessage[] = [];
    simulator.onMessage((message) => received.push(message));

    simulator.send(select);
    vi.advanceTimersByTime(9);
    expect(summary(received)).toEqual(["A"]);

    vi.advanceTimersByTime(1);
    expect(summary(received)).toEqual(["A", "B"]);

    vi.advanceTimersByTime(90); // most of the one-second gap, at 10x
    expect(summary(received)).toEqual(["A", "B"]);

    vi.advanceTimersByTime(15); // past it
    expect(summary(received)).toEqual(["A", "B", "A"]);
  });

  it("says its speed in every source status it sends", () => {
    const running: RecordedMessage = {
      t: 0,
      message: {
        type: "sourceStatus",
        seq: 99,
        payload: {
          source: "synthetic",
          name: "synthetic",
          state: "running",
          detail: "",
        },
      },
    };
    const simulator = createSimulator([running, line(10, "A")], { speed: 10 });
    const received: HostMessage[] = [];
    simulator.onMessage((message) => received.push(message));

    simulator.send({ command: "hello", protocol: 1 });
    simulator.send(select);
    vi.advanceTimersByTime(1);

    const speeds = received.flatMap((message) =>
      message.type === "sourceStatus" ? [message.payload.speed] : [],
    );
    expect(speeds).toEqual([10, 10]);
  });

  it("stops delivering to a handler that unsubscribed", () => {
    const simulator = createSimulator([line(0, "A"), line(100, "B")]);
    const received: HostMessage[] = [];
    const unsubscribe = simulator.onMessage((message) =>
      received.push(message),
    );
    simulator.send(select);
    vi.advanceTimersByTime(0);

    unsubscribe();
    vi.advanceTimersByTime(100);

    expect(summary(received)).toEqual(["A"]);
  });
});
