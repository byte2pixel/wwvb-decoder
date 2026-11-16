import type { SignalTokens } from "./model";

/** What the drawing takes from the page's CSS. */
export interface SignalStyle {
  tokens: SignalTokens;
  /** The root font size in pixels. */
  rem: number;
  /** The element's own font family. */
  fontFamily: string;
}

export function readSignalStyle(element: Element): SignalStyle {
  const style = getComputedStyle(element);

  const read = (variable: string): string =>
    style.getPropertyValue(variable).trim();

  const rem = Number.parseFloat(
    getComputedStyle(document.documentElement).fontSize,
  );

  const fontFamily = style.fontFamily;

  const tokens: SignalTokens = {
    line: read("--border"),
    lineStrong: read("--input"),
    signal: read("--signal"),
    onSignal: read("--on-signal"),
    zero: read("--zero"),
    zeroSurface: read("--zero-surface"),
    marker: read("--marker"),
    markerSurface: read("--marker-surface"),
    bad: read("--bad"),
    textSoft: read("--foreground-soft"),
    textMuted: read("--muted-foreground"),
  };

  return { tokens, rem, fontFamily };
}
