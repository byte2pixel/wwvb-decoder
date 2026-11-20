import { cn } from "cn";
import type { SymbolKind } from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import { failureLine, frameRows } from "./model";

const COLUMNS = [
  "Frame",
  "Symbols 0 to 59",
  "Min",
  "Hour",
  "Day",
  "Year",
  "UT1",
  "DST",
  "Result",
];

/** A tick's height and color, the pulse-width bars' colors again. */
const TICK_CLASSES: Record<SymbolKind, string> = {
  zero: "h-1/3 bg-signal/60",
  one: "h-full bg-signal",
  marker: "h-full bg-marker",
  error: "h-full bg-bad",
};

/** What a tick stands for, for the strip's tooltip. */
const SYMBOL_TEXT: Record<SymbolKind, string> = {
  zero: "0",
  one: "1",
  marker: "M",
  error: "×",
};

/** A frame's 60 symbols as a strip of ticks. */
function SymbolStrip({
  symbols,
}: Readonly<{ symbols: readonly SymbolKind[] }>) {
  return (
    <span
      title={symbols.map((kind) => SYMBOL_TEXT[kind]).join("")}
      className="flex h-4 items-end gap-px"
    >
      {symbols.map((kind, second) => (
        <span
          // A tick's place is its second, and the seconds of a frame never move.
          // oxlint-disable-next-line react/no-array-index-key
          key={second /* NOSONAR */}
          className={cn("min-w-0 flex-1 rounded-[1px]", TICK_CLASSES[kind])}
        />
      ))}
    </span>
  );
}

/** One row per validated frame, newest first, and no more than `maxRows` of them. */
export function FramesTable({ maxRows }: Readonly<{ maxRows: number }>) {
  const validations = useAppStore((state) => state.validations);
  const frameCount = useAppStore((state) => state.frameCount);
  const rows = frameRows(validations, frameCount).slice(0, maxRows);

  return (
    <div className="flex h-full flex-col px-3 pb-1 overflow-y-auto">
      <table className="w-full table-fixed border-collapse text-xs">
        <colgroup>
          <col className="w-[7%]" />
          <col className="w-[36%]" />
          <col className="w-[5%]" />
          <col className="w-[5%]" />
          <col className="w-[5%]" />
          <col className="w-[5%]" />
          <col className="w-[8%]" />
          <col className="w-[10%]" />
          <col />
        </colgroup>
        <thead className="sticky top-0 bg-card">
          <tr>
            {COLUMNS.map((column) => (
              <th
                key={column}
                scope="col"
                className="px-1.5 py-1 text-left text-[0.65625rem] font-normal text-muted-foreground"
              >
                {column}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="font-mono">
          {rows.map((row) => (
            <tr
              key={row.number}
              className={cn("border-t", !row.accepted && "bg-bad/10")}
            >
              <td className="px-1.5 py-1 text-muted-foreground">
                #{row.number}
              </td>
              <td className="px-1.5 py-1">
                <SymbolStrip symbols={row.symbols} />
              </td>
              <td className="px-1.5 py-1">{row.minute}</td>
              <td className="px-1.5 py-1">{row.hour}</td>
              <td className="px-1.5 py-1">{row.dayOfYear}</td>
              <td className="px-1.5 py-1">{row.year}</td>
              <td className="px-1.5 py-1">{row.ut1}</td>
              <td className="px-1.5 py-1">{row.dst}</td>
              <td
                title={
                  row.accepted
                    ? undefined
                    : row.failures.map(failureLine).join("\n")
                }
                className={cn(
                  "truncate px-1.5 py-1 font-sans",
                  !row.accepted && "text-bad",
                )}
              >
                {row.result}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {rows.length === 0 && (
        <p className="flex flex-1 items-center justify-center text-xs text-muted-foreground">
          No frames yet
        </p>
      )}
    </div>
  );
}
