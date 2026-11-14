import { describe, expect, it } from "vitest";
import type {
  HostMessage,
  SymbolKind,
  DecodedFrame,
  ClockState,
  ClockStatus,
  FrameSyncEvent,
} from "@/protocol/messages";
import { initialState, reduce } from "@/state/appState";
import {
  edgesStage,
  symbolsStage,
  pulsesStage,
  decodeStage,
  validateStage,
  clockStage,
  frameSyncStage,
  stages,
} from "./stages";

const play = (...messages: HostMessage[]) =>
  messages.reduce(reduce, initialState);

describe("edgesStage", () => {
  it("shows a dash and an idle dot before any edge", () => {
    expect(edgesStage(initialState)).toEqual({ readout: "—", health: "idle" });
  });

  it("shows the last edge as the serial line writes it", () => {
    const state = play({
      type: "edges",
      seq: 1,
      payload: {
        items: [
          { micros: 2_617_009_000, level: "high" },
          { micros: 2_617_209_411, level: "low" },
        ],
      },
    });

    expect(edgesStage(state)).toEqual({
      readout: "L 2617209411",
      health: "good",
    });
  });

  it("turns bad when the source reports an error, with or without edges", () => {
    const edge: HostMessage = {
      type: "edges",
      seq: 1,
      payload: { items: [{ micros: 2_617_009_000, level: "high" }] },
    };
    const error: HostMessage = {
      type: "sourceStatus",
      seq: 2,
      payload: {
        state: "error",
        source: "serial",
        name: "serial COM5",
        detail: "Access to the port 'COM5' was denied",
      },
    };

    expect(edgesStage(play(error))).toEqual({ readout: "—", health: "bad" });
    expect(edgesStage(play(edge, error))).toEqual({
      readout: "H 2617009000",
      health: "bad",
    });
  });
});

describe("pulsesStage", () => {
  it("shows a dash and an idle dot before any pulse", () => {
    expect(pulsesStage(initialState)).toEqual({ readout: "—", health: "idle" });
  });

  it("shows the last pulse's duration in whole milliseconds", () => {
    const state = play({
      type: "pulse",
      seq: 1,
      payload: {
        startMicros: 0,
        endMicros: 502_700,
        durationMicros: 502_700,
      },
    });
    expect(pulsesStage(state)).toEqual({
      readout: "503 ms",
      health: "good",
    });
  });
});

describe("symbolsStage", () => {
  const symbol = (seq: number, kind: SymbolKind): HostMessage => ({
    type: "symbol",
    seq,
    payload: { startMicros: seq * 1_000_000, durationMicros: 200_000, kind },
  });

  const zeros = (from: number, count: number) =>
    Array.from({ length: count }, (_, i) => symbol(from + i, "zero"));

  it("shows a dash and an idle dot before any symbol", () => {
    expect(symbolsStage(initialState)).toEqual({
      readout: "—",
      health: "idle",
    });
  });

  it("warns while an error is among the last 60 symbols", () => {
    const stillInside = play(symbol(1, "error"), ...zeros(2, 59));
    const pushedOut = play(symbol(1, "error"), ...zeros(2, 60));

    expect(symbolsStage(stillInside).health).toBe("warn");
    expect(symbolsStage(pushedOut).health).toBe("good");
  });

  it("shows the last symbol's kind as its word", () => {
    const state = play(symbol(1, "zero"), symbol(2, "one"));

    expect(symbolsStage(state)).toEqual({ readout: "one", health: "good" });
  });
});

const decoded = (over: Partial<DecodedFrame> = {}): DecodedFrame => ({
  frame: {
    startMicros: 1_000_000,
    endMicros: 60_800_000,
    symbols: Array(60).fill("zero"),
  },
  minute: 32,
  hour: 19,
  dayOfYear: 344,
  year: 26,
  ut1Sign: "plus",
  ut1Tenths: 0,
  leapYear: false,
  leapSecondPending: false,
  dst: "standard",
  errorSeconds: [],
  ...over,
});

const clock = (status: ClockStatus): ClockState => ({
  status,
  frameUtc: null,
  frameStartMicros: null,
  frameEndMicros: null,
  offsetFromPcMs: null,
  consecutiveAccepted: 0,
  rejectedSinceLastAccepted: 0,
});

describe("decodeStage", () => {
  it("shows a dash and an idle dot before any decoded frame", () => {
    expect(decodeStage(initialState)).toEqual({
      readout: "—",
      health: "idle",
    });
  });

  it("shows the hour, minute, and day of year", () => {
    const state = play({ type: "decodedFrame", seq: 1, payload: decoded() });
    expect(decodeStage(state)).toEqual({
      readout: "19:32 day 344",
      health: "good",
    });
  });

  it("pads the hour and minute to two digits", () => {
    const state = play({
      type: "decodedFrame",
      seq: 1,
      payload: decoded({ hour: 7, minute: 5, dayOfYear: 9 }),
    });
    expect(decodeStage(state)).toEqual({
      readout: "07:05 day 9",
      health: "good",
    });
  });

  it("warns when a field second held an error", () => {
    const state = play({
      type: "decodedFrame",
      seq: 1,
      payload: decoded({ errorSeconds: [6] }),
    });
    expect(decodeStage(state)).toEqual({
      readout: "19:32 day 344",
      health: "warn",
    });
  });
});

describe("validateStage", () => {
  it("a dash and an idle dot before any result", () => {
    expect(validateStage(initialState)).toEqual({
      readout: "—",
      health: "idle",
    });
  });

  it("shows accepted with a good dot", () => {
    const state = play({
      type: "validation",
      seq: 1,
      payload: {
        frame: decoded(),
        accepted: true,
        failures: [],
      },
    });
    expect(validateStage(state)).toEqual({
      readout: "accepted",
      health: "good",
    });
  });

  it("shows the newest result when a rejection follows", () => {
    const valid: HostMessage = {
      type: "validation",
      seq: 1,
      payload: {
        frame: decoded(),
        accepted: true,
        failures: [],
      },
    };
    const invalid: HostMessage = {
      type: "validation",
      seq: 2,
      payload: {
        frame: decoded(),
        accepted: false,
        failures: [{ check: "consecutive", detail: "some detail" }],
      },
    };
    expect(validateStage(play(valid, invalid))).toEqual({
      readout: "rejected",
      health: "bad",
    });
  });
});

describe("clockStage", () => {
  it("a dash and an idle dot before any clock message", () => {
    expect(clockStage(initialState)).toEqual({
      readout: "—",
      health: "idle",
    });
  });

  it.each([
    ["locked", "locked", "good"],
    ["acquiring", "acquiring", "warn"],
    ["holding", "holding", "warn"],
    ["noSignal", "no signal", "idle"],
  ] as const)("reads %s as '%s' with a %s dot", (status, readout, health) => {
    const state = play({ type: "clock", seq: 1, payload: clock(status) });

    expect(clockStage(state)).toEqual({ readout, health });
  });
});

const sync = (seq: number, payload: FrameSyncEvent): HostMessage => ({
  type: "frameSync",
  seq,
  payload,
});

describe("frameSyncStage", () => {
  it("a dash and an idle dot before any frame sync event", () => {
    expect(frameSyncStage(initialState)).toEqual({
      readout: "—",
      health: "idle",
    });
  });

  it("reads searching with a warn dot", () => {
    const state = play(sync(1, { kind: "searching", consecutiveMarkers: 1 }));
    expect(frameSyncStage(state)).toEqual({
      readout: "searching",
      health: "warn",
    });
  });

  it("counts the seconds placed", () => {
    const state = play(
      sync(1, {
        kind: "slotFilled",
        frameStartMicros: 0,
        second: 42,
        symbol: "one",
      }),
    );
    expect(frameSyncStage(state)).toEqual({
      readout: "43 of 60",
      health: "good",
    });
  });

  it("reads a complete frame as 60 of 60", () => {
    const state = play(
      sync(1, {
        kind: "frameComplete",
        frame: decoded().frame,
      }),
    );
    expect(frameSyncStage(state)).toEqual({
      readout: "60 of 60",
      health: "good",
    });
  });

  it("turns bad while a sync loss is held", () => {
    const state = play(
      sync(1, {
        kind: "syncLost",
        reason: "expected marker at second 9, got Zero",
      }),
      sync(2, {
        kind: "searching",
        consecutiveMarkers: 0,
      }),
    );
    expect(frameSyncStage(state)).toEqual({
      readout: "searching",
      health: "bad",
    });
  });

  it("keeps the readout of the latest event while a sync loss is held", () => {
    const state = play(
      sync(1, {
        kind: "syncLost",
        reason: "expected bit at second 58, got Marker",
      }),
      sync(2, { kind: "searching", consecutiveMarkers: 1 }),
      sync(3, {
        kind: "slotFilled",
        frameStartMicros: 0,
        second: 0,
        symbol: "marker",
      }),
    );

    expect(frameSyncStage(state)).toEqual({
      readout: "1 of 60",
      health: "bad",
    });
  });

  it("reads searching at the instant sync is lost", () => {
    const state = play(
      sync(1, { kind: "syncLost", reason: "gap 2300 ms before second 14" }),
    );

    expect(frameSyncStage(state)).toEqual({
      readout: "searching",
      health: "bad",
    });
  });
});

describe("stages", () => {
  it("lists the seven stages in pipeline order", () => {
    expect(stages.map((s) => `${s.number} ${s.name}`)).toEqual([
      "1 Edges",
      "2 Pulses",
      "3 Symbols",
      "4 Frame sync",
      "5 Decode",
      "6 Validate",
      "7 Clock",
    ]);
  });

  it("every stage is idle before a source starts", () => {
    expect(stages.map((s) => s.view(initialState))).toEqual(
      stages.map(() => ({ readout: "—", health: "idle" })),
    );
  });
});
