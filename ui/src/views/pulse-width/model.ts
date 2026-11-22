import type { SymbolKind, Symbol as WwvbSymbol } from "@/protocol/messages";

/** The width of each histogram bin in milliseconds. */
export const BIN_MS = 25;
/**
 * The total range of the histogram in milliseconds.
 * A longer pulse will fall into the last bin.
 */
export const RANGE_MS = 1_000;

// The four boundaries repeat the defaults of the host's SymbolClassifier, because no message
// carries them.
export const MIN_VALID_MS = 100;
export const ZERO_ONE_THRESHOLD_MS = 350;
export const ONE_MARKER_THRESHOLD_MS = 650;
export const MAX_VALID_MS = 900;

/** A stretch of the axis that the host gives one class. */
export interface ClassZone {
  fromMs: number;
  toMs: number;
  kind: SymbolKind;
  /** Where the zone begins, in the words of the stage's rule. */
  rule: string;
}

/** The classifier's rule as five stretches of the axis. A boundary belongs to the class above it. */
export const CLASS_ZONES: readonly ClassZone[] = [
  {
    fromMs: 0,
    toMs: MIN_VALID_MS,
    kind: "error",
    rule: `under ${MIN_VALID_MS} ms`,
  },
  {
    fromMs: MIN_VALID_MS,
    toMs: ZERO_ONE_THRESHOLD_MS,
    kind: "zero",
    rule: `from ${MIN_VALID_MS} ms`,
  },
  {
    fromMs: ZERO_ONE_THRESHOLD_MS,
    toMs: ONE_MARKER_THRESHOLD_MS,
    kind: "one",
    rule: `from ${ZERO_ONE_THRESHOLD_MS} ms`,
  },
  {
    fromMs: ONE_MARKER_THRESHOLD_MS,
    toMs: MAX_VALID_MS,
    kind: "marker",
    rule: `from ${ONE_MARKER_THRESHOLD_MS} ms`,
  },
  {
    fromMs: MAX_VALID_MS,
    toMs: RANGE_MS,
    kind: "error",
    rule: `over ${MAX_VALID_MS} ms`,
  },
];

/** The number of bins in the histogram. */
const BIN_COUNT = RANGE_MS / BIN_MS;
/** The bin width in microseconds. */
const BIN_MICROS = BIN_MS * 1_000;

/** One 25 ms bar of the histogram. */
export interface HistogramBin {
  /** The bin's lower edge in milliseconds. */
  fromMs: number;
  count: number;
  /** The host's class of the newest pulse in the bin; null while it is empty. */
  kind: SymbolKind | null;
  /** The height of the bin relative to the fullest bin; 0 while it is empty. */
  height: number;
}

/** The newest pulse, for the dashed line and the header line. */
export interface LastPulse {
  /** When the pulse began. No two pulses share it, so it tells a new pulse from the last one. */
  startMicros: number;
  durationMicros: number;
  /** Rounded to the millisecond, as the rail shows it. */
  durationMs: number;
  kind: SymbolKind;
}

export interface PulseWidthHistogram {
  bins: HistogramBin[];
  /** The newest pulse, or null if there are no pulses yet. */
  last: LastPulse | null;
}

export function pulseWidthHistogram(
  symbols: ReadonlyArray<WwvbSymbol>,
): PulseWidthHistogram {
  const bins: HistogramBin[] = Array.from(
    { length: BIN_COUNT },
    (_, index) => ({
      fromMs: index * BIN_MS,
      count: 0,
      kind: null,
      height: 0,
    }),
  );

  for (const symbol of symbols) {
    const binIndex = Math.min(
      BIN_COUNT - 1,
      Math.floor(symbol.durationMicros / BIN_MICROS),
    );
    const bin = bins[binIndex];
    bin.count += 1;
    bin.kind = symbol.kind;
  }

  const maxCount = Math.max(...bins.map((bin) => bin.count), 1);
  for (const bin of bins) {
    bin.height = bin.count / maxCount;
  }

  return { bins, last: lastPulse(symbols) };
}

/** The newest pulse as the host classified it, or null before the first. */
export function lastPulse(
  symbols: ReadonlyArray<WwvbSymbol>,
): LastPulse | null {
  const symbol = symbols.at(-1);
  if (!symbol) return null;

  return {
    startMicros: symbol.startMicros,
    durationMicros: symbol.durationMicros,
    durationMs: Math.round(symbol.durationMicros / 1000),
    kind: symbol.kind,
  };
}

/**
 * Which of `CLASS_ZONES` the pulse landed in, as an index. The class is the host's: the page
 * does not classify a duration. Two zones are errors, and there the duration says which side
 * of the valid range the pulse fell on.
 */
export function landingZone({ kind, durationMicros }: LastPulse): number {
  if (kind !== "error") {
    return CLASS_ZONES.findIndex((zone) => zone.kind === kind);
  }
  return durationMicros < MIN_VALID_MS * 1_000 ? 0 : CLASS_ZONES.length - 1;
}

const KIND_TEXT: Record<SymbolKind, string> = {
  zero: "0",
  one: "1",
  marker: "M",
  error: "error",
};

export function lastPulseText(
  last: Pick<LastPulse, "durationMs" | "kind"> | null,
): string {
  if (!last) {
    return "No pulses yet";
  }
  const { durationMs, kind } = last;
  const kindText = KIND_TEXT[kind];
  return `Last pulse ${durationMs} ms classified as ${kindText}`;
}
