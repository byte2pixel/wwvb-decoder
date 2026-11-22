import { describe, expect, it } from "vitest";
import { demoNoise, noiseDemo } from "./noiseDemo";

const on = {
  picks: { source: "synthetic", noise: 0.1 },
  command: { command: "selectSource", source: "synthetic", noise: 0.1 },
};

describe("noiseDemo", () => {
  it("is the synthetic source at a noise of 0.10", () => {
    expect(demoNoise).toBe(0.1);
  });

  it.each([0, 0.1, 0.4])(
    "starts the synthetic source at the demo's noise when it is not running, whatever noise is picked (%d)",
    (picked) => {
      expect(noiseDemo(false, picked)).toEqual(on);
    },
  );

  // 0.05 and 0.15 are the slider's steps on either side of the demo's noise.
  it.each([0, 0.05, 0.15, 0.4, 1])(
    "starts the synthetic source again when it runs at %d, so that the first frame is the rejected one",
    (running) => {
      expect(noiseDemo(true, running)).toEqual(on);
    },
  );

  it.each([
    ["0.10", 0.1],
    ["0.10 but for the last digit, above", 0.1 + 0.2 - 0.2],
    ["0.10 but for the last digit, below", 0.3 - 0.2],
  ])(
    "takes the noise away from a synthetic source that runs at %s, and does not start it again",
    (_, running) => {
      expect(noiseDemo(true, running)).toEqual({
        picks: { source: "synthetic", noise: 0 },
        command: { command: "setNoise", noise: 0 },
      });
    },
  );
});
