export interface SourceClock {
  /** The estimated present in source microseconds, or null when there is none. */
  now(): number | null;
  /** Reports the newest edge's timestamp as it arrives. */
  sync(sourceMicros: number): void;
  /** 0 means unpaced and gives no present. */
  setSpeed(speed: number): void;
  /** Discards the anchor, so there is no present until the next edge. Speed is kept. */
  reset(): void;
}

/** Anchor point for the source clock's estimation. */
interface Anchor {
  /** The wall clock time in milliseconds when this anchor was created. */
  wallMs: number;
  /** The clock's reading at wallMs. On the first edge this is the edge's timestamp; after that it is a reading the clock has already given, which is why readings never go backwards. */
  micros: number;
  /** The correction still to apply, in microseconds: where the clock is heading minus its reading. Paid out over slewMs after wallMs; negative when the clock is ahead. */
  pending: number;
}

/** How long a correction is spread over. */
const slewMs = 250;
/** The share of the difference taken back when an edge is behind the target. */
const pullBack = 0.1;

/** Creates a source clock that estimates the present based on incoming edges and wall clock time. */
export function createSourceClock(wallMs: () => number): SourceClock {
  let anchor: Anchor | null = null;
  let speed = 1; // 1 for live and synthetic, replay speed for replay

  /**
   * Where the clock is at atMs, or null when there is no anchor:
   * - reading is what the clock says.
   * - target is where it is heading: the reading once all of pending is applied.
   */
  const positionAt = (atMs: number) => {
    if (!anchor) return null;
    const deltaMs = atMs - anchor.wallMs;
    const advanced = anchor.micros + deltaMs * 1_000 * speed;
    const progress = Math.min(1, deltaMs / slewMs);
    return {
      reading: Math.max(anchor.micros, advanced + anchor.pending * progress),
      target: advanced + anchor.pending,
    };
  };

  return {
    now(): number | null {
      return positionAt(wallMs())?.reading ?? null;
    },
    sync(sourceMicros: number): void {
      if (speed === 0) return;
      const atMs = wallMs();
      const position = positionAt(atMs);
      if (position !== null) {
        const { reading, target } = position;
        const error = sourceMicros - target;
        const newTarget = target + (error > 0 ? error : error * pullBack);
        anchor = {
          wallMs: atMs,
          micros: reading,
          pending: newTarget - reading,
        };
      } else {
        anchor = { wallMs: atMs, micros: sourceMicros, pending: 0 };
      }
    },
    setSpeed(newSpeed: number): void {
      const atMs = wallMs();
      const position = positionAt(atMs);
      anchor =
        newSpeed === 0 || position === null
          ? null
          : {
              wallMs: atMs,
              micros: position.reading,
              pending: position.target - position.reading,
            };
      speed = newSpeed;
    },
    reset(): void {
      anchor = null;
    },
  };
}
