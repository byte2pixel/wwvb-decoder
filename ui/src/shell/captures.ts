import type { CaptureEntry } from "@/protocol/messages";

/** The time a capture was recorded, in UTC to the minute: `2026-11-09 04:34 UTC`. */
export function formatRecorded(recorded: string): string {
  const at = new Date(recorded);
  if (Number.isNaN(at.getTime())) return recorded;

  const iso = at.toISOString();
  return `${iso.slice(0, 10)} ${iso.slice(11, 16)} UTC`;
}

/** A capture's length to the nearest minute: `3 min`, `8 h 45 min`. */
export function formatLength(minutes: number): string {
  const total = Math.round(minutes);
  const hours = Math.floor(total / 60);
  return hours === 0
    ? `${total} min`
    : `${hours} h ${String(total % 60).padStart(2, "0")} min`;
}

/** What the capture list shows for one capture: when it was recorded and how long it runs. */
export const captureLabel = ({ recorded, minutes }: CaptureEntry): string =>
  `${formatRecorded(recorded)} · ${formatLength(minutes)}`;
