import {
  validationCheckSchema,
  type ValidationCheck,
  type ValidationFailure,
} from "@/protocol/messages";
import { CHECK_NAMES, type FrameRow } from "@/views/frames-table/model";

/**
 * What each check asks of a frame. The lines repeat the host's rules, because no message
 * carries them.
 */
const CHECK_RULES: Record<ValidationCheck, string> = {
  markers: "A marker in each of the seven marker seconds, and nowhere else",
  unusedBits: "Every unused second holds a 0",
  errorSymbols: "No second of a field holds an error symbol",
  bcdDigits: "No digit of the minute, hour, day, or year is above 9",
  minuteRange: "The minute is 0 to 59",
  hourRange: "The hour is 0 to 23",
  dayOfYearRange: "The day is 1 to 365, or 366 in a leap year",
  ut1Sign: "The sign bits read 1 0 1 or 0 1 0",
  ut1Range: "The UT1 correction is 0.0 to 0.9 s",
  consecutive: "The time is the last accepted frame's plus the minutes since",
};

/** One of the ten checks, as it went for one frame. */
export interface CheckLine {
  check: ValidationCheck;
  /** The check in words, with a capital: `Unused bits`. */
  name: string;
  passed: boolean;
  /** The host's detail when the check failed, and what the check asks when it passed. */
  text: string;
}

const capital = (words: string): string =>
  words.charAt(0).toUpperCase() + words.slice(1);

/**
 * The ten checks in the host's order, each passed or failed for the frame with these failures.
 * A check passed when the host reports no failure for it. The message does not say when the
 * host skipped the last check, so a skipped comparison reads as passed too.
 */
export function checkLines(
  failures: readonly ValidationFailure[],
): CheckLine[] {
  return validationCheckSchema.options.map((check) => {
    const failure = failures.find((failed) => failed.check === check);
    return {
      check,
      name: capital(CHECK_NAMES[check]),
      passed: failure === undefined,
      text: failure?.detail ?? CHECK_RULES[check],
    };
  });
}

const pad2 = (value: number): string => String(value).padStart(2, "0");

/** The frame's own time as it was decoded, `19:35 day 344`, with no correction. */
export const frameTime = ({ hour, minute, dayOfYear }: FrameRow): string =>
  `${pad2(hour)}:${pad2(minute)} day ${dayOfYear}`;

/** The stage's output for the frame: accepted, or rejected with the count of failed checks. */
export function verdict({ accepted, failures }: FrameRow): string {
  if (accepted) return "Accepted";

  const checks = validationCheckSchema.options.length;
  return `Rejected, ${failures.length} of ${checks} checks failed`;
}

/**
 * The checks panel's header line: which frame the checks are for, and why that one.
 * `shown` is the frame listed, and `selected` the number the presenter picked.
 */
export function checksLine(
  shown: FrameRow | null,
  selected: number | null,
): string {
  if (!shown) return "";

  return shown.number === selected
    ? `Frame #${shown.number}, selected`
    : `Frame #${shown.number}, the newest`;
}
