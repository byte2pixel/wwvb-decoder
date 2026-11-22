import { useAppStore } from "@/state/store";
import { decodedRecord } from "./model";

/** The record stage 5 hands to stage 6: the newest decoded frame, a row for each property. */
export function DecodedRecord() {
  const decodedFrame = useAppStore((state) => state.decodedFrame);
  const rows = decodedRecord(decodedFrame);

  return rows === null ? (
    <p className="flex h-full items-center justify-center text-xs text-muted-foreground">
      No frame yet
    </p>
  ) : (
    <dl className="flex h-full flex-col justify-between px-3 py-2 font-mono text-base">
      {rows.map(({ name, value }) => (
        // A row of its own for each property, so a long name does not narrow every value.
        <div key={name} className="flex justify-between gap-4">
          <dt className="text-muted-foreground">{name}</dt>
          <dd className="truncate">{value}</dd>
        </div>
      ))}
    </dl>
  );
}
