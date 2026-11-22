import type { SourceClock } from "@/time/sourceClock";
import { CheckList, ChecksLine } from "@/views/checks/CheckList";
import { EdgeLog } from "@/views/edge-log/EdgeLog";
import { InfoLines } from "@/views/edge-log/InfoLines";
import { FramesTable } from "@/views/frames-table/FramesTable";
import {
  DecodedFrameLine,
  DecodedFrameView,
} from "@/views/frame/DecodedFrameView";
import { DecodedRecord } from "@/views/frame/DecodedRecord";
import { FrameHeaderLine, FrameView } from "@/views/frame/FrameView";
import { SyncDiagram } from "@/views/frame/SyncDiagram";
import { SyncLossList } from "@/views/frame/SyncLossList";
import { Classification } from "@/views/pulse-width/Classification";
import {
  LastPulseLine,
  PulseWidthView,
} from "@/views/pulse-width/PulseWidthView";
import { ALL_LANES, type LaneName } from "@/views/signal/model";
import { SignalView } from "@/views/signal/SignalView";
import { timeWindowFor } from "@/views/signal/timeWindow";
import { ClockDiagram } from "@/views/time/ClockDiagram";
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

interface PulseWidthPanelProps extends Placed {
  /** Names the class zones and labels the last pulse, as the stage 3 focus view does. */
  detailed?: boolean;
}

export function PulseWidthPanel({
  detailed = false,
  className,
}: Readonly<PulseWidthPanelProps>) {
  return (
    <Panel
      title="Pulse width"
      stages={[3]}
      state={<LastPulseLine />}
      className={className}
    >
      <PulseWidthView detailed={detailed} />
    </Panel>
  );
}

export function ClassificationPanel({ className }: Readonly<Placed>) {
  return (
    <Panel
      title="Last pulse"
      stages={[3]}
      state="Input, rule, output"
      className={className}
    >
      <Classification />
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

export function InfoLinesPanel({ className }: Readonly<Placed>) {
  return (
    <Panel
      title="Info lines"
      stages={[1]}
      state="Newest first"
      className={className}
    >
      <InfoLines />
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

export function SyncDiagramPanel({ className }: Readonly<Placed>) {
  return (
    <Panel title="Frame sync states" stages={[4]} className={className}>
      <SyncDiagram />
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

export function DecodedRecordPanel({ className }: Readonly<Placed>) {
  return (
    <Panel
      title="Decoded frame"
      stages={[5]}
      state="Handed to stage 6"
      className={className}
    >
      <DecodedRecord />
    </Panel>
  );
}

interface DecodedFramesPanelProps extends Placed {
  /** How many frames to list. The overview has room for three. */
  rows?: number;
  /** Lets a row be picked, as the stage 6 focus view does, to list that frame's checks. */
  selectable?: boolean;
}

export function DecodedFramesPanel({
  rows = 3,
  selectable = false,
  className,
}: Readonly<DecodedFramesPanelProps>) {
  return (
    <Panel title="Decoded frames" stages={[5, 6]} className={className}>
      <FramesTable maxRows={rows} selectable={selectable} />
    </Panel>
  );
}

interface ChecksPanelProps extends Placed {
  /** How many rows the table beside it shows: a frame's checks are listed while its row is. */
  rows: number;
}

export function ChecksPanel({ rows, className }: Readonly<ChecksPanelProps>) {
  return (
    <Panel
      title="Checks"
      stages={[6]}
      state={<ChecksLine maxRows={rows} />}
      className={className}
    >
      <CheckList maxRows={rows} />
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

export function ClockDiagramPanel({ className }: Readonly<Placed>) {
  return (
    <Panel title="Clock states" stages={[7]} className={className}>
      <ClockDiagram />
    </Panel>
  );
}
