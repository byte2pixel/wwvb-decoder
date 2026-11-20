import type { DstStatus, Ut1Sign } from "./messages";

// How the page writes the values of a decoded frame that are not plain numbers.
// The decoded-frames table and the last complete frame both read them, so a value
// the protocol gains later does not compile until it has its text.

export const UT1_SIGNS: Record<Ut1Sign, string> = {
  plus: "+",
  minus: "-",
  invalid: "?",
};

export const DST_TEXT: Record<DstStatus, string> = {
  standard: "standard",
  dst: "dst",
  beginsToday: "begins today",
  endsToday: "ends today",
};

/** The size of a UT1 correction in seconds, from its tenths: `0.3 s`. */
export const ut1Seconds = (tenths: number): string =>
  `${(tenths / 10).toFixed(1)} s`;
