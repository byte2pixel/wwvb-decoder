import { cn } from "cn";
import { ArrowDown } from "lucide-react";
import type { SymbolKind } from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import { CLASS_ZONES, landingZone, lastPulse } from "./model";

/** A class as a symbol chip writes it, the signal panel's and the frame's letters again. */
const SYMBOL_TEXT: Record<SymbolKind, string> = {
  zero: "0",
  one: "1",
  marker: "M",
  error: "×",
};

/** The chip's colors, the frame cells' again. */
const CHIP_CLASSES: Record<SymbolKind, string> = {
  zero: "border-transparent bg-zero-surface text-zero",
  one: "border-transparent bg-signal text-on-signal",
  marker: "border-transparent bg-marker-surface text-marker",
  error: "border-bad text-bad",
};

/** A small heading over each of the three parts. */
function Label({ children }: Readonly<{ children: string }>) {
  return (
    <h3 className="text-xs font-semibold tracking-[0.06em] text-muted-foreground uppercase">
      {children}
    </h3>
  );
}

function Arrow() {
  return <ArrowDown className="size-5 flex-none self-center text-input" />;
}

/**
 * Stage 3 on its last pulse, as input, rule, and output: the pulse's duration, the classifier's
 * five zones with the one it landed in marked, and the symbol that came out.
 */
export function Classification() {
  const symbols = useAppStore((state) => state.symbols);
  const last = lastPulse(symbols);

  if (!last) {
    return (
      <p className="flex h-full items-center justify-center text-xs text-muted-foreground">
        No pulses yet
      </p>
    );
  }

  const landed = landingZone(last);

  return (
    <div className="flex h-full flex-col justify-between px-3 py-2.5">
      <div>
        <Label>Pulse</Label>
        <p className="font-mono text-4xl leading-tight font-semibold">
          {last.durationMs} ms
        </p>
      </div>
      <Arrow />
      <div>
        <Label>Rule</Label>
        <ol aria-label="Classes by duration" className="mt-1 font-mono text-lg">
          {CLASS_ZONES.map(({ fromMs, kind, rule }, zone) => (
            <li
              key={fromMs}
              aria-current={zone === landed ? "true" : undefined}
              className={cn(
                "flex justify-between rounded-sm px-2 leading-relaxed",
                zone === landed
                  ? "bg-accent font-semibold ring-1 ring-signal"
                  : "text-muted-foreground",
              )}
            >
              <span>{rule}</span>
              <span>{SYMBOL_TEXT[kind]}</span>
            </li>
          ))}
        </ol>
      </div>
      <Arrow />
      <div>
        <Label>Symbol</Label>
        <p className="mt-1 flex items-center gap-3">
          <span
            className={cn(
              "flex size-14 items-center justify-center rounded-sm border font-mono text-4xl leading-none font-semibold",
              CHIP_CLASSES[last.kind],
            )}
          >
            {SYMBOL_TEXT[last.kind]}
          </span>
          <span className="font-mono text-lg text-foreground-soft">
            {last.kind}
          </span>
        </p>
      </div>
    </div>
  );
}
