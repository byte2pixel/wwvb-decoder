import type {
  Edge,
  PinLevel,
  Pulse,
  Symbol as WwvbSymbol,
  SymbolKind,
} from "@/protocol/messages";
import type { FilledSlot } from "@/state/appState";

/**
 * Input for generating signal lanes.
 */
export interface SignalInput {
  edges: ReadonlyArray<Edge>;
  pulses: ReadonlyArray<Pulse>;
  symbols: ReadonlyArray<WwvbSymbol>;
  slots: ReadonlyArray<FilledSlot>;
  /** The present from the source clock, in source microseconds. */
  nowMicros: number | null;
  /** How much time the lanes show, ending at the present. */
  windowMicros: number;
  /** The pin level that means reduced carrier. */
  reducedLevel: PinLevel;
}

/**
 * A segment of the carrier signal.
 */
export interface CarrierSegment {
  startMicros: number;
  endMicros: number;
  reduced: boolean;
}

/**
 * A bar in the pulse lane.
 */
export interface PulseBar {
  startMicros: number;
  endMicros: number;
  /** The measured duration, or null while stage 2 has not measured the pulse. */
  durationMs: number | null;
}

/** The chip representing a symbol in the symbol lane. */
export interface SymbolChip {
  /** not clipped to the window */
  startMicros: number;
  kind: SymbolKind | "pending";
}

/** The second of the frame a symbol filled, shown in the second lane. */
export interface SecondLabel {
  /** The start of the label's pulse, not clipped to the window. */
  startMicros: number;
  /** Null when the symbol filled no second: frame sync was searching. */
  second: number | null;
}

export interface SignalLanes {
  /** The timespan the lanes cover. */
  window: { startMicros: number; endMicros: number } | null;
  carrier: CarrierSegment[];
  pulses: PulseBar[];
  symbols: SymbolChip[];
  seconds: SecondLabel[];
}

export function signalLanes(input: SignalInput): SignalLanes {
  const {
    edges,
    pulses,
    symbols,
    slots,
    nowMicros,
    windowMicros,
    reducedLevel,
  } = input;
  const carrier: CarrierSegment[] = [];
  const bars: PulseBar[] = [];
  const chips: SymbolChip[] = [];
  const labels: SecondLabel[] = [];
  const windowEnd = nowMicros ?? edges.at(-1)?.micros;
  if (windowEnd === undefined) {
    return {
      window: null,
      carrier,
      pulses: bars,
      symbols: chips,
      seconds: labels,
    };
  }
  const windowStart = windowEnd - windowMicros;

  /** The part of a span that is inside the window, or null when none of it is. */
  const clip = (start: number, end: number) => {
    const startMicros = Math.max(windowStart, start);
    const endMicros = Math.min(windowEnd, end);
    return endMicros > startMicros ? { startMicros, endMicros } : null;
  };

  const kinds = new Map(symbols.map((s) => [s.startMicros, s.kind]));
  const filled = new Map(slots.map((slot) => [slot.startMicros, slot.second]));
  const newestSymbol = symbols.at(-1)?.startMicros;
  pulses.forEach((pulse) => {
    const span = clip(pulse.startMicros, pulse.endMicros);
    if (!span) return;
    bars.push({
      ...span,
      durationMs: Math.round(pulse.durationMicros / 1000),
    });
    const kind = kinds.get(pulse.startMicros) ?? "pending";
    chips.push({ startMicros: pulse.startMicros, kind });

    if (kind === "pending") return;
    const second = filled.get(pulse.startMicros) ?? null;

    if (second === null && pulse.startMicros === newestSymbol) return;
    labels.push({ startMicros: pulse.startMicros, second });
  });

  // Stage 2 reports a pulse only once a later edge confirms it. Until then the
  // edges are all the page has, so reduced carrier after the last measured pulse
  // is drawn as a pending bar.
  const measuredUntil = pulses.at(-1)?.endMicros ?? -Infinity;
  edges.forEach((edge, i) => {
    const span = clip(edge.micros, edges[i + 1]?.micros ?? windowEnd);
    if (!span) return;
    const reduced = edge.level === reducedLevel;
    carrier.push({ ...span, reduced });
    if (reduced && edge.micros >= measuredUntil) {
      bars.push({ ...span, durationMs: null });
      chips.push({ startMicros: edge.micros, kind: "pending" });
    }
  });
  return {
    window: { startMicros: windowStart, endMicros: windowEnd },
    carrier,
    pulses: bars,
    symbols: chips,
    seconds: labels,
  };
}
