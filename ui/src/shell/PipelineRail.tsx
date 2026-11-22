import {
  Circle,
  CircleCheck,
  CircleX,
  TriangleAlert,
  type LucideIcon,
} from "lucide-react";
import { useShallow } from "zustand/react/shallow";
import { stages, type Health, type Stage } from "./stages";
import { focusStage } from "./transition";
import { useAppStore } from "@/state/store";

/** A completed frame passes through stages 4 to 7, and their buttons light in that order. */
const FIRST_LIT_STAGE = 4;
/** The time between one button lighting and the next, in milliseconds. */
const LIGHT_STEP_MS = 80;

const healthMarks: Record<
  Health,
  { icon: LucideIcon; label: string; className: string }
> = {
  idle: { icon: Circle, label: "no data", className: "text-muted-foreground" },
  good: { icon: CircleCheck, label: "good", className: "text-good" },
  warn: { icon: TriangleAlert, label: "warning", className: "text-warn" },
  bad: { icon: CircleX, label: "fault", className: "text-bad" },
};

const StageButton = ({ stage }: Readonly<{ stage: Stage }>) => {
  const { readout, health } = useAppStore(useShallow(stage.view));
  const focused = useAppStore((state) => state.focus === stage.number);
  const frameCount = useAppStore((state) => state.frameCount);
  // A pointer has no Esc, so the highlighted button leads back to the overview.
  const toggleFocus = () => focusStage(focused ? null : stage.number);
  const mark = healthMarks[health];
  const Icon = mark.icon;
  return (
    <button
      type="button"
      aria-pressed={focused}
      onClick={toggleFocus}
      className="relative flex w-full flex-col gap-0.5 rounded-md border border-input bg-secondary px-2.5 py-1.5 text-left hover:bg-accent aria-pressed:border-signal aria-pressed:bg-accent aria-pressed:ring-1 aria-pressed:ring-signal"
    >
      <span className="flex items-center gap-1.5">
        <span className="font-mono text-[0.65625rem] text-muted-foreground">
          {stage.number}
        </span>
        <span className="text-[0.8125rem] font-semibold">{stage.name}</span>
        <Icon className={`ml-auto size-3.5 ${mark.className}`} />
        <span className="sr-only">{mark.label}</span>
      </span>
      <span className="truncate font-mono text-xs text-foreground-soft">
        {readout}
      </span>
      {stage.number >= FIRST_LIT_STAGE && frameCount > 0 && (
        <span
          // A new frame is a new element, so its animation runs once for each frame.
          key={frameCount}
          aria-hidden="true"
          className="pointer-events-none absolute inset-0 animate-rail-light rounded-md ring-2 ring-signal motion-reduce:hidden"
          style={{
            animationDelay: `${(stage.number - FIRST_LIT_STAGE) * LIGHT_STEP_MS}ms`,
          }}
        />
      )}
    </button>
  );
};

export const PipelineRail = () => {
  return (
    <nav
      aria-label="Pipeline"
      className="flex h-14.5 flex-none items-center border-b bg-bar px-3"
    >
      <ol className="relative grid w-full grid-cols-7 gap-3 before:absolute before:inset-x-0 before:top-1/2 before:h-px before:bg-input">
        {stages.map((stage) => (
          <li key={stage.number}>
            <StageButton stage={stage} />
          </li>
        ))}
      </ol>
    </nav>
  );
};
