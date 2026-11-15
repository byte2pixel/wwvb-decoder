import type { SymbolKind, Symbol as WwvbSymbol } from "@/protocol/messages";

const BIN_COUNT = 40;
const BIN_MICROS = 25_000; // 25 ms in microseconds

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
      fromMs: index * (BIN_MICROS / 1000),
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

  const lastSymbol = symbols.at(-1);
  const last = lastSymbol
    ? {
        durationMs: Math.round(lastSymbol.durationMicros / 1000),
        kind: lastSymbol.kind,
      }
    : null;

  return { bins, last };
}

const KIND_TEXT: Record<SymbolKind, string> = {
  zero: "0",
  one: "1",
  marker: "M",
  error: "error",
};

export function lastPulseText(last: LastPulse | null): string {
  if (!last) {
    return "No pulses yet";
  }
  const { durationMs, kind } = last;
  const kindText = KIND_TEXT[kind];
  return `Last pulse ${durationMs} ms classified as ${kindText}`;
}
