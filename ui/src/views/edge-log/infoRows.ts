/** One informational line to show, with a key that tells it from an equal line. */
export interface InfoRow {
  key: string;
  line: string;
}

/**
 * The newest `max` informational lines, newest first. A source repeats a line such as
 * `# dropped 1`, so a row's key counts which of its equals it is.
 */
export function infoRows(lines: ReadonlyArray<string>, max: number): InfoRow[] {
  const seen = new Map<string, number>();
  return lines
    .map((line) => {
      const count = (seen.get(line) ?? 0) + 1;
      seen.set(line, count);
      return { key: `${count} ${line}`, line };
    })
    .slice(-max)
    .toReversed();
}
