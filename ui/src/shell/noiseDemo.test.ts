import { describe, expect, it } from "vitest";
import { demoNoise, noiseDemo } from "./noiseDemo";

describe("noiseDemo", () => {
  it("is the synthetic source at a noise of 0.10", () => {
    expect(demoNoise).toBe(0.1);
  });

  it.each([0, 0.1, 0.4])(
    "starts the synthetic source at the demo's noise when it is not running, whatever noise is picked (%d)",
    (picked) => {
      expect(noiseDemo(false, picked)).toEqual({
        picks: { source: "synthetic", noise: 0.1 },
        command: { command: "selectSource", source: "synthetic", noise: 0.1 },
      });
    },
  );

  it.each([0, 0.05, 0.4, 1])(
    "changes only the noise of a synthetic source that runs at %d",
    (running) => {
      expect(noiseDemo(true, running)).toEqual({
        picks: { source: "synthetic", noise: 0.1 },
        command: { command: "setNoise", noise: 0.1 },
      });
    },
  );

  it("takes the noise away again from a synthetic source that runs the demo", () => {
    expect(noiseDemo(true, 0.1)).toEqual({
      picks: { source: "synthetic", noise: 0 },
      command: { command: "setNoise", noise: 0 },
    });
  });
});
