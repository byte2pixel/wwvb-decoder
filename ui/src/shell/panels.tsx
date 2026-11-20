import type { SourceClock } from "@/time/sourceClock";
import { EdgeLog } from "@/views/edge-log/EdgeLog";
import { FramesTable } from "@/views/frames-table/FramesTable";
import { FrameHeaderLine, FrameView } from "@/views/frame/FrameView";
import {
  LastPulseLine,
  PulseWidthView,
} from "@/views/pulse-width/PulseWidthView";
import { ALL_LANES, type LaneName } from "@/views/signal/model";
import { SignalView } from "@/views/signal/SignalView";
import { timeWindowFor } from "@/views/signal/timeWindow";
import { Panel } from "./Panel";
import { useAppStore } from "@/state/store";

// The screen's panels, each a view inside its Panel. The overview and the focus views
// lay out the same ones.

/** Extra classes for the panel's place in a layout. */
interface Placed {
  className?: string;
}

/** The stage each lane of the signal view shows. */
const LANE_STAGES: Record<LaneName, number> = {
  carrier: 1,
  pulse: 2,
  symbol: 3,
  second: 4,
};

interface SignalPanelProps extends Placed {
  clock: SourceClock;
  /** A constant, as `SignalView` asks. */
  lanes?: readonly LaneName[];
}

export function SignalPanel({
  clock,
  lanes = ALL_LANES,
  className,
}: Readonly<SignalPanelProps>) {
  const speed = useAppStore((state) => state.sourceStatus?.speed ?? 1);
  const { windowMicros } = timeWindowFor(speed);

  return (
    <Panel
      title="Signal"
      stages={lanes.map((lane) => LANE_STAGES[lane])}
      state={`${windowMicros / 1_000_000} s window`}
      className={className}
    >
      <SignalView clock={clock} show={lanes} />
    </Panel>
  );
}

export function PulseWidthPanel({ className }: Readonly<Placed>) {
  return (
    <Panel
      title="Pulse width"
      stages={[3]}
      state={<LastPulseLine />}
      className={className}
    >
      <PulseWidthView />
    </Panel>
  );
}

export function EdgeLogPanel({ className }: Readonly<Placed>) {
  return (
    <Panel
      title="Edge log"
      stages={[1]}
      state="Newest first"
      className={className}
    >
      <EdgeLog />
    </Panel>
  );
}

export function FramePanel({ className }: Readonly<Placed>) {
  return (
    <Panel
      title="Frame"
      stages={[4]}
      state={<FrameHeaderLine />}
      className={className}
    >
      <FrameView />
    </Panel>
  );
}

interface DecodedFramesPanelProps extends Placed {
  /** How many frames to list. The overview has room for three. */
  rows?: number;
}

export function DecodedFramesPanel({
  rows = 3,
  className,
}: Readonly<DecodedFramesPanelProps>) {
  return (
    <Panel title="Decoded frames" stages={[5, 6]} className={className}>
      <FramesTable maxRows={rows} />
    </Panel>
  );
}

// The one below shows its no-data state until Step 13 gives it a view.
export function TimePanel({ className }: Readonly<Placed>) {
  return (
    <Panel title="Time" stages={[7]} state="No signal" className={className}>
      <p className="flex h-full items-center justify-center font-mono text-[3.75rem] font-semibold text-muted-foreground">
        --:--:--
      </p>
    </Panel>
  );
}
