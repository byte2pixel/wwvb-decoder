import type {
  Edge,
  PinLevel,
  Pulse,
  Symbol as WwvbSymbol,
  SymbolKind,
} from "@/protocol/messages";
import type { FilledSlot } from "@/state/appState";

const SECOND_MICROS = 1_000_000;

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
  /** The start is the pulse's own, not clipped. */
  startMicros: number;
  /** The end is clipped to the window's end. */
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

/**
 * A stretch of the carrier that stage 2's 50 ms rule took care of, seen from what it reported.
 * The page compares the edges with the measured pulses and applies no threshold of its own.
 */
export interface Glitch {
  /** The start is the stretch's own, not clipped. */
  startMicros: number;
  /** The end is clipped to the window's end. */
  endMicros: number;
  /**
   * `dropped`: reduced carrier that no measured pulse covers, so stage 2 dropped it.
   * `bridged`: full carrier inside a measured pulse, so stage 2 bridged it.
   */
  kind: "dropped" | "bridged";
}

export interface SignalLanes {
  /** The timespan the lanes cover. */
  window: { startMicros: number; endMicros: number } | null;
  carrier: CarrierSegment[];
  pulses: PulseBar[];
  /** What stage 2 dropped or bridged, oldest first. */
  glitches: Glitch[];
  symbols: SymbolChip[];
  seconds: SecondLabel[];
  /** The vertical lines marking each second. */
  ticks: number[];
  /** The present, where the cursor is drawn. Null in a still picture. */
  cursorMicros: number | null;
}

/**
 * What stage 2 did with one stretch of the carrier that has both its edges, or null when the
 * stretch is a pulse or the ordinary gap between two pulses.
 */
function glitchKind(
  reduced: boolean,
  startMicros: number,
  endMicros: number,
  pulses: ReadonlyArray<Pulse>,
): Glitch["kind"] | null {
  if (reduced) {
    const measured = pulses.some(
      (pulse) =>
        pulse.startMicros <= startMicros && endMicros <= pulse.endMicros,
    );
    return measured ? null : "dropped";
  }

  const insidePulse = pulses.some(
    (pulse) => pulse.startMicros < startMicros && endMicros < pulse.endMicros,
  );
  return insidePulse ? "bridged" : null;
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
  const glitches: Glitch[] = [];
  const chips: SymbolChip[] = [];
  const labels: SecondLabel[] = [];
  const ticks: number[] = [];
  const windowEnd = nowMicros ?? edges.at(-1)?.micros;
  const phase = slots.at(-1)?.frameStartMicros ?? pulses.at(-1)?.startMicros;
  if (windowEnd === undefined) {
    return {
      window: null,
      carrier,
      pulses: bars,
      glitches,
      symbols: chips,
      seconds: labels,
      ticks,
      cursorMicros: null,
    };
  }
  const windowStart = windowEnd - windowMicros;
  if (phase !== undefined) {
    const first =
      phase + Math.ceil((windowStart - phase) / SECOND_MICROS) * SECOND_MICROS;
    for (let t = first; t <= windowEnd; t += SECOND_MICROS) {
      ticks.push(t);
    }
  }

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
      startMicros: pulse.startMicros,
      endMicros: span.endMicros,
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
  // Before the oldest pulse the page holds, it cannot tell a dropped glitch from a pulse it
  // no longer has.
  const measuredFrom = pulses.at(0)?.startMicros ?? Infinity;
  edges.forEach((edge, i) => {
    const next = edges[i + 1]?.micros;
    const span = clip(edge.micros, next ?? windowEnd);
    if (!span) return;
    const reduced = edge.level === reducedLevel;
    carrier.push({ ...span, reduced });
    if (reduced && edge.micros >= measuredUntil) {
      bars.push({
        startMicros: edge.micros,
        endMicros: span.endMicros,
        durationMs: null,
      });
      chips.push({ startMicros: edge.micros, kind: "pending" });
      return;
    }
    // Only a stretch with both its edges, older than the newest pulse, has been decided.
    if (next === undefined || edge.micros < measuredFrom) return;

    const kind = glitchKind(reduced, edge.micros, next, pulses);
    if (kind) {
      glitches.push({
        startMicros: edge.micros,
        endMicros: span.endMicros,
        kind,
      });
    }
  });
  return {
    window: { startMicros: windowStart, endMicros: windowEnd },
    carrier,
    pulses: bars,
    glitches,
    symbols: chips,
    seconds: labels,
    ticks,
    cursorMicros: nowMicros,
  };
}
