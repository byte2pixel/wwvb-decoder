import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { HostMessage } from "@/protocol/messages";
import { createSimulator } from "./simulator";
import type { RecordedMessage } from "./recording";

const line = (t: number, text: string): RecordedMessage => ({
  t,
  message: { type: "info", seq: 99, payload: { line: text } },
});

/** What arrived, in short: an info message by its line, anything else by its type. */
const summary = (messages: HostMessage[]) =>
  messages.map((message) =>
    message.type === "info" ? message.payload.line : message.type,
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

  it("answers hello with the source status, the ports, and the captures", () => {
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
        payload: { source: "none", name: "", state: "stopped", detail: "" },
      },
      { type: "ports", seq: 2, payload: { items: [] } },
      { type: "captures", seq: 3, payload: { items: [] } },
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
    expect(received.map((message) => message.seq)).toEqual([1, 2, 3, 4, 5]);
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
