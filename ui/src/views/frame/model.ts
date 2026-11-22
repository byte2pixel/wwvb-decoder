import { DST_TEXT, UT1_SIGNS, ut1Seconds } from "@/protocol/decodedText";
import type {
  DecodedFrame,
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
export type FieldRole = Exclude<SecondRole, "marker" | "unused">;

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

/**
 * The symbols of the last complete frame, indexed by second.
 * Every second is empty before the first frame.
 */
export function frameSymbols(
  frame: DecodedFrame | null,
): ReadonlyArray<SymbolKind | null> {
  return (
    frame?.frame.symbols ?? Array.from({ length: FRAME_SECONDS }, () => null)
  );
}

/** Each field's decoded value in words, by the role of its seconds. */
export type FieldValues = Readonly<Record<FieldRole, string>>;

const yesOrNo = (flag: boolean): string => (flag ? "yes" : "no");

/** What the host decoded for each field, or null before the first frame. */
export function fieldValues(frame: DecodedFrame | null): FieldValues | null {
  if (!frame) return null;

  return {
    minute: String(frame.minute),
    hour: String(frame.hour),
    dayOfYear: String(frame.dayOfYear),
    ut1Sign: UT1_SIGNS[frame.ut1Sign],
    ut1Correction: ut1Seconds(frame.ut1Tenths),
    year: String(frame.year),
    leapYear: yesOrNo(frame.leapYear),
    leapSecondPending: yesOrNo(frame.leapSecondPending),
    dst: DST_TEXT[frame.dst],
  };
}

/**
 * The weights of each field's bits that are 1, written as a sum: `20 + 10`.
 * A field whose seconds carry no weight, a flag or the UT1 sign, has no entry.
 */
export type FieldWeights = Readonly<Partial<Record<FieldRole, string>>>;

/** What a weighted field reads when none of its bits is 1. */
const NO_WEIGHTS = "none";

/**
 * Which weights the last complete frame set in each field, or null before the first frame or
 * the layout. Only a 1 counts: the host reads an error symbol as 0, so the weights listed add
 * up to the value it decoded. The page lists them and does not add them.
 */
export function setWeights(
  frame: DecodedFrame | null,
  layout: FrameLayout | null,
): FieldWeights | null {
  if (!frame || !layout) return null;

  const set = new Map<SecondRole, number[]>();
  layout.roles.forEach((role, second) => {
    const weight = layout.weights[second];
    if (weight === null) return;

    const weights = set.get(role) ?? [];
    if (frame.frame.symbols[second] === "one") weights.push(weight);
    set.set(role, weights);
  });

  return Object.fromEntries(
    [...set].map(([role, weights]) => [
      role,
      weights.length === 0 ? NO_WEIGHTS : weights.join(" + "),
    ]),
  );
}

/** One property of the host's `DecodedFrame` record. */
export interface RecordRow {
  /** The property as C# names it. */
  name: string;
  value: string;
}

/** A JSON enum value under its C# name: the host writes `EndsToday` as `endsToday`. */
const pascal = (word: string): string =>
  word.charAt(0).toUpperCase() + word.slice(1);

/**
 * The record stage 5 hands to stage 6, a row for each property in the order C# declares them,
 * or null before the first frame.
 */
export function decodedRecord(frame: DecodedFrame | null): RecordRow[] | null {
  if (!frame) return null;

  return [
    { name: "Frame", value: `${frame.frame.symbols.length} symbols` },
    { name: "Minute", value: String(frame.minute) },
    { name: "Hour", value: String(frame.hour) },
    { name: "DayOfYear", value: String(frame.dayOfYear) },
    { name: "Year", value: String(frame.year) },
    { name: "Ut1Sign", value: pascal(frame.ut1Sign) },
    { name: "Ut1Tenths", value: String(frame.ut1Tenths) },
    { name: "LeapYear", value: String(frame.leapYear) },
    { name: "LeapSecondPending", value: String(frame.leapSecondPending) },
    { name: "Dst", value: pascal(frame.dst) },
    {
      name: "ErrorSeconds",
      value:
        frame.errorSeconds.length === 0
          ? "none"
          : frame.errorSeconds.join(", "),
    },
  ];
}

/**
 * The last-frame panel's header line: the seconds that hold an error symbol.
 * It counts the frame's own symbols, so it names every cell drawn as an error. The host's
 * `errorSeconds` leaves out an unused second.
 */
export function decodedFrameLine(frame: DecodedFrame | null): string {
  if (!frame) return "No frame yet";

  const seconds = frame.frame.symbols.flatMap((symbol, second) =>
    symbol === "error" ? [second] : [],
  );
  if (seconds.length === 0) return "No error symbols";
  return seconds.length === 1
    ? `Error symbol in second ${seconds[0]}`
    : `Error symbols in seconds ${seconds.join(", ")}`;
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

/** What moves frame sync from one state to the other, for the arrows of its diagram. */
export const SYNC_MOVES = {
  sync: `${MARKERS_TO_SYNC} markers a second apart`,
  lose: "sync lost",
} as const;

/** Frame sync's two states as its diagram draws them. */
export interface SyncDiagram {
  /** The state frame sync is in, or null while no source has run. */
  state: "searching" | "synced" | null;
  /** The count under the state it is in: markers while searching, symbols while synced. */
  count: string;
  /** The label of the arrow back to searching. It names the host's reason while a loss is held. */
  lose: string;
  /** True while a loss is held, which is 5 s. */
  lost: boolean;
}

export function syncDiagram({
  frameSync,
  syncLoss,
  sourceStatus,
}: Pick<AppState, "frameSync" | "syncLoss" | "sourceStatus">): SyncDiagram {
  const lose = syncLoss
    ? `${SYNC_MOVES.lose}: ${syncLoss.reason}`
    : SYNC_MOVES.lose;
  const lost = syncLoss !== null;
  const searching = (markers: number): SyncDiagram => ({
    state: "searching",
    count: `${markers} of ${MARKERS_TO_SYNC} markers`,
    lose,
    lost,
  });
  const inSync = (placed: number): SyncDiagram => ({
    state: "synced",
    count: `${placed} of ${FRAME_SECONDS} symbols`,
    lose,
    lost,
  });

  switch (frameSync?.kind) {
    case "slotFilled":
      return inSync(frameSync.second + 1);
    case "frameComplete":
      return inSync(FRAME_SECONDS);
    case "searching":
      return searching(frameSync.consecutiveMarkers);
    case "syncLost":
      // The host's next event says how many markers the search starts with.
      return searching(0);
    default:
      // Frame sync starts out searching, so a running source is searching before it reports.
      return sourceStatus?.state === "running"
        ? searching(0)
        : { state: null, count: "", lose, lost };
  }
}
