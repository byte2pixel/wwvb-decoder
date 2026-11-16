import { edgeLine } from "@/protocol/edgeLine";
import { useAppStore } from "@/state/store";

const MAX_LINES = 50;

/** The newest edges as the serial protocol writes them, newest first. */
export function EdgeLog() {
  const edges = useAppStore((state) => state.edges);

  return edges.length === 0 ? (
    <p className="flex h-full items-center justify-center text-xs text-muted-foreground">
      No edges yet
    </p>
  ) : (
    <ol aria-label="Edges" className="h-full overflow-hidden font-mono text-sm">
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
