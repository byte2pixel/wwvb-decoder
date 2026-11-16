/** How much time the signal panel shows, and whether the pulse and second labels fit in it. */
export interface TimeWindow {
  windowMicros: number;
  labels: boolean;
}

export function timeWindowFor(speed: number): TimeWindow {
  if (speed <= 1) {
    return { windowMicros: 12_000_000, labels: true };
  } else {
    return { windowMicros: 60_000_000, labels: false };
  }
}
