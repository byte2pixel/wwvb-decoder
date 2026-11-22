import { cn } from "cn";
import { CircleCheck, CircleX } from "lucide-react";
import { useAppStore } from "@/state/store";
import {
  frameRows,
  shownFrame,
  type FrameRow,
} from "@/views/frames-table/model";
import { checkLines, checksLine, frameTime, verdict } from "./model";

interface ChecksProps {
  /** How many rows the table beside the list shows. A frame is listed only while its row is. */
  maxRows: number;
}

/** The frame the checks are for: the selected one while the table shows it, else the newest. */
function useShownFrame(maxRows: number): FrameRow | null {
  const validations = useAppStore((state) => state.validations);
  const frameCount = useAppStore((state) => state.frameCount);
  const selected = useAppStore((state) => state.selectedFrame);

  return shownFrame(
    frameRows(validations, frameCount).slice(0, maxRows),
    selected,
  );
}

/** The ten checks of one frame, each passed or failed, under the frame and its verdict. */
export function CheckList({ maxRows }: Readonly<ChecksProps>) {
  const frame = useShownFrame(maxRows);

  if (!frame) {
    return (
      <p className="flex h-full items-center justify-center text-xs text-muted-foreground">
        No frames yet
      </p>
    );
  }

  return (
    <div className="flex h-full flex-col px-3 py-2">
      <p className="flex flex-none items-baseline gap-3 border-b pb-1.5 text-base">
        <span className="font-mono text-muted-foreground">#{frame.number}</span>
        <span className="font-mono">{frameTime(frame)}</span>
        <span
          className={cn(
            "ml-auto font-semibold",
            frame.accepted ? "text-good" : "text-bad",
          )}
        >
          {verdict(frame)}
        </span>
      </p>
      <ol
        aria-label={`Checks of frame ${frame.number}`}
        className="flex min-h-0 flex-1 flex-col justify-between pt-1.5"
      >
        {checkLines(frame.failures).map(({ check, name, passed, text }) => {
          const Icon = passed ? CircleCheck : CircleX;
          return (
            <li key={check} className="flex items-start gap-2">
              <Icon
                className={cn(
                  "mt-0.5 size-4 flex-none",
                  passed ? "text-good" : "text-bad",
                )}
              />
              <div className="min-w-0 flex-1">
                <p className="text-base leading-tight font-semibold">
                  {name}
                  <span className="sr-only">
                    {passed ? ": passed" : ": failed"}
                  </span>
                </p>
                <p
                  title={passed ? undefined : text}
                  className={cn(
                    "truncate text-[0.8125rem] leading-tight",
                    passed ? "text-muted-foreground" : "font-mono text-bad",
                  )}
                >
                  {text}
                </p>
              </div>
            </li>
          );
        })}
      </ol>
    </div>
  );
}

/** The checks panel's header line: which frame the checks are for. */
export function ChecksLine({ maxRows }: Readonly<ChecksProps>) {
  const frame = useShownFrame(maxRows);
  const selected = useAppStore((state) => state.selectedFrame);

  return checksLine(frame, selected);
}
