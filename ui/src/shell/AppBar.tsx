import { Button } from "@/components/ui/button";
import {
  Popover,
  PopoverContent,
  PopoverHeader,
  PopoverTitle,
  PopoverTrigger,
} from "@/components/ui/popover";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Slider } from "@/components/ui/slider";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import type { Bridge } from "@/protocol/bridge";
import type { Command, PinLevel } from "@/protocol/messages";
import { Keyboard, SlidersHorizontal, SunMoon } from "lucide-react";
import { useAppStore, type Picks } from "@/state/store";
import { useId } from "react";
import { captureLabel } from "./captures";
import { toggleTheme } from "./theme";

const sources: readonly { kind: Picks["source"]; label: string }[] = [
  { kind: "serial", label: "Live" },
  { kind: "replay", label: "Replay" },
  { kind: "synthetic", label: "Synthetic" },
];

const speeds = [1, 10, 60] as const;

const levels: readonly { level: PinLevel; label: string }[] = [
  { level: "high", label: "High" },
  { level: "low", label: "Low" },
];

function PickList({
  label,
  items,
  value,
  empty,
  onPick,
  onOpen,
}: Readonly<{
  label: string;
  items: readonly { value: string; label: string }[];
  value: string | undefined;
  empty: string;
  onPick: (value: string | null) => void;
  onOpen: () => void;
}>) {
  return (
    <Select
      items={items}
      value={value ?? null}
      onValueChange={(picked, { reason }) => {
        if (reason !== "none") onPick(picked);
      }}
      onOpenChange={(open) => {
        if (open) onOpen();
      }}
    >
      <SelectTrigger size="sm" aria-label={label}>
        <SelectValue placeholder={empty} />
      </SelectTrigger>
      <SelectContent>
        {items.map((item) => (
          <SelectItem key={item.value} value={item.value}>
            {item.label}
          </SelectItem>
        ))}
        {items.length === 0 && (
          <SelectItem value="" disabled>
            {empty}
          </SelectItem>
        )}
      </SelectContent>
    </Select>
  );
}

function SpeedButtons({
  speed,
  onPick,
}: Readonly<{ speed: number; onPick: (speed: number) => void }>) {
  return (
    <ToggleGroup
      aria-label="Speed"
      value={[String(speed)]}
      onValueChange={([picked]) => {
        const next = speeds.find((option) => String(option) === picked);
        if (next !== undefined) onPick(next);
      }}
      variant="outline"
      size="sm"
      spacing={0}
    >
      {speeds.map((option) => (
        <ToggleGroupItem key={option} value={String(option)}>
          {`${option}×`}
        </ToggleGroupItem>
      ))}
    </ToggleGroup>
  );
}

function SignalSettings({ bridge }: Readonly<{ bridge: Bridge }>) {
  const levelId = useId();
  const reducedLevel = useAppStore((state) => state.reducedLevel);
  const setReducedLevel = useAppStore((state) => state.setReducedLevel);

  return (
    <Popover>
      <Tooltip>
        <TooltipTrigger
          render={
            <PopoverTrigger
              render={
                <Button
                  className="ml-auto"
                  variant="ghost"
                  size="icon-sm"
                  aria-label="Signal settings"
                />
              }
            />
          }
        >
          <SlidersHorizontal />
        </TooltipTrigger>
        <TooltipContent>Signal settings</TooltipContent>
      </Tooltip>
      <PopoverContent align="end" className="w-auto">
        <PopoverHeader>
          <PopoverTitle>Signal settings</PopoverTitle>
        </PopoverHeader>
        <div className="flex items-center gap-3">
          <span id={levelId} className="text-xs text-muted-foreground">
            Reduced carrier level
          </span>
          <ToggleGroup
            aria-labelledby={levelId}
            value={[reducedLevel]}
            onValueChange={([picked]) => {
              const next = levels.find(({ level }) => level === picked);
              if (next) {
                setReducedLevel(next.level);
                bridge.send({
                  command: "setReducedCarrierLevel",
                  level: next.level,
                });
              }
            }}
            variant="outline"
            size="sm"
            spacing={0}
          >
            {levels.map(({ level, label }) => (
              <ToggleGroupItem key={level} value={level}>
                {label}
              </ToggleGroupItem>
            ))}
          </ToggleGroup>
        </div>
      </PopoverContent>
    </Popover>
  );
}
export function AppBar({ bridge }: Readonly<{ bridge: Bridge }>) {
  // The picks are in the store, where the host's selection sets them when the page loads.
  const picks = useAppStore((state) => state.picks);
  const setPicks = useAppStore((state) => state.setPicks);
  const { source, speed, noise } = picks;
  const noiseId = useId();
  const ports = useAppStore((state) => state.ports);
  const captures = useAppStore((state) => state.captures);
  const replayRunning = useAppStore(
    (state) =>
      state.sourceStatus?.source === "replay" &&
      state.sourceStatus.state === "running",
  );
  const syntheticRunning = useAppStore(
    (state) =>
      state.sourceStatus?.source === "synthetic" &&
      state.sourceStatus.state === "running",
  );
  const setDrawer = useAppStore((state) => state.setDrawer);

  const port = ports.find((name) => name === picks.port) ?? ports.at(0);
  const capture = (
    captures.find((entry) => entry.path === picks.capture) ?? captures.at(0)
  )?.path;

  const startCommand = (): Command | null => {
    switch (source) {
      case "serial":
        return port === undefined
          ? null
          : { command: "selectSource", source: "serial", port };
      case "replay":
        return capture === undefined
          ? null
          : {
              command: "selectSource",
              source: "replay",
              path: capture,
              speed,
            };
      case "synthetic":
        return { command: "selectSource", source: "synthetic", noise };
    }
  };
  const start = startCommand();

  return (
    <header className="flex h-11.5 flex-none items-center gap-3 border-b bg-bar px-3">
      <h1 className="text-sm font-semibold whitespace-nowrap">WWVB decoder</h1>
      <ToggleGroup
        aria-label="Source"
        value={[source]}
        onValueChange={([picked]) => {
          const next = sources.find(({ kind }) => kind === picked);
          if (next) setPicks({ source: next.kind });
        }}
        variant="outline"
        size="sm"
        spacing={0}
      >
        {sources.map(({ kind, label }) => (
          <ToggleGroupItem key={kind} value={kind}>
            {label}
          </ToggleGroupItem>
        ))}
      </ToggleGroup>
      {source === "serial" && (
        <PickList
          label="Port"
          items={ports.map((name) => ({ value: name, label: name }))}
          value={port}
          empty="No ports found"
          onPick={(picked) => setPicks({ port: picked })}
          onOpen={() => bridge.send({ command: "listPorts" })}
        />
      )}
      {source === "replay" && (
        <>
          <PickList
            label="Capture"
            items={captures.map((entry) => ({
              value: entry.path,
              label: captureLabel(entry),
            }))}
            value={capture}
            empty="No captures found"
            onPick={(picked) => setPicks({ capture: picked })}
            onOpen={() => bridge.send({ command: "listCaptures" })}
          />
          <SpeedButtons
            speed={speed}
            onPick={(next) => {
              setPicks({ speed: next });
              if (replayRunning) {
                bridge.send({ command: "setReplaySpeed", speed: next });
              }
            }}
          />
        </>
      )}
      {source === "synthetic" && (
        <>
          <span id={noiseId} className="text-xs text-muted-foreground">
            Noise
          </span>
          <div className="w-32">
            <Slider
              aria-labelledby={noiseId}
              min={0}
              max={1}
              step={0.05}
              value={[noise]}
              onValueChange={(value) => {
                const next = typeof value === "number" ? value : value[0];
                setPicks({ noise: next });
                if (syntheticRunning) {
                  bridge.send({ command: "setNoise", noise: next });
                }
              }}
            />
          </div>
          <span className="w-8 font-mono text-xs">{noise.toFixed(2)}</span>
        </>
      )}
      <Button
        size="sm"
        disabled={start === null}
        onClick={() => {
          if (start) bridge.send(start);
        }}
      >
        Start
      </Button>
      <Button
        size="sm"
        variant="outline"
        onClick={() =>
          bridge.send({
            command: "stop",
          })
        }
      >
        Stop
      </Button>
      <SignalSettings bridge={bridge} />
      <Tooltip>
        <TooltipTrigger
          render={
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label="Switch theme"
              onClick={toggleTheme}
            />
          }
        >
          <SunMoon />
        </TooltipTrigger>
        <TooltipContent>Switch theme</TooltipContent>
      </Tooltip>
      <Tooltip>
        <TooltipTrigger
          render={
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label="Keyboard shortcuts"
              onClick={() => setDrawer("shortcuts")}
            />
          }
        >
          <Keyboard />
        </TooltipTrigger>
        <TooltipContent>Keyboard shortcuts</TooltipContent>
      </Tooltip>
    </header>
  );
}
