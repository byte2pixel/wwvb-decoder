import { useAppStore } from "@/state/store";
import {
  BIN_MS,
  CLASS_ZONES,
  landingZone,
  lastPulse,
  lastPulseText,
  MAX_VALID_MS,
  MIN_VALID_MS,
  ONE_MARKER_THRESHOLD_MS,
  pulseWidthHistogram,
  RANGE_MS,
  ZERO_ONE_THRESHOLD_MS,
} from "./model";
import { cn } from "cn";
import type { SymbolKind } from "@/protocol/messages";

/** A duration as a distance along the axis. */
const along = (ms: number): string => `${ms / (RANGE_MS / 100)}%`;

const BOUNDARIES = [
  { ms: MIN_VALID_MS, limit: true },
  { ms: ZERO_ONE_THRESHOLD_MS, limit: false },
  { ms: ONE_MARKER_THRESHOLD_MS, limit: false },
  { ms: MAX_VALID_MS, limit: true },
];

const ERROR_ZONES = [
  { title: `Error: under ${MIN_VALID_MS} ms`, fromMs: 0, toMs: MIN_VALID_MS },
  {
    title: `Error: over ${MAX_VALID_MS} ms`,
    fromMs: MAX_VALID_MS,
    toMs: RANGE_MS,
  },
];

const BAR_CLASSES: Record<SymbolKind, string> = {
  zero: "bg-signal/60",
  one: "bg-signal",
  marker: "bg-marker",
  error: "bg-bad",
};

/** A class as the zone above the plot names it. */
const CLASS_TEXT: Record<SymbolKind, string> = {
  zero: "0",
  one: "1",
  marker: "M",
  error: "error",
};

interface PulseWidthViewProps {
  /**
   * The stage 3 focus view's additions: each zone named above the plot with the one the last
   * pulse landed in marked, and the last pulse's line carrying its duration and dropping into
   * place when a new pulse arrives.
   */
  detailed?: boolean;
}

export function PulseWidthView({
  detailed = false,
}: Readonly<PulseWidthViewProps>) {
  const symbols = useAppStore((state) => state.symbols);
  const { bins, last } = pulseWidthHistogram(symbols);
  const landed = last ? landingZone(last) : null;

  return (
    <div
      className={cn("@container h-full px-3 pb-5", detailed ? "pt-8" : "pt-2")}
    >
      <div className="relative h-full border-b border-input">
        {detailed && (
          <ul aria-label="Classes">
            {CLASS_ZONES.map(({ fromMs, toMs, kind }, zone) => (
              <li
                key={fromMs}
                aria-current={zone === landed ? "true" : undefined}
                className={cn(
                  "absolute bottom-full mb-1 rounded-sm text-center font-mono text-base leading-tight",
                  zone === landed
                    ? "bg-accent font-semibold text-foreground"
                    : "text-muted-foreground",
                )}
                style={{ left: along(fromMs), width: along(toMs - fromMs) }}
              >
                {CLASS_TEXT[kind]}
              </li>
            ))}
          </ul>
        )}
        {ERROR_ZONES.map(({ fromMs, toMs, title }) => (
          <div
            key={title}
            title={title}
            className="absolute inset-y-0 bg-bad/10"
            style={{ left: along(fromMs), width: along(toMs - fromMs) }}
          />
        ))}
        <ul aria-label="Boundaries">
          {BOUNDARIES.map(({ ms, limit }) => (
            <li
              key={ms}
              style={{ left: along(ms) }}
              className={cn(
                "absolute inset-y-0 border-l border-input",
                limit ? "border-dashed" : "",
              )}
            >
              <span className="absolute top-full mt-1 -translate-x-1/2 font-mono text-[0.65625rem] leading-none text-muted-foreground @2xl:text-base">
                {ms}
              </span>
            </li>
          ))}
        </ul>
        <span className="absolute top-full right-0 mt-1 font-mono text-[0.65625rem] leading-none text-muted-foreground @2xl:text-base">
          ms
        </span>
        <ul aria-label="Bins">
          {bins.map(
            (bin) =>
              bin.kind && (
                <li
                  key={bin.fromMs}
                  title={`${bin.fromMs} to ${bin.fromMs + BIN_MS} ms: ${bin.count}`}
                  className={cn(
                    "absolute bottom-0 min-h-0.5",
                    BAR_CLASSES[bin.kind],
                  )}
                  style={{
                    left: along(bin.fromMs),
                    width: along(BIN_MS),
                    height: `${bin.height * 100}%`,
                  }}
                />
              ),
          )}
        </ul>
        {last && (
          <div
            // A new pulse is a new line, so the line drops in again for each pulse.
            key={detailed ? last.startMicros : "last"}
            title="Last pulse"
            className={cn(
              "absolute top-0 h-full border-l border-dashed border-foreground",
              detailed &&
                "animate-in duration-200 fade-in slide-in-from-top-4 motion-reduce:animate-none",
            )}
            style={{ left: along(Math.min(last.durationMs, RANGE_MS)) }}
          >
            {detailed && (
              <span
                className={cn(
                  "absolute top-1 rounded-sm bg-foreground px-1.5 font-mono text-sm leading-tight font-semibold whitespace-nowrap text-background",
                  // The label stays inside the plot: past the middle it sits left of its line.
                  last.durationMs > RANGE_MS / 2 ? "right-1" : "left-1",
                )}
              >
                {last.durationMs} ms
              </span>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

export function LastPulseLine() {
  const symbols = useAppStore((state) => state.symbols);
  return lastPulseText(lastPulse(symbols));
}
