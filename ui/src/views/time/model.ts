import type {
  ClockState,
  ClockStatus,
  DstStatus,
  Source,
} from "@/protocol/messages";

/** What the time panel's clock face reads. */
export interface ClockFace {
  /** The time of day in UTC, `19:30:07`, or `--:--:--` before the first accepted frame. */
  digits: string;
  /** The UTC date, `2026-12-10`. Empty before the first accepted frame. */
  date: string;
  /** The day of the year, `day 344`. Empty before the first accepted frame. */
  dayOfYear: string;
}

const NO_TIME: ClockFace = { digits: "--:--:--", date: "", dayOfYear: "" };

const DAY_MS = 86_400_000;

/**
 * The UTC time the decoder is at, in Unix milliseconds: the last accepted frame's time plus
 * the source time between that frame's start and `atMicros`. With no `atMicros`, the time at
 * the frame's end. Null before the first accepted frame.
 */
export function decodedTimeMs(
  clock: ClockState | null,
  atMicros: number | null,
): number | null {
  if (!clock || clock.frameUtc === null || clock.frameStartMicros === null) {
    return null;
  }

  const micros = atMicros ?? clock.frameEndMicros ?? clock.frameStartMicros;
  return Date.parse(clock.frameUtc) + (micros - clock.frameStartMicros) / 1000;
}

/** The clock face at the present, cut to the second. */
export function clockFace(
  clock: ClockState | null,
  nowMicros: number | null,
): ClockFace {
  const timeMs = decodedTimeMs(clock, nowMicros);
  if (timeMs === null) return NO_TIME;

  const time = new Date(Math.floor(timeMs / 1000) * 1000);
  const iso = time.toISOString();
  const newYear = Date.UTC(time.getUTCFullYear(), 0, 0);

  return {
    digits: iso.slice(11, 19),
    date: iso.slice(0, 10),
    dayOfYear: `day ${Math.floor((time.getTime() - newYear) / DAY_MS)}`,
  };
}

/** One of the four statuses in the panel's row, and whether the clock is in it now. */
export interface StatusChip {
  status: ClockStatus;
  label: string;
  current: boolean;
}

/** The statuses in the order a clock passes through them. */
const STATUS_LABELS: readonly [ClockStatus, string][] = [
  ["noSignal", "No signal"],
  ["acquiring", "Acquiring"],
  ["locked", "Locked"],
  ["holding", "Holding"],
];

/** One labelled value under the clock. */
export interface TimeRow {
  label: string;
  value: string;
}

/** What a row reads while it has nothing to show. */
const NO_VALUE = "—";

/** The rejected frames in a row that send a locked clock back to acquiring. The host's default. */
const REJECTED_TO_ACQUIRE = 3;

/** The four statuses with the current one marked. Before any clock message there is no signal. */
export function statusChips(clock: ClockState | null): StatusChip[] {
  const now = clock?.status ?? "noSignal";
  return STATUS_LABELS.map(([status, label]) => ({
    status,
    label,
    current: status === now,
  }));
}

/** The last accepted frame's time of day, and how the frames since it have gone. */
export function lockRows(clock: ClockState | null): TimeRow[] {
  const frameUtc = clock?.frameUtc ?? null;
  const rejected = clock?.rejectedSinceLastAccepted ?? 0;
  return [
    {
      label: "Last accepted frame",
      value: frameUtc === null ? NO_VALUE : `${frameUtc.slice(11, 16)} UTC`,
    },
    rejected > 0
      ? {
          label: "Rejected since then",
          value: `${rejected} of ${REJECTED_TO_ACQUIRE}`,
        }
      : {
          label: "Accepted in a row",
          value: String(clock?.consecutiveAccepted ?? 0),
        },
  ];
}

const HOUR_MS = 3_600_000;

/** A capture's wall-clock line: the PC's UTC time when the source's time was the number after it. */
const WALL_LINE = /^# wall (\S+) (\d+)$/;

/** US Central time for each DST status. On a switch day the bits do not say whether 02:00 has passed. */
const CENTRAL: Record<
  DstStatus,
  { hours: number; zone: string; approximate: boolean }
> = {
  standard: { hours: -6, zone: "CST", approximate: false },
  dst: { hours: -5, zone: "CDT", approximate: false },
  beginsToday: { hours: -6, zone: "CST", approximate: true },
  endsToday: { hours: -5, zone: "CDT", approximate: true },
};

const signedMs = (ms: number): string =>
  `${ms < 0 ? "-" : "+"}${Math.abs(Math.round(ms))} ms`;

/**
 * WWVB's time minus the PC's. For a live or synthetic source the host measures it. Under replay
 * that would compare a recording with today, so the row uses the capture's newest wall-clock line.
 */
export function offsetRow(
  clock: ClockState | null,
  source: Source | undefined,
  infoLines: ReadonlyArray<string>,
): TimeRow {
  if (source !== "replay") {
    const offset = clock?.offsetFromPcMs ?? null;
    return {
      label: "Offset from the PC clock",
      value: offset === null ? NO_VALUE : signedMs(offset),
    };
  }

  const wall = infoLines
    .map((line) => WALL_LINE.exec(line))
    .findLast((match) => match !== null);
  const decoded = wall ? decodedTimeMs(clock, Number(wall[2])) : null;
  const recorded = wall ? Date.parse(wall[1]) : Number.NaN;
  return {
    label: "Recorded offset",
    value:
      decoded === null || Number.isNaN(recorded)
        ? NO_VALUE
        : signedMs(decoded - recorded),
  };
}

/** The time in US Central, from the frame's DST bits. */
export function localTimeRow(
  timeMs: number | null,
  dst: DstStatus | undefined,
): TimeRow {
  const label = "Local time, US Central";
  if (timeMs === null || dst === undefined) return { label, value: NO_VALUE };

  const { hours, zone, approximate } = CENTRAL[dst];
  const local = new Date(Math.floor(timeMs / 1000) * 1000 + hours * HOUR_MS)
    .toISOString()
    .slice(11, 19);
  return {
    label,
    value: `${local} ${zone}${approximate ? ", approximate" : ""}`,
  };
}

/** The accepted frames in a row that lock the clock. The host's default. */
const ACCEPTED_TO_LOCK = 2;

/** What moves the clock from one status to another, for the arrows of the state diagram. */
export const CLOCK_MOVES = {
  firstFrame: "first frame",
  lock: `${ACCEPTED_TO_LOCK} accepted in a row`,
  hold: "rejected",
  recover: "accepted",
  letGo: `${REJECTED_TO_ACQUIRE} rejected in a row`,
  reset: "A source reset returns the clock to No signal.",
} as const;

/** One of the two counts the host keeps, and the number at which it moves the clock. */
export interface ClockCounter {
  label: string;
  /** The count now. */
  value: string;
  /** What the count does when it reaches its limit. */
  limit: string;
}

/** The two counters that move the clock, as the host last sent them. Both are 0 before a frame. */
export function clockCounters(clock: ClockState | null): ClockCounter[] {
  return [
    {
      label: "Accepted in a row",
      value: String(clock?.consecutiveAccepted ?? 0),
      limit: `${ACCEPTED_TO_LOCK} lock the clock`,
    },
    {
      label: "Rejected in a row",
      value: String(clock?.rejectedSinceLastAccepted ?? 0),
      limit: `${REJECTED_TO_ACQUIRE} end the hold`,
    },
  ];
}
