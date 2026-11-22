import { useAppStore } from "@/state/store";
import { infoRows } from "./infoRows";

/** As many lines as the panel under the carrier lane holds. */
const MAX_LINES = 4;

/**
 * The source's informational lines, newest first: the logger's banner and its `# dropped`
 * reports, and a capture's header and `# wall` lines. The synthetic source sends none.
 */
export function InfoLines() {
  const infoLines = useAppStore((state) => state.infoLines);

  return infoLines.length === 0 ? (
    <p className="flex h-full items-center justify-center text-xs text-muted-foreground">
      No info lines yet
    </p>
  ) : (
    <ol
      aria-label="Info lines"
      className="h-full overflow-hidden px-3 py-2 font-mono text-base"
    >
      {infoRows(infoLines, MAX_LINES).map(({ key, line }) => (
        <li key={key} className="truncate">
          {line}
        </li>
      ))}
    </ol>
  );
}
