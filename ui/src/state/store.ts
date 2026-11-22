import { create } from "zustand";
import type { HostMessage, PinLevel, Selection } from "@/protocol/messages";
import {
  expireSyncLoss,
  initialState,
  isSourceReset,
  reduce,
  type AppState,
} from "./appState";

const syncLossHoldMs = 5_000;

/** A sheet that slides over the page. One is open at a time. */
export type Drawer = "log" | "shortcuts";

/**
 * What the page decides for itself. No host message changes it, so it is not in the
 * reducer's state, and a source reset leaves it alone.
 */
export interface UiState {
  /** The stage whose focus view is open, 1 to 7. Null is the overview. */
  focus: number | null;
  /** The drawer that is open, if one is. */
  drawer: Drawer | null;
}

/**
 * What the app bar has picked for the next Start. A port or a capture is null until one is
 * picked, and the first port and the newest capture then count as picked.
 */
export type Picks = Omit<Selection, "reducedCarrierLevel">;

/** The host's values when it remembers nothing. */
const defaultPicks: Picks = {
  source: "synthetic",
  port: null,
  capture: null,
  speed: 1,
  noise: 0,
};

interface AppStore extends AppState, UiState {
  /** The pin level that means reduced carrier. The page picks it, high is the host's default. */
  reducedLevel: PinLevel;
  picks: Picks;
  /** The warning the presenter closed. The banner stays away while the host sends this text. */
  closedWarning: string | null;
  /**
   * The frame picked in the stage 6 table, by its number since the source reset. Null follows
   * the newest frame.
   */
  selectedFrame: number | null;
  /** Feeds one host message through the reducer. */
  dispatch: (message: HostMessage) => void;
  setFocus: (focus: number | null) => void;
  setDrawer: (drawer: Drawer | null) => void;
  setReducedLevel: (level: PinLevel) => void;
  /** Changes some of the picks and keeps the rest. */
  setPicks: (change: Partial<Picks>) => void;
  closeWarning: (warning: string) => void;
  selectFrame: (frame: number | null) => void;
}

/**
 * The two messages that reach the page's own state. `selection` sets the picks and the level.
 * A status without a warning forgets the warning that was closed, so the same text shows
 * again when it comes back. A source reset forgets the selected frame, because the frames are
 * numbered from 1 again.
 */
const fromHost = (
  message: HostMessage,
): Partial<
  Pick<AppStore, "picks" | "reducedLevel" | "closedWarning" | "selectedFrame">
> => {
  if (message.type === "selection") {
    const { reducedCarrierLevel, ...picks } = message.payload;
    return { picks, reducedLevel: reducedCarrierLevel };
  }
  if (message.type === "sourceStatus") {
    return {
      ...(message.payload.warning ? {} : { closedWarning: null }),
      ...(isSourceReset(message.payload) ? { selectedFrame: null } : {}),
    };
  }
  return {};
};

/**
 * The page's one store. Components subscribe to slices of it; canvas views read
 * `useAppStore.getState()` on each animation frame, outside React.
 */
export const useAppStore = create<AppStore>()((set) => ({
  ...initialState,
  focus: null,
  drawer: null,
  reducedLevel: "high",
  picks: defaultPicks,
  closedWarning: null,
  selectedFrame: null,
  // set() merges, so what the reducer does not return survives every update, a source
  // reset included: dispatch and the setters, the focus, the drawer, and the picks.
  dispatch: (message) => {
    set((state) => ({ ...reduce(state, message), ...fromHost(message) }));
    if (message.type === "frameSync" && message.payload.kind === "syncLost") {
      // The timer's id is not kept: expireSyncLoss ignores a timer whose loss was replaced or reset.
      setTimeout(
        () => set((state) => expireSyncLoss(state, message.seq)),
        syncLossHoldMs,
      );
    }
  },
  setFocus: (focus) => set({ focus }),
  setDrawer: (drawer) => set({ drawer }),
  setReducedLevel: (reducedLevel) => set({ reducedLevel }),
  setPicks: (change) =>
    set((state) => ({ picks: { ...state.picks, ...change } })),
  closeWarning: (closedWarning) => set({ closedWarning }),
  selectFrame: (selectedFrame) => set({ selectedFrame }),
}));
