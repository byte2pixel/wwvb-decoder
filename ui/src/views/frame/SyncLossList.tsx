import { useAppStore } from "@/state/store";

/** The newest losses of frame sync, newest first, each with its number and the host's reason. */
export function SyncLossList() {
  const losses = useAppStore((state) => state.syncLosses);

  return losses.length === 0 ? (
    <p className="flex h-full items-center justify-center text-xs text-muted-foreground">
      No sync loss yet
    </p>
  ) : (
    <ol
      aria-label="Sync losses"
      className="h-full overflow-hidden px-3 py-2 font-mono text-base"
    >
      {losses.toReversed().map(({ number, reason }) => (
        <li key={number} className="flex gap-3">
          <span className="text-muted-foreground">#{number}</span>
          <span className="truncate">{reason}</span>
        </li>
      ))}
    </ol>
  );
}
