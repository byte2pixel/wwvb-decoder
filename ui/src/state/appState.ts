import type {
  CaptureEntry,
  ClockState,
  DecodedFrame,
  Edge,
  FrameSyncEvent,
  HostMessage,
  Pulse,
  SourceStatus,
  ValidationResult,
  Symbol as WwvbSymbol,
} from "@/protocol/messages.ts";
import type { ProtocolLog } from "./log.ts";
import { appendMessage, emptyLog } from "./log.ts";

const maxEdges = 2_000;
const maxPulses = 300;
const maxSymbols = 600;
const maxValidations = 20;
const maxInfos = 50;

const appendCapped = <T>(
  array: ReadonlyArray<T>,
  items: T[],
  max: number,
): ReadonlyArray<T> => [...array, ...items].slice(-max);

export interface AppState {
  edges: ReadonlyArray<Edge>;
  pulses: ReadonlyArray<Pulse>;
  symbols: ReadonlyArray<WwvbSymbol>;
  validations: ReadonlyArray<ValidationResult>;
  infoLines: ReadonlyArray<string>;
  clock: Readonly<ClockState> | null;
  decodedFrame: Readonly<DecodedFrame> | null;
  frameSync: Readonly<FrameSyncEvent> | null;
  sourceStatus: Readonly<SourceStatus> | null;
  ports: ReadonlyArray<string>;
  captures: ReadonlyArray<CaptureEntry>;
  frameCount: number;
  lastSeq: number | null;
  lostMessages: number;
  log: ProtocolLog;
}

export const initialState: AppState = {
  edges: [],
  pulses: [],
  symbols: [],
  validations: [],
  infoLines: [],
  clock: null,
  decodedFrame: null,
  frameSync: null,
  sourceStatus: null,
  ports: [],
  captures: [],
  frameCount: 0,
  lastSeq: null,
  lostMessages: 0,
  log: emptyLog,
};

export const reduce = (state: AppState, message: HostMessage): AppState => {
  const gap =
    state.lastSeq === null ? 0 : Math.max(message.seq - state.lastSeq - 1, 0);

  return {
    ...applyMessage(state, message),
    lastSeq: message.seq,
    log: appendMessage(state.log, message),
    lostMessages: state.lostMessages + gap,
  };
};

function applyMessage(state: AppState, message: HostMessage): AppState {
  switch (message.type) {
    case "edges":
      return {
        ...state,
        edges: appendCapped(state.edges, message.payload.items, maxEdges),
      };
    case "pulse":
      return {
        ...state,
        pulses: appendCapped(state.pulses, [message.payload], maxPulses),
      };
    case "symbol":
      return {
        ...state,
        symbols: appendCapped(state.symbols, [message.payload], maxSymbols),
      };
    case "validation":
      return {
        ...state,
        validations: appendCapped(
          state.validations,
          [message.payload],
          maxValidations,
        ),
        frameCount: state.frameCount + 1,
      };
    case "info":
      return {
        ...state,
        infoLines: appendCapped(
          state.infoLines,
          [message.payload.line],
          maxInfos,
        ),
      };
    case "clock":
      return {
        ...state,
        clock: message.payload,
      };
    case "decodedFrame":
      return {
        ...state,
        decodedFrame: message.payload,
      };
    case "frameSync":
      return {
        ...state,
        frameSync: message.payload,
      };
    case "sourceStatus":
      if (message.payload.detail.startsWith("reset:")) {
        return {
          ...initialState,
          ports: state.ports,
          captures: state.captures,
          sourceStatus: message.payload,
        };
      }
      return {
        ...state,
        sourceStatus: message.payload,
      };
    case "ports":
      return {
        ...state,
        ports: message.payload.items,
      };
    case "captures":
      return {
        ...state,
        captures: message.payload.items,
      };
    default:
      message satisfies never;
      return state;
  }
}
