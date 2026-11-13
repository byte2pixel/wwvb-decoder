import { describe, expect, it } from "vitest";
import { initialState, reduce, type AppState } from "./appState";
import type {
  CaptureEntry,
  ClockState,
  DecodedFrame,
  Edge,
  FrameSyncEvent,
  HostMessage,
  SourceStatus,
  ValidationResult,
} from "@/protocol/messages";

const info = (seq: number): HostMessage => ({
  type: "info",
  seq,
  payload: { line: "#" },
});

const reset = (seq: number): HostMessage => ({
  type: "sourceStatus",
  seq,
  payload: {
    source: "replay",
    name: "replay",
    state: "running",
    detail: "reset: loop",
  },
});

const validation = (minute: number): ValidationResult => ({
  frame: {
    frame: {
      startMicros: 0,
      endMicros: 59_800_000,
      symbols: Array(60).fill("zero"),
    },
    minute,
    hour: 19,
    dayOfYear: 344,
    year: 26,
    ut1Sign: "plus",
    ut1Tenths: 0,
    leapYear: false,
    leapSecondPending: false,
    dst: "standard",
    errorSeconds: [],
  },
  accepted: true,
  failures: [],
});

describe("reduce", () => {
  it("appends the edges of an edges message, oldest first", () => {
    const state = reduce(initialState, {
      type: "edges",
      seq: 1,
      payload: {
        items: [
          { micros: 12_000_000, level: "high" },
          { micros: 12_200_412, level: "low" },
        ],
      },
    });

    expect(state.edges).toEqual([
      { micros: 12_000_000, level: "high" },
      { micros: 12_200_412, level: "low" },
    ]);
  });

  it("keeps only the newest 2,000 edges", () => {
    const edges = (from: number, count: number): Edge[] =>
      Array.from({ length: count }, (_, i) => ({
        micros: from + i,
        level: "high",
      }));

    let state = reduce(initialState, {
      type: "edges",
      seq: 1,
      payload: { items: edges(0, 1_999) },
    });
    state = reduce(state, {
      type: "edges",
      seq: 2,
      payload: { items: edges(1_999, 3) },
    });

    expect(state.edges).toHaveLength(2_000);
    expect(state.edges[0]).toEqual({ micros: 2, level: "high" });
    expect(state.edges.at(-1)).toEqual({ micros: 2_001, level: "high" });
  });

  it("appends each pulse and keeps only the newest 300", () => {
    const messages = Array.from({ length: 302 }, (_, i): HostMessage => ({
      type: "pulse",
      seq: i + 1,
      payload: {
        startMicros: i * 1_000_000,
        durationMicros: 200_000,
        endMicros: i * 1_000_000 + 200_000,
      },
    }));

    const state = messages.reduce(reduce, initialState);

    expect(state.pulses).toHaveLength(300);
    expect(state.pulses[0].startMicros).toBe(2_000_000);
    expect(state.pulses.at(-1)?.startMicros).toBe(301_000_000);
  });

  it("appends each symbol and keeps only the newest 600", () => {
    const messages = Array.from({ length: 602 }, (_, i): HostMessage => ({
      type: "symbol",
      seq: i + 1,
      payload: {
        startMicros: i * 1_000_000,
        durationMicros: 200_000,
        kind: "zero",
      },
    }));

    const state = messages.reduce(reduce, initialState);

    expect(state.symbols).toHaveLength(600);
    expect(state.symbols[0].startMicros).toBe(2_000_000);
    expect(state.symbols.at(-1)?.startMicros).toBe(601_000_000);
  });

  it("appends each validation and keeps only the newest 20", () => {
    const messages = Array.from({ length: 22 }, (_, i): HostMessage => ({
      type: "validation",
      seq: i + 1,
      payload: validation(i),
    }));

    const state = messages.reduce(reduce, initialState);

    expect(state.validations).toHaveLength(20);
    expect(state.validations[0].frame.minute).toBe(2);
    expect(state.validations.at(-1)?.frame.minute).toBe(21);
  });

  it("appends each info line and keeps only the newest 50", () => {
    const messages = Array.from({ length: 52 }, (_, i): HostMessage => ({
      type: "info",
      seq: i + 1,
      payload: { line: `Info line ${i + 1}` },
    }));

    const state = messages.reduce(reduce, initialState);

    expect(state.infoLines).toHaveLength(50);
    expect(state.infoLines[0]).toBe("Info line 3");
    expect(state.infoLines.at(-1)).toBe("Info line 52");
  });

  it("remembers the latest clock state", () => {
    const clock: ClockState = {
      status: "locked",
      frameUtc: "2026-12-10T19:30:00Z",
      frameStartMicros: 1_000_000,
      frameEndMicros: 60_800_000,
      offsetFromPcMs: 37,
      consecutiveAccepted: 3,
      rejectedSinceLastAccepted: 0,
    };

    const state = reduce(initialState, {
      type: "clock",
      seq: 1,
      payload: clock,
    });

    expect(initialState.clock).toBeNull();
    expect(state.clock).toEqual(clock);
  });

  it("remembers the latest decoded frame", () => {
    const decodedFrame: DecodedFrame = {
      frame: {
        startMicros: 1000000,
        endMicros: 1200000,
        symbols: ["marker"],
      },
      minute: 1,
      hour: 2,
      dayOfYear: 3,
      year: 26,
      ut1Sign: "plus",
      ut1Tenths: 9,
      leapYear: false,
      leapSecondPending: false,
      dst: "dst",
      errorSeconds: [],
    };

    const state = reduce(initialState, {
      type: "decodedFrame",
      seq: 1,
      payload: decodedFrame,
    });

    expect(initialState.decodedFrame).toBeNull();
    expect(state.decodedFrame).toEqual(decodedFrame);
  });

  it("remembers the latest frame sync event", () => {
    const frameSync: FrameSyncEvent = {
      kind: "slotFilled",
      frameStartMicros: 1_000_000,
      second: 1,
      symbol: "zero",
    };

    const state = reduce(initialState, {
      type: "frameSync",
      seq: 1,
      payload: frameSync,
    });

    expect(initialState.frameSync).toBeNull();
    expect(state.frameSync).toEqual(frameSync);
  });

  it("remembers the latest source status", () => {
    const sourceStatus: SourceStatus = {
      source: "serial",
      name: "COM3",
      detail: "Some detail",
      state: "running",
    };

    const state = reduce(initialState, {
      type: "sourceStatus",
      seq: 1,
      payload: sourceStatus,
    });

    expect(initialState.sourceStatus).toBeNull();
    expect(state.sourceStatus).toEqual(sourceStatus);
  });

  it("remembers the latest ports", () => {
    const ports = ["COM3", "COM4"];

    const state = reduce(initialState, {
      type: "ports",
      seq: 1,
      payload: { items: ports },
    });

    expect(initialState.ports).toHaveLength(0);
    expect(state.ports).toEqual(ports);
  });

  it("remembers the latest captures", () => {
    const captures: CaptureEntry[] = [
      { path: "some/path1", recorded: "2026-06-18T12:00:00Z", minutes: 1 },
      { path: "some/path2", recorded: "2026-06-19T12:00:00Z", minutes: 2 },
    ];

    const state = reduce(initialState, {
      type: "captures",
      seq: 1,
      payload: { items: captures },
    });

    expect(initialState.captures).toHaveLength(0);
    expect(state.captures).toEqual(captures);
  });

  it("clears the stream on a source reset and keeps the lists", () => {
    const before: HostMessage[] = [
      {
        type: "captures",
        seq: 0,
        payload: {
          items: [
            {
              path: "captures/a.edges.txt",
              recorded: "2026-11-02T03:10:27Z",
              minutes: 487.5,
            },
          ],
        },
      },
      { type: "ports", seq: 1, payload: { items: ["COM5"] } },
      {
        type: "edges",
        seq: 2,
        payload: { items: [{ micros: 1, level: "high" }] },
      },
      {
        type: "info",
        seq: 3,
        payload: { line: "# wall 2026-11-02T03:11:00Z 14500398" },
      },
      { type: "validation", seq: 4, payload: validation(30) },
      {
        type: "frameSync",
        seq: 5,
        payload: { kind: "searching", consecutiveMarkers: 1 },
      },
    ];
    const loopReset = reset(6);

    const state = [...before, loopReset].reduce(reduce, initialState);

    expect(state).toEqual({
      ...initialState,
      ports: ["COM5"],
      captures: [
        {
          path: "captures/a.edges.txt",
          recorded: "2026-11-02T03:10:27Z",
          minutes: 487.5,
        },
      ],
      sourceStatus: loopReset.payload,
      lastSeq: 6,
      log: state.log,
    });
  });

  it("keeps the stream on a source status that is not a reset", () => {
    const state = [
      {
        type: "edges",
        seq: 1,
        payload: { items: [{ micros: 1, level: "high" }] },
      },
      {
        type: "sourceStatus",
        seq: 2,
        payload: { source: "none", name: "", state: "stopped", detail: "" },
      },
    ].reduce<AppState>(
      (current, message) => reduce(current, message as HostMessage),
      initialState,
    );

    expect(state.edges).toHaveLength(1);
  });

  it("counts frames since the last source reset", () => {
    const frames = Array.from({ length: 22 }, (_, i): HostMessage => ({
      type: "validation",
      seq: i + 1,
      payload: validation(i),
    }));

    const counted = frames.reduce(reduce, initialState);
    const afterReset = reduce(counted, {
      type: "sourceStatus",
      seq: 23,
      payload: {
        source: "replay",
        name: "replay",
        state: "running",
        detail: "reset: loop",
      },
    });

    expect(counted.frameCount).toBe(22);
    expect(afterReset.frameCount).toBe(0);
  });

  it("tracks the last sequence number and counts the gaps", () => {
    const state = [info(1), info(2), info(5), info(6)].reduce(
      reduce,
      initialState,
    );

    expect(state.lastSeq).toBe(6);
    expect(state.lostMessages).toBe(2);
  });

  it("keeps the sequence tracking across a source reset", () => {
    const state = [info(1), info(4), reset(5)].reduce(reduce, initialState);

    expect(state.lastSeq).toBe(5);
    expect(state.lostMessages).toBe(2);
  });

  it("counts nothing when the sequence restarts", () => {
    const state = [info(10), info(1), info(2)].reduce(reduce, initialState);

    expect(state.lastSeq).toBe(2);
    expect(state.lostMessages).toBe(0);
  });

  it("records every message in the protocol log, across a reset", () => {
    const state = [info(1), reset(2)].reduce(reduce, initialState);

    expect(state.log.entries).toEqual([info(1), reset(2)]);
    expect(state.log.counts.info).toBe(1);
    expect(state.log.counts.sourceStatus).toBe(1);
  });
});
