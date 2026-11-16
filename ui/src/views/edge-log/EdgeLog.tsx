import { edgeLine } from "@/protocol/edgeLine";
import { useAppStore } from "@/state/store";

const MAX_LINES = 50;

/**
 * The newest edges as the serial protocol writes them, newest first.
 * Three columns, filled one after the other, hold the 50 lines.
 */
export function EdgeLog() {
  const edges = useAppStore((state) => state.edges);

  return edges.length === 0 ? (
    <p className="flex h-full items-center justify-center text-xs text-muted-foreground">
      No edges yet
    </p>
  ) : (
    <ol
      aria-label="Edges"
      className="h-full columns-3 gap-6 overflow-hidden px-3 py-2 font-mono text-base [column-fill:auto]"
    >
      {edges
        .slice(-MAX_LINES)
        .toReversed()
        .map((edge) => {
          const line = edgeLine(edge);
          return <li key={line}>{line}</li>;
        })}
    </ol>
  );
}
