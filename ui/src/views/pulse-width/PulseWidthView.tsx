import { useAppStore } from "@/state/store";
import { BIN_MS, lastPulseText, pulseWidthHistogram, RANGE_MS } from "./model";
import { cn } from "cn";
import type { SymbolKind } from "@/protocol/messages";

// repeat the host's SymbolClassifier defaults.
const MIN_VALID_MS = 100;
const ZERO_ONE_THRESHOLD_MS = 350;
const ONE_MARKER_THRESHOLD_MS = 650;
const MAX_VALID_MS = 900;

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

export function PulseWidthView() {
  const symbols = useAppStore((state) => state.symbols);
  const { bins, last } = pulseWidthHistogram(symbols);

  return (
    <div className="@container h-full px-3 pt-2 pb-5">
      <div className="relative h-full border-b border-input">
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
            title="Last pulse"
            className="absolute top-0 h-full border-l border-dashed border-foreground"
            style={{ left: along(Math.min(last.durationMs, RANGE_MS)) }}
          />
        )}
      </div>
    </div>
  );
}

export function LastPulseLine() {
  const symbols = useAppStore((state) => state.symbols);
  const { last } = pulseWidthHistogram(symbols);
  return lastPulseText(last);
}
