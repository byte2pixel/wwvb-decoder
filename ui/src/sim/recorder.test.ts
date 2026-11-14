import { describe, expect, it } from "vitest";
import type { HostMessage } from "@/protocol/messages";
import { createRecorder } from "./recorder";

const info = (seq: number): HostMessage => ({
  type: "info",
  seq,
  payload: { line: "#" },
});

describe("createRecorder", () => {
  it("records nothing until it is started", () => {
    // Arrange
    const recorder = createRecorder(() => 0);

    // Act
    recorder.add(info(1));

    // Assert
    expect(recorder.dump()).toBe("");
  });

  it("writes one line per message, with the time since the start", () => {
    // Arrange
    let ms = 5_000;
    const recorder = createRecorder(() => ms);

    // Act
    recorder.start();
    ms += 40;
    recorder.add(info(7));
    ms += 100;
    recorder.add(info(8));

    // Assert
    expect(recorder.dump()).toBe(
      '{"t":40,"type":"info","seq":7,"payload":{"line":"#"}}\n' +
        '{"t":140,"type":"info","seq":8,"payload":{"line":"#"}}',
    );
  });

  it("rounds the time to a whole millisecond", () => {
    // Arrange
    let ms = 5_000.25;
    const recorder = createRecorder(() => ms);

    // Act
    recorder.start();
    ms += 40.6;
    recorder.add(info(1));

    // Assert
    expect(recorder.dump()).toBe(
      '{"t":41,"type":"info","seq":1,"payload":{"line":"#"}}',
    );
  });

  it("clears the recording when it is started again", () => {
    // Arrange
    const recorder = createRecorder(() => 0);
    recorder.start();
    recorder.add(info(1));

    // Act
    recorder.start();

    // Assert
    expect(recorder.dump()).toBe("");
  });
});
