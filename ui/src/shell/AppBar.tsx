import { Button } from "@/components/ui/button";
import { Slider } from "@/components/ui/slider";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import type { Bridge } from "@/protocol/bridge";
import { SunMoon } from "lucide-react";
import { useState, useId } from "react";

export function AppBar({ bridge }: Readonly<{ bridge: Bridge }>) {
  const [noise, setNoise] = useState(0);
  const noiseId = useId();

  return (
    <header className="flex h-11.5 flex-none items-center gap-3 border-b bg-bar px-3">
      <h1 className="text-sm font-semibold whitespace-nowrap">WWVB decoder</h1>
      <ToggleGroup
        aria-label="Source"
        value={["synthetic"]}
        variant="outline"
        size="sm"
        spacing={0}
      >
        <ToggleGroupItem value="serial" disabled>
          Live
        </ToggleGroupItem>
        <ToggleGroupItem value="replay" disabled>
          Replay
        </ToggleGroupItem>
        <ToggleGroupItem value="synthetic">Synthetic</ToggleGroupItem>
      </ToggleGroup>

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
      <Button
        size="sm"
        onClick={() =>
          bridge.send({
            command: "selectSource",
            source: "synthetic",
            noise,
          })
        }
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
              onClick={() => document.documentElement.classList.toggle("dark")}
            />
          }
        >
          <SunMoon />
        </TooltipTrigger>
        <TooltipContent>Switch theme</TooltipContent>
      </Tooltip>
    </header>
  );
}
