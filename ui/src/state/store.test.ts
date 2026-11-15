import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
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

const syncLost = (seq: number): HostMessage => ({
  type: "frameSync",
  seq,
  payload: {
    kind: "syncLost",
    reason: "expected marker at second 9, got Zero",
  },
});

describe("sync loss hold", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    useAppStore.setState(initialState);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("forgets a sync loss 5 s after it arrived", () => {
    useAppStore.getState().dispatch(syncLost(1));

    vi.advanceTimersByTime(4_999);
    expect(useAppStore.getState().syncLoss).not.toBeNull();

    vi.advanceTimersByTime(1);
    expect(useAppStore.getState().syncLoss).toBeNull();
  });

  it("holds a second loss for its own 5 s", () => {
    useAppStore.getState().dispatch(syncLost(1));
    vi.advanceTimersByTime(2_000);
    useAppStore.getState().dispatch(syncLost(2));

    vi.advanceTimersByTime(3_000); // the first loss's timer fires here
    expect(useAppStore.getState().syncLoss?.seq).toBe(2);

    vi.advanceTimersByTime(2_000);
    expect(useAppStore.getState().syncLoss).toBeNull();
  });
});
