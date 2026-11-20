import type { SourceClock } from "@/time/sourceClock";
import { EdgeLog } from "@/views/edge-log/EdgeLog";
import { FramesTable } from "@/views/frames-table/FramesTable";
import {
  DecodedFrameLine,
  DecodedFrameView,
} from "@/views/frame/DecodedFrameView";
import { FrameHeaderLine, FrameView } from "@/views/frame/FrameView";
import { SyncLossList } from "@/views/frame/SyncLossList";
import {
  LastPulseLine,
  PulseWidthView,
} from "@/views/pulse-width/PulseWidthView";
import { ALL_LANES, type LaneName } from "@/views/signal/model";
import { SignalView } from "@/views/signal/SignalView";
import { timeWindowFor } from "@/views/signal/timeWindow";
import { ClockStatusLine, TimeView } from "@/views/time/TimeView";
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

export function SyncLossPanel({ className }: Readonly<Placed>) {
  return (
    <Panel
      title="Sync losses"
      stages={[4]}
      state="Newest first"
      className={className}
    >
      <SyncLossList />
    </Panel>
  );
}

export function LastFramePanel({ className }: Readonly<Placed>) {
  return (
    <Panel
      title="Last complete frame"
      stages={[5]}
      state={<DecodedFrameLine />}
      className={className}
    >
      <DecodedFrameView />
    </Panel>
  );
}

interface DecodedFramesPanelProps extends Placed {
  /** How many frames to list. The overview has room for three. */
  rows?: number;
  /** Lists every failure of a rejected frame in its row, as the stage 6 focus view does. */
  details?: boolean;
}

export function DecodedFramesPanel({
  rows = 3,
  details = false,
  className,
}: Readonly<DecodedFramesPanelProps>) {
  return (
    <Panel title="Decoded frames" stages={[5, 6]} className={className}>
      <FramesTable maxRows={rows} details={details} />
    </Panel>
  );
}

interface TimePanelProps extends Placed {
  clock: SourceClock;
}

export function TimePanel({ clock, className }: Readonly<TimePanelProps>) {
  return (
    <Panel
      title="Time"
      stages={[7]}
      state={<ClockStatusLine />}
      className={className}
    >
      <TimeView clock={clock} />
    </Panel>
  );
}
