import type { Edge, PinLevel, Pulse } from "@/protocol/messages";

/**
 * Input for generating signal lanes.
 */
export interface SignalInput {
  edges: ReadonlyArray<Edge>;
  pulses: ReadonlyArray<Pulse>;
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

export interface SignalLanes {
  /** The timespan the lanes cover. */
  window: { startMicros: number; endMicros: number } | null;
  carrier: CarrierSegment[];
  pulses: PulseBar[];
}

export function signalLanes(input: SignalInput): SignalLanes {
  const { edges, pulses, nowMicros, windowMicros, reducedLevel } = input;
  const carrier: CarrierSegment[] = [];
  const bars: PulseBar[] = [];
  const windowEnd = nowMicros ?? edges.at(-1)?.micros;
  if (windowEnd === undefined) return { window: null, carrier, pulses: bars };
  const windowStart = windowEnd - windowMicros;

  /** The part of a span that is inside the window, or null when none of it is. */
  const clip = (start: number, end: number) => {
    const startMicros = Math.max(windowStart, start);
    const endMicros = Math.min(windowEnd, end);
    return endMicros > startMicros ? { startMicros, endMicros } : null;
  };

  pulses.forEach((pulse) => {
    const span = clip(pulse.startMicros, pulse.endMicros);
    if (!span) return;
    bars.push({
      ...span,
      durationMs: Math.round(pulse.durationMicros / 1000),
    });
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
    }
  });
  return {
    window: { startMicros: windowStart, endMicros: windowEnd },
    carrier,
    pulses: bars,
  };
}
