import fixture from "./fixtures/synthetic-noise-0.jsonl?raw";
import { parseRecording } from "./recording";
import { createSimulator } from "./simulator";

export const speedFrom = (search: string): number => {
  const speed = new URLSearchParams(search).get("speed");
  const parsed = Number(speed);
  if (!speed || Number.isNaN(parsed) || parsed <= 0) {
    return 1;
  }
  return parsed;
};

export const createSimulatorBridge = (search: string) => {
  const speed = speedFrom(search);
  return createSimulator(parseRecording(fixture), { speed });
};
