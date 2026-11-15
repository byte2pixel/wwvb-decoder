import { useEffect, type ReactNode } from "react";
import { type Bridge } from "./protocol/bridge";
import { useAppStore } from "./state/store";
import { TooltipProvider } from "./components/ui/tooltip";
import { AppBar } from "./shell/AppBar";
import { PipelineRail } from "./shell/PipelineRail";
import { StatusBar } from "./shell/StatusBar";
import { Panel } from "./shell/Panel";
import type { SourceClock } from "./time/sourceClock";
import { SignalView } from "./views/signal/SignalView";
import { timeWindowFor } from "./views/signal/timeWindow";

/** What a panel shows before it has anything to draw. */
function Placeholder({ children }: Readonly<{ children: ReactNode }>) {
  return (
    <p className="flex h-full items-center justify-center text-xs text-muted-foreground">
      {children}
    </p>
  );
}

export default function App({
  bridge,
  clock,
}: Readonly<{ bridge: Bridge; clock: SourceClock }>) {
  useEffect(() => {
    const off = bridge.onMessage(useAppStore.getState().dispatch);
    bridge.send({ command: "hello", protocol: 1 });
    return off;
  }, [bridge]);

  const { windowMicros } = timeWindowFor(clock.speed());
  const windowSeconds = windowMicros / 1_000_000;

  return (
    <TooltipProvider>
      <div className="flex h-full flex-col">
        <AppBar bridge={bridge} />
        <PipelineRail />
        <main className="grid min-h-0 flex-1 grid-cols-[minmax(0,1fr)_21.5rem] grid-rows-[11.625rem_14.625rem_minmax(0,1fr)] gap-3 p-3">
          <Panel
            title="Signal"
            stages={[1, 2, 3, 4]}
            state={`${windowSeconds} s window`}
          >
            <SignalView clock={clock} />
          </Panel>
          <Panel title="Pulse width" stages={[3]}>
            <Placeholder>No pulses yet</Placeholder>
          </Panel>
          <Panel title="Frame" stages={[4]}>
            <Placeholder>No symbols placed yet</Placeholder>
          </Panel>
          <Panel
            title="Time"
            stages={[7]}
            state="No signal"
            className="row-span-2"
          >
            <p className="flex h-full items-center justify-center font-mono text-[3.75rem] font-semibold text-muted-foreground">
              --:--:--
            </p>
          </Panel>
          <Panel title="Decoded frames" stages={[5, 6]}>
            <Placeholder>No frames yet</Placeholder>
          </Panel>
        </main>
        <StatusBar />
      </div>
    </TooltipProvider>
  );
}
