import type { Command } from "@/protocol/messages";
import type { Picks } from "@/state/store";

/**
 * The noise of the demo. At 0.10 the synthetic source still completes frames, and stage 6
 * rejects some of them, which is what the demo is there to show.
 */
export const demoNoise = 0.1;

/** What `D` changes: the picks the app bar shows, and the command that makes the host follow. */
export interface NoiseDemo {
  picks: Pick<Picks, "source" | "noise">;
  command: Command;
}

/**
 * The noise demo preset, which `D` switches on and off. On is the synthetic source at
 * `demoNoise`, and off is the same source with no noise. A synthetic source that is running
 * keeps running and only changes its noise, so the clock it has locked is not started again.
 * `noise` is the picked noise, which is the running one while the synthetic source runs.
 */
export function noiseDemo(syntheticRunning: boolean, noise: number): NoiseDemo {
  const next = syntheticRunning && noise === demoNoise ? 0 : demoNoise;
  return {
    picks: { source: "synthetic", noise: next },
    command: syntheticRunning
      ? { command: "setNoise", noise: next }
      : { command: "selectSource", source: "synthetic", noise: next },
  };
}
