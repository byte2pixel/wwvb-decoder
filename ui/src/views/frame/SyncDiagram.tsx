import { useId } from "react";
import { useAppStore } from "@/state/store";
import {
  ArrowHead,
  StateNode,
  Transition,
  type Tone,
} from "@/views/state-diagram/parts";
import { SYNC_MOVES, syncDiagram } from "./model";

const NODE = { y: 38, width: 190, height: 72 };

/**
 * Frame sync's two states as a diagram: the state it is in now with its count, and an arrow
 * each way with what causes the move. For 5 s after a loss the arrow back names the reason.
 */
export function SyncDiagram() {
  const frameSync = useAppStore((state) => state.frameSync);
  const syncLoss = useAppStore((state) => state.syncLoss);
  const sourceStatus = useAppStore((state) => state.sourceStatus);
  const head = useId();
  const titleId = useId();

  const { state, count, lose, lost } = syncDiagram({
    frameSync,
    syncLoss,
    sourceStatus,
  });
  const searching = state === "searching";
  const synced = state === "synced";
  // A search that a loss of sync has just started is a fault, as on the rail.
  const searchingTone: Tone = lost ? "bad" : "warn";

  return (
    <svg
      viewBox="0 0 800 148"
      aria-labelledby={titleId}
      className="h-full w-full p-1"
    >
      <title id={titleId}>Frame sync states</title>
      <ArrowHead id={head} />
      <Transition
        path="M210 58 H590"
        head={head}
        label={SYNC_MOVES.sync}
        x={400}
        y={40}
      />
      <Transition
        path="M590 90 H210"
        head={head}
        label={lose}
        x={400}
        y={108}
        fault={lost}
      />
      <StateNode
        {...NODE}
        x={20}
        label="Searching"
        detail={searching ? count : undefined}
        tone={searching ? searchingTone : undefined}
      />
      <StateNode
        {...NODE}
        x={590}
        label="Synced"
        detail={synced ? count : undefined}
        tone={synced ? "good" : undefined}
      />
    </svg>
  );
}
