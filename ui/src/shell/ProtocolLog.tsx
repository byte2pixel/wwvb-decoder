import { useAppStore } from "@/state/store";

export function ProtocolLog() {
  const log = useAppStore((state) => state.log);
  return (
    <div className="flex min-h-0 flex-1 flex-col gap-3 px-4 pb-4 font-mono text-xs">
      <ul
        aria-label="Message counts"
        className="flex flex-wrap gap-x-4 gap-y-1"
      >
        {Object.entries(log.counts).map(([type, count]) => (
          <li key={type}>
            {type} {count}
          </li>
        ))}
      </ul>
      <ol aria-label="Messages" className="min-h-0 flex-1 overflow-auto">
        {log.entries.toReversed().map(({ type, payload, seq }) => (
          <li key={seq} className="flex gap-3 whitespace-nowrap">
            <span className="w-12 text-right text-muted-foreground">{seq}</span>
            <span className="w-24">{type}</span>
            <span className="text-foreground-soft">
              {JSON.stringify(payload)}
            </span>
          </li>
        ))}
      </ol>
    </div>
  );
}
