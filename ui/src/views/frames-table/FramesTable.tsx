import { cn } from "cn";
import { useState } from "react";
import type { SymbolKind } from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import { failureLine, frameRows, shownFrame } from "./model";

/** Each column's heading and its width; the result takes what is left. */
const COLUMNS = [
  { name: "Frame", width: "w-[7%]" },
  { name: "Symbols 0 to 59", width: "w-[36%]" },
  { name: "Min", width: "w-[5%]" },
  { name: "Hour", width: "w-[5%]" },
  { name: "Day", width: "w-[5%]" },
  { name: "Year", width: "w-[5%]" },
  { name: "UT1", width: "w-[8%]" },
  { name: "DST", width: "w-[10%]" },
  { name: "Result", width: "" },
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

interface FramesTableProps {
  /** How many rows to show at most. */
  maxRows: number;
  /**
   * Lets a row be picked, as the stage 6 focus view does. The frame number becomes a button
   * that covers its row, and the row of the frame the view is about is outlined.
   */
  selectable?: boolean;
}

/** One row per validated frame, newest first, and no more than `maxRows` of them. */
export function FramesTable({
  maxRows,
  selectable = false,
}: Readonly<FramesTableProps>) {
  const validations = useAppStore((state) => state.validations);
  const frameCount = useAppStore((state) => state.frameCount);
  const selected = useAppStore((state) => state.selectedFrame);
  const selectFrame = useAppStore((state) => state.selectFrame);
  const rows = frameRows(validations, frameCount).slice(0, maxRows);
  const shown = selectable ? shownFrame(rows, selected)?.number : undefined;

  // The frames that were there when the table appeared are drawn in place. Only a frame that
  // arrives after that slides in. A source reset numbers the frames from 1 again.
  const [counted, setCounted] = useState(frameCount);
  if (frameCount < counted) setCounted(frameCount);

  return (
    <div className="flex h-full flex-col px-3 pb-1 overflow-y-auto">
      <table className="w-full table-fixed border-collapse text-xs">
        <colgroup>
          {COLUMNS.map(({ name, width }) => (
            <col key={name} className={width} />
          ))}
        </colgroup>
        <thead className="sticky top-0 z-10 bg-card">
          <tr>
            {COLUMNS.map(({ name }) => (
              <th
                key={name}
                scope="col"
                className="px-1.5 py-1 text-left text-[0.65625rem] font-normal text-muted-foreground"
              >
                {name}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="align-top font-mono">
          {rows.map((row) => (
            <tr
              key={row.number}
              className={cn(
                "border-t",
                !row.accepted && "bg-bad/10",
                selectable && "relative hover:bg-accent",
                row.number > counted &&
                  "animate-in duration-200 fade-in slide-in-from-top-2 motion-reduce:animate-none",
                row.number === shown &&
                  "outline-1 -outline-offset-1 outline-signal",
              )}
            >
              <td className="px-1.5 py-1 text-muted-foreground">
                {selectable ? (
                  <button
                    type="button"
                    aria-pressed={row.number === selected}
                    // A second click lets go of the frame, and the view follows the newest again.
                    onClick={() =>
                      selectFrame(row.number === selected ? null : row.number)
                    }
                    className="cursor-pointer after:absolute after:inset-0 aria-pressed:font-semibold aria-pressed:text-signal"
                  >
                    #{row.number}
                  </button>
                ) : (
                  `#${row.number}`
                )}
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
