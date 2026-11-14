import { beforeEach, describe, expect, it, vi } from "vitest";
import type { HostMessage } from "@/protocol/messages";
import { initialState } from "./appState";
import { useAppStore } from "./store";

const info = (seq: number): HostMessage => ({
  type: "info",
  seq,
  payload: { line: "#" },
});

describe("useAppStore", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(initialState);
  });

  it("starts in the initial state", () => {
    expect(useAppStore.getState()).toMatchObject(initialState);
  });

  it("applies the reducer when a message is dispatched", () => {
    useAppStore.getState().dispatch(info(1));

    expect(useAppStore.getState().infoLines).toEqual(["#"]);
    expect(useAppStore.getState().lastSeq).toBe(1);
  });

  it("notifies subscribers once per message", () => {
    const listener = vi.fn();
    const unsubscribe = useAppStore.subscribe(listener);

    useAppStore.getState().dispatch(info(1));
    unsubscribe();
    useAppStore.getState().dispatch(info(2));

    expect(listener).toHaveBeenCalledTimes(1);
  });
});
