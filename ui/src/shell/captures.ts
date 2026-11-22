import type { CaptureEntry } from "@/protocol/messages";

const MONTHS = [
  "Jan",
  "Feb",
  "Mar",
  "Apr",
  "May",
  "Jun",
  "Jul",
  "Aug",
  "Sep",
  "Oct",
  "Nov",
  "Dec",
];

/** A capture's file name: `<date>T<time>Z-<label>.edges.txt`, as capture-format.md names it. */
const CAPTURE_FILE = /^\d{4}-\d{2}-\d{2}T\d{4}Z-(.+)$/;

/**
 * The time a capture was recorded, in UTC to the minute, with the month as a word so two
 * dates are told apart at a glance: `9 Nov 2026, 04:34 UTC`.
 */
export function formatRecorded(recorded: string): string {
  const at = new Date(recorded);
  if (Number.isNaN(at.getTime())) return recorded;

  const date = `${at.getUTCDate()} ${MONTHS[at.getUTCMonth()]} ${at.getUTCFullYear()}`;
  return `${date}, ${at.toISOString().slice(11, 16)} UTC`;
}

/** A capture's length to the nearest minute: `3 min`, `8 h 45 min`. */
export function formatLength(minutes: number): string {
  const total = Math.round(minutes);
  const hours = Math.floor(total / 60);
  return hours === 0
    ? `${total} min`
    : `${hours} h ${String(total % 60).padStart(2, "0")} min`;
}

/**
 * The label a capture was recorded under: the part of its file name after the timestamp,
 * `overnight` for `2026-11-09T0434Z-overnight.edges.txt`. A file named another way gives its
 * name without the extensions.
 */
export function captureName(path: string): string {
  const file = path.split(/[\\/]/).at(-1) ?? "";
  const name = file.split(".")[0];
  return CAPTURE_FILE.exec(name)?.[1] ?? name;
}

/** What the capture list shows for one capture: its label, when it was recorded, and how long it runs. */
export const captureLabel = ({
  path,
  recorded,
  minutes,
}: CaptureEntry): string =>
  [captureName(path), formatRecorded(recorded), formatLength(minutes)]
    .filter((part) => part !== "")
    .join(" · ");
