import { useId } from "react";
import type { ClockStatus } from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import {
  ArrowHead,
  StateNode,
  Transition,
  type Tone,
} from "@/views/state-diagram/parts";
import { CLOCK_MOVES, clockCounters, statusChips } from "./model";

/** The current status in the rail's health colors, as the time panel's chips have them. */
const STATUS_TONES: Record<ClockStatus, Tone> = {
  noSignal: "idle",
  acquiring: "warn",
  locked: "good",
  holding: "warn",
};

const NODE = { width: 140, height: 60 };

/** Where each status sits, in the units of the viewBox. */
const PLACES: Record<ClockStatus, { x: number; y: number }> = {
  noSignal: { x: 20, y: 30 },
  acquiring: { x: 320, y: 30 },
  locked: { x: 640, y: 30 },
  holding: { x: 640, y: 290 },
};

/**
 * The clock's four statuses as a state diagram: an arrow for each move with what causes it,
 * the status the clock is in now, and the two counters that move it.
 */
export function ClockDiagram() {
  const clock = useAppStore((state) => state.clock);
  const head = useId();
  const titleId = useId();

  return (
    <svg
      viewBox="0 0 840 430"
      aria-labelledby={titleId}
      className="h-full w-full p-2"
    >
      <title id={titleId}>Clock states</title>
      <ArrowHead id={head} />
      <Transition
        path="M160 60 H320"
        head={head}
        label={CLOCK_MOVES.firstFrame}
        x={240}
        y={42}
      />
      <Transition
        path="M460 60 H640"
        head={head}
        label={CLOCK_MOVES.lock}
        x={550}
        y={42}
      />
      <Transition
        path="M685 90 V290"
        head={head}
        label={CLOCK_MOVES.hold}
        x={675}
        y={190}
        anchor="end"
      />
      <Transition
        path="M735 290 V90"
        head={head}
        label={CLOCK_MOVES.recover}
        x={745}
        y={190}
        anchor="start"
      />
      <Transition
        path="M640 320 H390 V90"
        head={head}
        label={CLOCK_MOVES.letGo}
        x={515}
        y={302}
      />
      {statusChips(clock).map(({ status, label, current }) => (
        <StateNode
          key={status}
          {...PLACES[status]}
          {...NODE}
          label={label}
          tone={current ? STATUS_TONES[status] : undefined}
        />
      ))}
      {clockCounters(clock).map(({ label, value, limit }, row) => (
        <g key={label}>
          <text
            x={64}
            y={196 + row * 76}
            textAnchor="end"
            dominantBaseline="central"
            className="fill-foreground font-mono text-[34px] font-semibold"
          >
            {value}
          </text>
          <text
            x={80}
            y={186 + row * 76}
            dominantBaseline="central"
            className="fill-foreground text-[17px]"
          >
            {label}
          </text>
          <text
            x={80}
            y={208 + row * 76}
            dominantBaseline="central"
            className="fill-muted-foreground text-[14px]"
          >
            {limit}
          </text>
        </g>
      ))}
      <text
        x={20}
        y={410}
        dominantBaseline="central"
        className="fill-muted-foreground text-[14px]"
      >
        {CLOCK_MOVES.reset}
      </text>
    </svg>
  );
}
