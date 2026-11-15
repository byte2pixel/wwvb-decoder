import type { AppState } from "@/state/appState";
import type {
  ClockStatus,
  FrameSyncEvent,
  PinLevel,
  Symbol as WwvbSymbol,
} from "@/protocol/messages";

/** The dot's meaning. `idle` is the text-3 dot: nothing to judge yet. */
export type Health = "idle" | "good" | "warn" | "bad";

/** What the rail shows for one stage. */
export interface StageView {
  readout: string;
  health: Health;
}

export const noData = "—";

const levelChar = (level: PinLevel) => (level === "high" ? "H" : "L");

const edgeHealth = (state: AppState): Health => {
  if (state.sourceStatus?.state === "error") {
    return "bad";
  }
  return state.edges.length === 0 ? "idle" : "good";
};

export function edgesStage(state: AppState): StageView {
  const lastEdge = state.edges.at(-1);
  const health: Health = edgeHealth(state);
  if (!lastEdge) {
    return { readout: noData, health };
  }

  return {
    readout: `${levelChar(lastEdge.level)} ${lastEdge.micros}`,
    health,
  };
}

export function pulsesStage(state: AppState): StageView {
  const lastPulse = state.pulses.at(-1);
  if (!lastPulse) return { readout: noData, health: "idle" };
  return {
    readout: `${Math.round(lastPulse.durationMicros / 1000)} ms`,
    health: "good",
  };
}

const symbolsHealth = (lastSymbols: WwvbSymbol[]): Health => {
  return lastSymbols.some((s) => s.kind === "error") ? "warn" : "good";
};

export function symbolsStage(state: AppState): StageView {
  const last = state.symbols.at(-1);
  if (!last) return { readout: noData, health: "idle" };
  return {
    readout: last.kind,
    health: symbolsHealth(state.symbols.slice(-60)),
  };
}

const pad2 = (s: number) => String(s).padStart(2, "0");

export function decodeStage(state: AppState): StageView {
  if (!state.decodedFrame) return { readout: noData, health: "idle" };
  const lastFrame = state.decodedFrame;
  return {
    readout: `${pad2(lastFrame.hour)}:${pad2(lastFrame.minute)} day ${lastFrame.dayOfYear}`,
    health: lastFrame.errorSeconds.length > 0 ? "warn" : "good",
  };
}

export function validateStage(state: AppState): StageView {
  const lastValidation = state.validations.at(-1);
  if (lastValidation) {
    return {
      readout: lastValidation.accepted ? "accepted" : "rejected",
      health: lastValidation.accepted ? "good" : "bad",
    };
  }

  return { readout: noData, health: "idle" };
}

const clockViews: Record<ClockStatus, StageView> = {
  noSignal: { readout: "no signal", health: "idle" },
  locked: { readout: "locked", health: "good" },
  acquiring: { readout: "acquiring", health: "warn" },
  holding: { readout: "holding", health: "warn" },
};

export function clockStage(state: AppState): StageView {
  if (!state.clock) return { readout: noData, health: "idle" };
  return clockViews[state.clock.status];
}

const frameSyncView = (event: FrameSyncEvent): StageView => {
  switch (event.kind) {
    case "syncLost":
    case "searching":
      return { readout: "searching", health: "warn" };
    case "slotFilled":
      return { readout: `${event.second + 1} of 60`, health: "good" };
    case "frameComplete":
      return { readout: "60 of 60", health: "good" };
  }
};

export function frameSyncStage(state: AppState): StageView {
  if (!state.frameSync) return { readout: noData, health: "idle" };

  const view = frameSyncView(state.frameSync);
  return state.syncLoss ? { ...view, health: "bad" } : view;
}

export interface Stage {
  /** 1 to 7. Also the key that opens the stage's focus view in Step 12b. */
  number: number;
  name: string;
  view: (state: AppState) => StageView;
}

export const stages: readonly Stage[] = [
  { number: 1, name: "Edges", view: edgesStage },
  { number: 2, name: "Pulses", view: pulsesStage },
  { number: 3, name: "Symbols", view: symbolsStage },
  { number: 4, name: "Frame sync", view: frameSyncStage },
  { number: 5, name: "Decode", view: decodeStage },
  { number: 6, name: "Validate", view: validateStage },
  { number: 7, name: "Clock", view: clockStage },
];
