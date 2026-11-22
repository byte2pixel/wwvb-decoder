import type { AppState } from "@/state/appState";
import type {
  ClockStatus,
  FrameSyncEvent,
  Symbol as WwvbSymbol,
} from "@/protocol/messages";
import { edgeLine } from "@/protocol/edgeLine";

/** The dot's meaning. `idle` is the text-3 dot: nothing to judge yet. */
export type Health = "idle" | "good" | "warn" | "bad";

/** What the rail shows for one stage. */
export interface StageView {
  readout: string;
  health: Health;
}

export const noData = "—";

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
    readout: edgeLine(lastEdge),
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
  /** 1 to 7. Also the key that opens the stage's focus view. */
  number: number;
  name: string;
  /** The host's type for the stage, as C# writes it. */
  type: string;
  /** The stage's rule text, for its focus view. The numbers repeat the host's defaults. */
  rule: string;
  view: (state: AppState) => StageView;
}

export const stages: readonly Stage[] = [
  {
    number: 1,
    name: "Edges",
    type: "IEdgeSource",
    rule: "Every change of the receiver's output pin arrives as its new level and a time in microseconds.",
    view: edgesStage,
  },
  {
    number: 2,
    name: "Pulses",
    type: "IStage<Edge, Pulse>",
    rule: "A pulse is the time the carrier stays reduced. A pulse under 50 ms is dropped and a gap under 50 ms is bridged.",
    view: pulsesStage,
  },
  {
    number: 3,
    name: "Symbols",
    type: "IStage<Pulse, Symbol>",
    rule: "A pulse from 100 ms is a 0, from 350 ms a 1, from 650 ms a marker, and outside 100 to 900 ms an error.",
    view: symbolsStage,
  },
  {
    number: 4,
    name: "Frame sync",
    type: "IStage<Symbol, FrameSyncEvent>",
    rule: "Two markers a second apart start a frame. A marker or a bit in the wrong second, or a gap outside 0.5 to 1.5 s, loses sync.",
    view: frameSyncStage,
  },
  {
    number: 5,
    name: "Decode",
    type: "IStage<Frame, DecodedFrame>",
    rule: "Each field is the sum of the weights of its bits that are 1.",
    view: decodeStage,
  },
  {
    number: 6,
    name: "Validate",
    type: "IStage<DecodedFrame, ValidationResult>",
    rule: "A frame is accepted when it passes all ten checks. The last is that its time is the last accepted frame's plus the minutes passed since.",
    view: validateStage,
  },
  {
    number: 7,
    name: "Clock",
    type: "IStage<ValidationResult, ClockState>",
    rule: "Two accepted frames in a row lock the clock. Once locked it holds through two rejected frames, and the third sends it back to acquiring.",
    view: clockStage,
  },
];
