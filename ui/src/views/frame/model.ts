import type {
  FrameLayout,
  FrameSyncEvent,
  SecondRole,
  SymbolKind,
} from "@/protocol/messages";
import type { AppState, FilledSlot } from "@/state/appState";

/** The seconds in a frame. */
const FRAME_SECONDS = 60;
/** The frame is drawn in rows of this many seconds. */
export const ROW_SECONDS = 20;

/** The position markers, named in the order their seconds come. */
const MARKER_NAMES = ["Pr", "P1", "P2", "P3", "P4", "P5", "P0"];

/** One second of the frame, as the layout describes it. */
export interface FrameCell {
  second: number;
  /** What the second carries, or null until the host has sent the layout. */
  role: SecondRole | null;
  /** Written above the cell: the bit's weight, the marker's name, or nothing. */
  label: string;
}

export function frameCells(layout: FrameLayout | null): FrameCell[] {
  let markers = 0;
  return Array.from({ length: FRAME_SECONDS }, (_, second) => {
    const role = layout?.roles[second] ?? null;
    const weight = layout?.weights[second] ?? null;
    if (role === "marker") {
      return { second, role, label: MARKER_NAMES[markers++] ?? "" };
    }
    return { second, role, label: weight === null ? "" : String(weight) };
  });
}

/** A role that is a field of the time code: every role but a marker and an unused second. */
type FieldRole = Exclude<SecondRole, "marker" | "unused">;

const FIELD_NAMES: Record<FieldRole, string> = {
  minute: "Minute",
  hour: "Hour",
  dayOfYear: "Day of year",
  ut1Sign: "UT1 sign",
  ut1Correction: "UT1",
  year: "Year",
  leapYear: "LY",
  leapSecondPending: "LS",
  dst: "DST",
};

/** The seconds a field covers, for the bracket drawn under its cells. */
export interface FieldBracket {
  role: FieldRole;
  name: string;
  firstSecond: number;
  /** The field's last second. A marker or an unused second between the two is inside the bracket. */
  lastSecond: number;
}

export function fieldBrackets(layout: FrameLayout | null): FieldBracket[] {
  const brackets: FieldBracket[] = [];
  layout?.roles.forEach((role, second) => {
    if (role === "marker" || role === "unused") return;

    const bracket = brackets.find((known) => known.role === role);
    if (bracket) {
      bracket.lastSecond = second;
    } else {
      brackets.push({
        role,
        name: FIELD_NAMES[role],
        firstSecond: second,
        lastSecond: second,
      });
    }
  });
  return brackets;
}

/**
 * The symbol frame sync placed in each second of the frame in progress, indexed by second.
 * Null where the second is still empty.
 */
export function placedSymbols(
  slots: ReadonlyArray<FilledSlot>,
): (SymbolKind | null)[] {
  const symbols: (SymbolKind | null)[] = Array.from(
    { length: FRAME_SECONDS },
    () => null,
  );
  const current = slots.at(-1);
  for (const slot of slots) {
    if (slot.frameStartMicros === current?.frameStartMicros) {
      symbols[slot.second] = slot.symbol;
    }
  }
  return symbols;
}

/** The second being received now, or null when frame sync is not placing symbols. */
export function receivingSecond(
  frameSync: FrameSyncEvent | null,
): number | null {
  if (frameSync?.kind !== "slotFilled") return null;

  const next = frameSync.second + 1;
  return next < FRAME_SECONDS ? next : null;
}

/** The markers frame sync needs in a row before it starts a frame. */
const MARKERS_TO_SYNC = 2;

/** The frame panel's header line. */
export interface FrameHeader {
  text: string;
  /** The share of the frame that is placed, 0 to 1, or null when there is no bar to draw. */
  progress: number | null;
}

const synced = (placed: number): FrameHeader => ({
  text: `Synced, ${placed} of ${FRAME_SECONDS} symbols`,
  progress: placed / FRAME_SECONDS,
});

export function frameHeader({
  frameSync,
  syncLoss,
  sourceStatus,
}: Pick<AppState, "frameSync" | "syncLoss" | "sourceStatus">): FrameHeader {
  if (syncLoss) {
    return { text: `Sync lost: ${syncLoss.reason}`, progress: null };
  }

  switch (frameSync?.kind) {
    case "slotFilled":
      return synced(frameSync.second + 1);
    case "frameComplete":
      return synced(FRAME_SECONDS);
    case "searching":
      return {
        text: `Searching, ${frameSync.consecutiveMarkers} of ${MARKERS_TO_SYNC} markers`,
        progress: null,
      };
    default:
      return {
        text: sourceStatus?.state === "running" ? "Searching" : "",
        progress: null,
      };
  }
}

export type CellState =
  | "empty"
  | "emptyMarker"
  | "emptyUnused"
  | "zero"
  | "unusedZero"
  | "one"
  | "marker"
  | "error";

export function cellState(
  role: SecondRole | null,
  symbol: SymbolKind | null,
): CellState {
  switch (symbol) {
    case null:
      if (role === "marker") return "emptyMarker";
      return role === "unused" ? "emptyUnused" : "empty";
    case "zero":
      return role === "unused" ? "unusedZero" : "zero";
    default:
      return symbol;
  }
}
