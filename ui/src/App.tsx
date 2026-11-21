import { useEffect } from "react";
import { type Bridge } from "./protocol/bridge";
import { useAppStore } from "./state/store";
import { TooltipProvider } from "./components/ui/tooltip";
import { AppBar } from "./shell/AppBar";
import { FocusLayout } from "./shell/FocusLayout";
import { PipelineRail } from "./shell/PipelineRail";
import { ShortcutList } from "./shell/ShortcutList";
import { StatusBar } from "./shell/StatusBar";
import { WarningBanner } from "./shell/WarningBanner";
import {
  DecodedFramesPanel,
  FramePanel,
  PulseWidthPanel,
  SignalPanel,
  TimePanel,
} from "./shell/panels";
import { stages } from "./shell/stages";
import { useKeyboard } from "./shell/useKeyboard";
import type { SourceClock } from "./time/sourceClock";

/** The whole pipeline at a glance: the panels ordered by time scale. */
function Overview({ clock }: Readonly<{ clock: SourceClock }>) {
  return (
    <main className="grid min-h-0 flex-1 grid-cols-[minmax(0,1fr)_21.5rem] grid-rows-[11.125rem_14.125rem_minmax(0,1fr)] gap-3 p-3">
      <SignalPanel clock={clock} />
      <PulseWidthPanel />
      <FramePanel />
      <TimePanel clock={clock} className="row-span-2" />
      <DecodedFramesPanel />
    </main>
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

  useKeyboard();

  const focus = useAppStore((state) => state.focus);
  const focused = stages.find((stage) => stage.number === focus);

  return (
    <TooltipProvider>
      <div className="flex h-full flex-col">
        <AppBar bridge={bridge} />
        <WarningBanner />
        <PipelineRail />
        {focused ? (
          <FocusLayout stage={focused} clock={clock} />
        ) : (
          <Overview clock={clock} />
        )}
        <StatusBar />
        <ShortcutList />
      </div>
    </TooltipProvider>
  );
}
