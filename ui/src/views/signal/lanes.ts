import type { Edge, PinLevel } from "@/protocol/messages";

/**
 * Input for generating signal lanes.
 */
export interface SignalInput {
  edges: ReadonlyArray<Edge>;
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

export interface SignalLanes {
  /** The timespan the lanes cover. */
  window: { startMicros: number; endMicros: number } | null;
  carrier: CarrierSegment[];
}

export function signalLanes(input: SignalInput): SignalLanes {
  const { edges, nowMicros, windowMicros, reducedLevel } = input;
  const carrier: CarrierSegment[] = [];
  const windowEnd = nowMicros ?? edges.at(-1)?.micros;
  if (windowEnd === undefined) return { window: null, carrier };
  const windowStart = windowEnd - windowMicros;
  edges.forEach((edge, i) => {
    const endMicros = Math.min(windowEnd, edges[i + 1]?.micros ?? windowEnd);
    const startMicros = Math.max(windowStart, edge.micros);
    if (endMicros <= startMicros) return;
    carrier.push({
      startMicros,
      endMicros,
      reduced: edge.level === reducedLevel,
    });
  });
  return {
    window: { startMicros: windowStart, endMicros: windowEnd },
    carrier,
  };
}
