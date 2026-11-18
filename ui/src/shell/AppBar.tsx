import { Button } from "@/components/ui/button";
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
import type { Command, Source } from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import { Keyboard, SunMoon } from "lucide-react";
import { useState, useId } from "react";
import { captureLabel } from "./captures";
import { toggleTheme } from "./theme";

type SourceKind = Exclude<Source, "none">;

const sources: readonly { kind: SourceKind; label: string }[] = [
  { kind: "serial", label: "Live" },
  { kind: "replay", label: "Replay" },
  { kind: "synthetic", label: "Synthetic" },
];

const speeds = [1, 10, 60] as const;

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
      onValueChange={onPick}
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

export function AppBar({ bridge }: Readonly<{ bridge: Bridge }>) {
  const [source, setSource] = useState<SourceKind>("synthetic");
  const [pickedPort, setPickedPort] = useState<string | null>(null);
  const [pickedCapture, setPickedCapture] = useState<string | null>(null);
  const [speed, setSpeed] = useState<number>(1);
  const [noise, setNoise] = useState(0);
  const noiseId = useId();
  const ports = useAppStore((state) => state.ports);
  const captures = useAppStore((state) => state.captures);
  const replayRunning = useAppStore(
    (state) =>
      state.sourceStatus?.source === "replay" &&
      state.sourceStatus.state === "running",
  );
  const setDrawer = useAppStore((state) => state.setDrawer);

  const port = pickedPort ?? ports.at(0);
  const capture = pickedCapture ?? captures.at(0)?.path;

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
          if (next) setSource(next.kind);
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
          onPick={setPickedPort}
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
            onPick={setPickedCapture}
            onOpen={() => bridge.send({ command: "listCaptures" })}
          />
          <SpeedButtons
            speed={speed}
            onPick={(next) => {
              setSpeed(next);
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
              onValueChange={(value) =>
                setNoise(typeof value === "number" ? value : value[0])
              }
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
      <Tooltip>
        <TooltipTrigger
          render={
            <Button
              className="ml-auto"
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
