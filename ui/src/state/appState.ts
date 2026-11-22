import type {
  CaptureEntry,
  ClockState,
  DecodedFrame,
  Edge,
  FrameLayout,
  FrameSyncEvent,
  HostMessage,
  Pulse,
  SourceStatus,
  SymbolKind,
  ValidationResult,
  Symbol as WwvbSymbol,
} from "@/protocol/messages";
import type { ProtocolLog } from "./log";
import { appendMessage, emptyLog } from "./log";

const maxEdges = 2_000;
const maxPulses = 300;
const maxSymbols = 600;
const maxValidations = 20;
const maxInfos = 50;
const maxSlots = 120;
const maxSyncLosses = 5;

const appendCapped = <T>(
  array: ReadonlyArray<T>,
  items: T[],
  max: number,
): ReadonlyArray<T> => [...array, ...items].slice(-max);

/** A second of the frame that frame sync filled. */
export interface FilledSlot {
  /** The start of the symbol placed: the one that arrived just before the event. */
  startMicros: number;
  frameStartMicros: number;
  second: number;
  symbol: SymbolKind;
}

/** A loss of frame sync, as the stage 4 focus view lists it. */
export interface SyncLossEntry {
  /** The loss's count since the last source reset. The newest has the highest. */
  number: number;
  /** The host's reason, as it sent it. */
  reason: string;
}

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
  slots: ReadonlyArray<FilledSlot>;
  /** Comes from the hello reply. A reset does not clear it. */
  frameLayout: Readonly<FrameLayout> | null;
  /** The latest loss of frame sync. The store clears it 5 s later. */
  syncLoss: Readonly<{ reason: string; seq: number }> | null;
  /** The newest losses of frame sync, oldest first. Nothing but a source reset clears them. */
  syncLosses: ReadonlyArray<SyncLossEntry>;
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
  slots: [],
  frameLayout: null,
  syncLoss: null,
  syncLosses: [],
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
    case "frameLayout":
      return {
        ...state,
        frameLayout: message.payload,
      };
    case "frameSync": {
      const placed = state.symbols.at(-1);
      const slots =
        message.payload.kind === "slotFilled" && placed
          ? appendCapped(
              state.slots,
              [
                {
                  startMicros: placed.startMicros,
                  frameStartMicros: message.payload.frameStartMicros,
                  second: message.payload.second,
                  symbol: message.payload.symbol,
                },
              ],
              maxSlots,
            )
          : state.slots;
      if (message.payload.kind !== "syncLost") {
        return { ...state, frameSync: message.payload, slots };
      }

      const { reason } = message.payload;
      const number = (state.syncLosses.at(-1)?.number ?? 0) + 1;
      return {
        ...state,
        frameSync: message.payload,
        syncLoss: { reason, seq: message.seq },
        syncLosses: appendCapped(
          state.syncLosses,
          [{ number, reason }],
          maxSyncLosses,
        ),
        slots,
      };
    }
    case "sourceStatus":
      if (message.payload.detail.startsWith("reset:")) {
        return {
          ...initialState,
          ports: state.ports,
          captures: state.captures,
          frameLayout: state.frameLayout,
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
    case "selection":
      // The store takes it into the page's own state, which a source reset must not clear.
      return state;
    default:
      message satisfies never;
      return state;
  }
}

export const expireSyncLoss = (state: AppState, seq: number): AppState =>
  state.syncLoss?.seq === seq
    ? {
        ...state,
        syncLoss: null,
      }
    : state;
