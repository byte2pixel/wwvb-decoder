import { cn } from "cn";

// The pieces a state diagram is drawn from, in SVG. A diagram sets a viewBox and places these
// in its units, so the whole drawing scales with its panel, text included.

/** How the current state is tinted: the rail's health colors. */
export type Tone = "idle" | "good" | "warn" | "bad";

const BOX_TONES: Record<Tone, string> = {
  idle: "fill-accent stroke-foreground-soft",
  good: "fill-good/15 stroke-good",
  warn: "fill-warn/15 stroke-warn",
  bad: "fill-bad/15 stroke-bad",
};

const TEXT_TONES: Record<Tone, string> = {
  idle: "fill-foreground",
  good: "fill-good",
  warn: "fill-warn",
  bad: "fill-bad",
};

interface StateNodeProps {
  x: number;
  y: number;
  width: number;
  height: number;
  label: string;
  /** A second line under the label, such as a count. */
  detail?: string;
  /** The tint of the state the machine is in now. Without it the node is drawn at rest. */
  tone?: Tone;
}

/** One state: a rounded box with its name. The current one is tinted and marked `aria-current`. */
export function StateNode({
  x,
  y,
  width,
  height,
  label,
  detail,
  tone,
}: Readonly<StateNodeProps>) {
  const middle = x + width / 2;

  return (
    <g aria-current={tone ? "true" : undefined}>
      <rect
        x={x}
        y={y}
        width={width}
        height={height}
        rx={10}
        className={cn(
          "stroke-[1.5] transition-colors duration-200",
          tone ? BOX_TONES[tone] : "fill-card stroke-input",
        )}
      />
      <text
        x={middle}
        y={detail === undefined ? y + height / 2 : y + height * 0.36}
        textAnchor="middle"
        dominantBaseline="central"
        className={cn(
          "text-[20px] font-semibold transition-colors duration-200",
          tone ? TEXT_TONES[tone] : "fill-muted-foreground",
        )}
      >
        {label}
      </text>
      {detail !== undefined && (
        <text
          x={middle}
          y={y + height * 0.72}
          textAnchor="middle"
          dominantBaseline="central"
          className="fill-foreground-soft font-mono text-[14px]"
        >
          {detail}
        </text>
      )}
    </g>
  );
}

/** The arrowhead every transition ends in. Put one in a diagram and give its id to `Transition`. */
export function ArrowHead({ id }: Readonly<{ id: string }>) {
  return (
    <defs>
      <marker
        id={id}
        viewBox="0 0 10 10"
        refX={9}
        refY={5}
        markerWidth={8}
        markerHeight={8}
        orient="auto-start-reverse"
      >
        <path d="M0 0 L10 5 L0 10 z" className="fill-foreground-soft" />
      </marker>
    </defs>
  );
}

interface TransitionProps {
  /** The arrow's path, from the state it leaves to the state it enters. */
  path: string;
  /** The id of the diagram's `ArrowHead`. */
  head: string;
  /** What moves the machine along the arrow. */
  label: string;
  /** Where the label is anchored. */
  x: number;
  y: number;
  anchor?: "start" | "middle" | "end";
  /** Draws the label in the fault color, for a transition that was just taken as a fault. */
  fault?: boolean;
}

/** One transition: an arrow and the words for what causes it. */
export function Transition({
  path,
  head,
  label,
  x,
  y,
  anchor = "middle",
  fault = false,
}: Readonly<TransitionProps>) {
  return (
    <g>
      <path
        d={path}
        markerEnd={`url(#${head})`}
        className="fill-none stroke-foreground-soft stroke-[1.5]"
      />
      <text
        x={x}
        y={y}
        textAnchor={anchor}
        dominantBaseline="central"
        className={cn(
          "text-[15px] transition-colors duration-200",
          fault ? "fill-bad font-semibold" : "fill-foreground-soft",
        )}
      >
        {label}
      </text>
    </g>
  );
}
