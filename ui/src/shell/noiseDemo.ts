import type { Command } from "@/protocol/messages";
import type { Picks } from "@/state/store";

/**
 * The noise of the demo. At 0.10 the synthetic source still completes frames, and stage 6
 * rejects some of them, which is what the demo is there to show.
 */
export const demoNoise = 0.1;

/**
 * True for the demo's noise. The slider steps by 0.05, so a noise within half a step of
 * `demoNoise` is the one the slider shows as 0.10, whatever the arithmetic left in its last digit.
 */
const isDemoNoise = (noise: number): boolean =>
  Math.abs(noise - demoNoise) < 0.025;

/** What `D` changes: the picks the app bar shows, and the command that makes the host follow. */
export interface NoiseDemo {
  picks: Pick<Picks, "source" | "noise">;
  command: Command;
}

/**
 * The noise demo preset, which `D` switches on and off.
 *
 * On starts the synthetic source at `demoNoise`, also when the synthetic source is the one
 * running. The source draws the same noise on every start, so the first frame of a new start
 * is rejected one minute in, every time. Noise turned up on a source that already runs lands
 * somewhere else in that noise, and a rejected frame is then a matter of luck.
 *
 * Off is for a synthetic source that runs the demo: it keeps running and loses its noise.
 * `noise` is the picked noise, which is the running one while the synthetic source runs.
 */
export function noiseDemo(syntheticRunning: boolean, noise: number): NoiseDemo {
  if (syntheticRunning && isDemoNoise(noise)) {
    return {
      picks: { source: "synthetic", noise: 0 },
      command: { command: "setNoise", noise: 0 },
    };
  }
  return {
    picks: { source: "synthetic", noise: demoNoise },
    command: {
      command: "selectSource",
      source: "synthetic",
      noise: demoNoise,
    },
  };
}
