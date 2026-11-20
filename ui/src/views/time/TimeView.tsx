import { useEffect, useRef, useState } from "react";
import { cn } from "cn";
import type { ClockStatus } from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import type { SourceClock } from "@/time/sourceClock";
import {
  clockFace,
  decodedTimeMs,
  localTimeRow,
  lockRows,
  offsetRow,
  statusChips,
} from "./model";

/** The current status in the rail's health colors. */
const CURRENT_CLASSES: Record<ClockStatus, string> = {
  noSignal: "border-foreground-soft text-foreground",
  acquiring: "border-warn text-warn",
  locked: "border-good text-good",
  holding: "border-warn text-warn",
};

/**
 * The source clock's present, as it was when the decoded time last reached a new second.
 * The clock is read on every animation frame, and the view renders once a second.
 */
function usePresent(sourceClock: SourceClock): number | null {
  const [present, setPresent] = useState<number | null>(null);
  const shown = useRef<number | null | undefined>(undefined);

  useEffect(() => {
    let frame = 0;
    const tick = (): void => {
      const now = sourceClock.now();
      const timeMs = decodedTimeMs(useAppStore.getState().clock, now);
      const second = timeMs === null ? null : Math.floor(timeMs / 1000);
      if (second !== shown.current) {
        shown.current = second;
        setPresent(now);
      }
      frame = requestAnimationFrame(tick);
    };
    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
  }, [sourceClock]);

  return present;
}

/** The clock the decoder has built: the time now, the four statuses, and four rows about it. */
export function TimeView({
  clock: sourceClock,
}: Readonly<{ clock: SourceClock }>) {
  const clock = useAppStore((state) => state.clock);
  const source = useAppStore((state) => state.sourceStatus?.source);
  const infoLines = useAppStore((state) => state.infoLines);
  const dst = useAppStore((state) => state.decodedFrame?.dst);
  const present = usePresent(sourceClock);

  const face = clockFace(clock, present);
  const rows = [
    ...lockRows(clock),
    offsetRow(clock, source, infoLines),
    localTimeRow(decodedTimeMs(clock, present), dst),
  ];

  return (
    <div className="@container-size h-full">
      <div className="flex h-full flex-col items-center justify-center gap-[0.14em] px-3 text-[min(19cqw,22cqh)]">
        <p
          className={cn(
            "font-mono leading-none font-semibold tabular-nums",
            face.date === "" && "text-muted-foreground",
          )}
        >
          {face.digits}
        </p>
        <p className="h-[1.3em] text-[0.2em] text-muted-foreground">
          {face.date === "" ? "" : `${face.date} · ${face.dayOfYear}`}
        </p>
        <ul
          aria-label="Clock status"
          className="flex gap-[0.5em] text-[0.15em]"
        >
          {statusChips(clock).map(({ status, label, current }) => (
            <li
              key={status}
              aria-current={current ? "true" : undefined}
              className={cn(
                "rounded-full border px-[0.7em] py-[0.15em]",
                current
                  ? CURRENT_CLASSES[status]
                  : "border-transparent text-muted-foreground",
              )}
            >
              {label}
            </li>
          ))}
        </ul>
        <dl className="grid grid-cols-[auto_auto] gap-x-[1.4em] gap-y-[0.25em] text-[0.17em]">
          {rows.map(({ label, value }, index) => (
            <div key={index === 1 ? "frames" : label} className="contents">
              <dt className="text-muted-foreground">{label}</dt>
              <dd className="font-mono">{value}</dd>
            </div>
          ))}
        </dl>
      </div>
    </div>
  );
}

/** The time panel's header line: the clock's status in words. */
export function ClockStatusLine() {
  const clock = useAppStore((state) => state.clock);
  return statusChips(clock).find((chip) => chip.current)?.label ?? "";
}
