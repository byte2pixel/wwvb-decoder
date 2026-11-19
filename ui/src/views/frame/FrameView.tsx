import { cn } from "cn";
import type { SymbolKind } from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import {
  cellState,
  fieldBrackets,
  frameCells,
  frameHeader,
  placedSymbols,
  receivingSecond,
  ROW_SECONDS,
  type CellState,
} from "./model";

/** A second's share of a row's width, in percent. */
const SECOND_WIDTH = 100 / ROW_SECONDS;

const CELL_CLASSES: Record<CellState, string> = {
  empty: "border-input",
  emptyMarker: "border-foreground-soft",
  emptyUnused: "border-dashed border-input",
  zero: "border-transparent bg-zero-surface text-zero",
  unusedZero: "border-dashed border-input text-muted-foreground",
  one: "border-transparent bg-signal text-on-signal",
  marker: "border-transparent bg-marker-surface text-marker",
  error: "border-bad text-bad",
};

/** What a cell shows for its symbol, as the signal panel's chips do. */
const SYMBOL_TEXT: Record<SymbolKind, string> = {
  zero: "0",
  one: "1",
  marker: "M",
  error: "×",
};

/** What a cell holds, in words, for its tooltip. */
const SYMBOL_NAMES: Record<SymbolKind, string> = {
  zero: "0",
  one: "1",
  marker: "marker",
  error: "error",
};

/** The frame in progress: three rows of twenty seconds. */
export function FrameView() {
  const layout = useAppStore((state) => state.frameLayout);
  const slots = useAppStore((state) => state.slots);
  const frameSync = useAppStore((state) => state.frameSync);

  const cells = frameCells(layout);
  const brackets = fieldBrackets(layout);
  const symbols = placedSymbols(slots);
  const receiving = receivingSecond(frameSync);
  const rowStarts = Array.from(
    { length: cells.length / ROW_SECONDS },
    (_, row) => row * ROW_SECONDS,
  );

  return (
    <div className="flex h-full flex-col gap-1.5 px-2.5 py-2 @container-size">
      {rowStarts.map((start) => {
        const end = start + ROW_SECONDS;
        const seconds = `${start} to ${end - 1}`;
        return (
          <div key={start} className="flex min-h-0 flex-1 flex-col">
            <ol
              aria-label={`Seconds ${seconds}`}
              className="flex min-h-0 flex-1"
            >
              {cells.slice(start, end).map((cell) => {
                const symbol = symbols[cell.second];
                return (
                  <li
                    key={cell.second}
                    className="flex min-w-0 flex-1 basis-0 flex-col px-0.5"
                  >
                    <span className="h-[clamp(0.875rem,7cqh,1.5rem)] flex-none text-center font-mono text-[clamp(0.65625rem,5cqh,1rem)] leading-none text-muted-foreground">
                      {cell.label}
                    </span>
                    <span
                      title={`Second ${cell.second}: ${symbol ? SYMBOL_NAMES[symbol] : "empty"}`}
                      aria-current={
                        cell.second === receiving ? "true" : undefined
                      }
                      className={cn(
                        "flex min-h-0 flex-1 items-center justify-center rounded-sm border font-mono text-[clamp(0.75rem,7cqh,1.5rem)] leading-none font-semibold",
                        CELL_CLASSES[cellState(cell.role, symbol)],
                        cell.second === receiving &&
                          "border-signal ring-1 ring-signal",
                      )}
                    >
                      {symbol ? SYMBOL_TEXT[symbol] : ""}
                    </span>
                  </li>
                );
              })}
            </ol>
            <ul
              aria-label={`Fields in seconds ${seconds}`}
              className="relative h-[clamp(1rem,8cqh,1.75rem)] flex-none"
            >
              {brackets
                .filter(
                  ({ firstSecond }) =>
                    firstSecond >= start && firstSecond < end,
                )
                .map(({ role, name, firstSecond, lastSecond }) => (
                  <li
                    key={role}
                    className="absolute inset-y-0 px-0.5"
                    style={{
                      left: `${(firstSecond - start) * SECOND_WIDTH}%`,
                      width: `${(lastSecond - firstSecond + 1) * SECOND_WIDTH}%`,
                    }}
                  >
                    <span className="mt-0.5 block truncate rounded-b-sm border-x border-b border-input text-center text-[clamp(0.65625rem,5cqh,1rem)] leading-tight text-muted-foreground">
                      {name}
                    </span>
                  </li>
                ))}
            </ul>
          </div>
        );
      })}
    </div>
  );
}

/** The frame panel's header line: what frame sync is doing, with a bar while it places symbols. */
export function FrameHeaderLine() {
  const frameSync = useAppStore((state) => state.frameSync);
  const syncLoss = useAppStore((state) => state.syncLoss);
  const sourceStatus = useAppStore((state) => state.sourceStatus);
  const { text, progress } = frameHeader({ frameSync, syncLoss, sourceStatus });

  return (
    <span
      className={cn("inline-flex items-center gap-2", syncLoss && "text-bad")}
    >
      {text}
      {progress !== null && (
        <progress
          aria-label="Frame placed"
          max={100}
          value={Math.round(progress * 100)}
          className="h-1 w-16 appearance-none overflow-hidden rounded-full [&::-webkit-progress-bar]:bg-secondary [&::-webkit-progress-value]:bg-signal"
        />
      )}
    </span>
  );
}
