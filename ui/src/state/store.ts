import { create } from "zustand";
import type { HostMessage } from "../protocol/messages.ts";
import { initialState, reduce, type AppState } from "./appState.ts";

interface AppStore extends AppState {
  /** Feeds one host message through the reducer. */
  dispatch: (message: HostMessage) => void;
}

/**
 * The page's one store. Components subscribe to slices of it; canvas views read
 * `useAppStore.getState()` on each animation frame, outside React.
 */
export const useAppStore = create<AppStore>()((set) => ({
  ...initialState,
  // set() merges, so dispatch itself survives every update, a source reset included.
  dispatch: (message) => set((state) => reduce(state, message)),
}));
