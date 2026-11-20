import { useAppStore } from "@/state/store";
import { FrameGrid } from "./FrameView";
import { decodedFrameLine, fieldValues, frameSymbols } from "./model";

/** The last complete frame: its 60 symbols, and under each field the value the host decoded. */
export function DecodedFrameView() {
  const decodedFrame = useAppStore((state) => state.decodedFrame);

  return (
    <FrameGrid
      symbols={frameSymbols(decodedFrame)}
      values={fieldValues(decodedFrame)}
    />
  );
}

/** The last-frame panel's header line: the seconds that hold an error symbol. */
export function DecodedFrameLine() {
  const decodedFrame = useAppStore((state) => state.decodedFrame);

  return decodedFrameLine(decodedFrame);
}
