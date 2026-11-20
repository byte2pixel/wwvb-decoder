import type {
  DstStatus,
  SymbolKind,
  Ut1Sign,
  ValidationCheck,
  ValidationFailure,
  ValidationResult,
} from "@/protocol/messages";

/** One row of the decoded-frames table: a frame as the host decoded and judged it. */
export interface FrameRow {
  /** The frame's count since the last source reset. The newest frame has the highest. */
  number: number;
  /** The frame's 60 symbols, indexed by second, for the strip of ticks. */
  symbols: readonly SymbolKind[];
  minute: number;
  hour: number;
  dayOfYear: number;
  year: number;
  /** The UT1 correction as a sign and seconds: `+0.3 s`. */
  ut1: string;
  dst: string;
  accepted: boolean;
  /** `Accepted`, or `Rejected: ` and the first failure's check. */
  result: string;
  /** Every failure, with its detail. Empty for an accepted frame. */
  failures: readonly ValidationFailure[];
}

const UT1_SIGNS: Record<Ut1Sign, string> = {
  plus: "+",
  minus: "-",
  invalid: "?",
};

const DST_TEXT: Record<DstStatus, string> = {
  standard: "standard",
  dst: "dst",
  beginsToday: "begins today",
  endsToday: "ends today",
};

const CHECK_NAMES: Record<ValidationCheck, string> = {
  markers: "markers",
  unusedBits: "unused bits",
  errorSymbols: "error symbols",
  bcdDigits: "BCD digits",
  minuteRange: "minute range",
  hourRange: "hour range",
  dayOfYearRange: "day of year range",
  ut1Sign: "UT1 sign",
  ut1Range: "UT1 range",
  consecutive: "consecutive",
};

/** One failure as a line of text: the check in words, then the host's detail. */
export const failureLine = ({ check, detail }: ValidationFailure): string =>
  `${CHECK_NAMES[check]}: ${detail}`;

const resultText = (
  accepted: boolean,
  failures: readonly ValidationFailure[],
): string => {
  if (accepted) return "Accepted";

  const first = failures.at(0);
  return first ? `Rejected: ${CHECK_NAMES[first.check]}` : "Rejected";
};

/** The table's rows, newest first, from the validation results the store holds. */
export function frameRows(
  validations: ReadonlyArray<ValidationResult>,
  frameCount: number,
): FrameRow[] {
  const oldest = frameCount - validations.length + 1;
  return validations
    .map(({ frame, accepted, failures }, index): FrameRow => ({
      number: oldest + index,
      symbols: frame.frame.symbols,
      minute: frame.minute,
      hour: frame.hour,
      dayOfYear: frame.dayOfYear,
      year: frame.year,
      ut1: `${UT1_SIGNS[frame.ut1Sign]}${(frame.ut1Tenths / 10).toFixed(1)} s`,
      dst: DST_TEXT[frame.dst],
      accepted,
      result: resultText(accepted, failures),
      failures,
    }))
    .reverse();
}
