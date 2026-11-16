import { create } from "zustand";
import type { HostMessage } from "@/protocol/messages";
import {
  expireSyncLoss,
  initialState,
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

interface AppStore extends AppState, UiState {
  /** Feeds one host message through the reducer. */
  dispatch: (message: HostMessage) => void;
  setFocus: (focus: number | null) => void;
  setDrawer: (drawer: Drawer | null) => void;
}

/**
 * The page's one store. Components subscribe to slices of it; canvas views read
 * `useAppStore.getState()` on each animation frame, outside React.
 */
export const useAppStore = create<AppStore>()((set) => ({
  ...initialState,
  focus: null,
  drawer: null,
  // set() merges, so what the reducer does not return survives every update, a source
  // reset included: dispatch and the setters, the focus, and the drawer.
  dispatch: (message) => {
    set((state) => reduce(state, message));
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
}));
