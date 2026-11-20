import type { SourceClock } from "@/time/sourceClock";
import type { LaneName } from "@/views/signal/model";
import {
  DecodedFramesPanel,
  EdgeLogPanel,
  FramePanel,
  PulseWidthPanel,
  SignalPanel,
  TimePanel,
} from "./panels";
import type { Stage } from "./stages";

// Constants, because SignalView restarts its painting when it is handed a new array.
const CARRIER: readonly LaneName[] = ["carrier"];
const CARRIER_AND_PULSE: readonly LaneName[] = ["carrier", "pulse"];
const SYMBOL: readonly LaneName[] = ["symbol"];

interface FocusProps {
  stage: Stage;
  clock: SourceClock;
}

/** What a stage's focus view shows: the "Baseline" column of the table in ui-design.md. */
function StagePanels({ stage, clock }: Readonly<FocusProps>) {
  switch (stage.number) {
    case 1:
      return (
        <div className="grid h-full grid-cols-[minmax(0,1fr)_30rem] gap-3">
          <SignalPanel clock={clock} lanes={CARRIER} />
          <EdgeLogPanel />
        </div>
      );
    case 2:
      return (
        <SignalPanel
          clock={clock}
          lanes={CARRIER_AND_PULSE}
          className="h-full"
        />
      );
    case 3:
      return (
        <div className="grid h-full grid-rows-[minmax(0,1fr)_9rem] gap-3">
          <PulseWidthPanel />
          <SignalPanel clock={clock} lanes={SYMBOL} />
        </div>
      );
    case 4:
      return <FramePanel className="h-full" />;
    case 5:
    case 6:
      return <DecodedFramesPanel rows={16} className="h-full" />;
    default:
      // Stage 7.
      return <TimePanel className="h-full" />;
  }
}

/** One stage in place of the overview's panels. See "Focus views" in docs/design/ui-design.md. */
export function FocusLayout({ stage, clock }: Readonly<FocusProps>) {
  return (
    <main className="flex min-h-0 flex-1 flex-col gap-3 p-3">
      <header className="flex flex-none flex-col gap-1">
        <div className="flex items-baseline gap-3">
          <span className="font-mono text-[1.375rem] text-muted-foreground">
            {stage.number}
          </span>
          <h2 className="text-[1.375rem] font-semibold">{stage.name}</h2>
          <code className="font-mono text-sm text-muted-foreground">
            {stage.type}
          </code>
        </div>
        <p className="text-base text-foreground-soft">{stage.rule}</p>
      </header>
      <div className="min-h-0 flex-1">
        <StagePanels stage={stage} clock={clock} />
      </div>
    </main>
  );
}
