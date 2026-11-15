import { useEffect, useRef } from "react";
import type { SourceClock } from "@/time/sourceClock";
import { useAppStore } from "@/state/store";
import { timeWindowFor } from "./timeWindow";
import { signalLanes } from "./lanes";
import { drawSignal, LANE_WEIGHTS, type LaneName } from "./model";
import { readSignalStyle } from "./style";
import { paintSignal } from "./paint";
import type { PinLevel } from "@/protocol/messages";

const LANES: readonly LaneName[] = ["carrier", "pulse", "symbol", "second"];
const LANE_NAMES: Record<LaneName, string> = {
  carrier: "Carrier",
  pulse: "Pulse",
  symbol: "Symbol",
  second: "Second",
};
/** The host's default. Step 13's signal settings will make it a choice. */
const REDUCED_LEVEL: PinLevel = "high";

/** The signal panel's canvas: four lanes over one time axis, painted on every animation frame. */
export function SignalView({ clock }: Readonly<{ clock: SourceClock }>) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const idle = useAppStore(
    (state) =>
      state.sourceStatus?.state !== "running" && state.edges.length === 0,
  );

  useEffect(() => {
    const canvas = canvasRef.current;
    const ctx = canvas?.getContext("2d");
    if (!canvas || !ctx) return;

    let width = 0;
    let height = 0;
    let scale = 1;
    let style = readSignalStyle(canvas);

    const sizes = new ResizeObserver((entries) => {
      const box = entries[0].contentRect;
      width = box.width;
      height = box.height;
      scale = window.devicePixelRatio;
      canvas.width = Math.round(width * scale);
      canvas.height = Math.round(height * scale);
      // rem changes with the resize, so read the style again.
      style = readSignalStyle(canvas);
    });
    sizes.observe(canvas);

    const theme = new MutationObserver(() => {
      style = readSignalStyle(canvas);
    });
    theme.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ["class"],
    });

    let frame = 0;
    const paint = (): void => {
      ctx.setTransform(scale, 0, 0, scale, 0, 0);
      ctx.clearRect(0, 0, width, height);
      const { edges, pulses, symbols, slots } = useAppStore.getState();
      const { windowMicros, labels } = timeWindowFor(clock.speed());
      const lanes = signalLanes({
        edges,
        pulses,
        symbols,
        slots,
        windowMicros,
        nowMicros: clock.now(),
        reducedLevel: REDUCED_LEVEL,
      });
      const drawing = drawSignal(lanes, {
        width,
        height,
        rem: style.rem,
        show: LANES,
        labels,
        tokens: style.tokens,
      });
      paintSignal(ctx, drawing, style.fontFamily);
      frame = requestAnimationFrame(paint);
    };
    frame = requestAnimationFrame(paint);

    return () => {
      cancelAnimationFrame(frame);
      sizes.disconnect();
      theme.disconnect();
    };
  }, [clock]);

  return (
    <div className="flex h-full">
      <ul
        aria-label="Lanes"
        className="flex flex-none flex-col border-r text-[0.65625rem] text-muted-foreground"
      >
        {LANES.map((lane) => (
          <li
            key={lane}
            style={{ flexGrow: LANE_WEIGHTS[lane] }}
            className="flex min-h-0 basis-0 items-center px-2 justify-end"
          >
            {LANE_NAMES[lane]}
          </li>
        ))}
      </ul>
      <div className="relative min-w-0 flex-1">
        <canvas
          ref={canvasRef}
          className="absolute inset-0 block h-full w-full font-mono"
        >
          The signal over the last seconds, drawn in four lanes.
        </canvas>
        {idle && (
          <p className="absolute inset-0 flex items-center justify-center text-xs text-muted-foreground">
            Pick a source and press Start
          </p>
        )}
      </div>
    </div>
  );
}
