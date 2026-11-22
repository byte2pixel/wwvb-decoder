import { useAppStore } from "@/state/store";
import { FrameGrid } from "./FrameView";
import {
  decodedFrameLine,
  fieldValues,
  frameSymbols,
  setWeights,
} from "./model";

/**
 * The last complete frame: its 60 symbols, and under each field the value the host decoded
 * and the weights of the bits that were 1.
 */
export function DecodedFrameView() {
  const decodedFrame = useAppStore((state) => state.decodedFrame);
  const layout = useAppStore((state) => state.frameLayout);

  return (
    <FrameGrid
      symbols={frameSymbols(decodedFrame)}
      values={fieldValues(decodedFrame)}
      weights={setWeights(decodedFrame, layout)}
    />
  );
}

/** The last-frame panel's header line: the seconds that hold an error symbol. */
export function DecodedFrameLine() {
  const decodedFrame = useAppStore((state) => state.decodedFrame);

  return decodedFrameLine(decodedFrame);
}
