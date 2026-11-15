import { act, render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { Bridge } from "@/protocol/bridge";
import type { Command, HostMessage } from "@/protocol/messages";
import { initialState } from "@/state/appState";
import { useAppStore } from "@/state/store";
import { createSourceClock } from "@/time/sourceClock";
import App from "./App";

function fakeBridge() {
  let handler: ((message: HostMessage) => void) | null = null;
  const bridge = {
    onMessage: (next: (message: HostMessage) => void) => {
      handler = next;
      return () => {
        handler = null;
      };
    },
    send: vi.fn<(command: Command) => void>(),
  } satisfies Bridge;

  return {
    bridge,
    /** Delivers a message as the host would. */
    emit: (message: HostMessage) => act(() => handler?.(message)),
    isSubscribed: () => handler !== null,
  };
}

/** A clock no edge has synced: it has a speed and no present. */
const idleClock = () => createSourceClock(() => 0);

describe("App", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(initialState);
  });

  it("lays out the bar, the rail, five panels, and the status bar", () => {
    render(<App bridge={fakeBridge().bridge} clock={idleClock()} />);

    expect(
      screen.getByRole("heading", { level: 1, name: "WWVB decoder" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("navigation", { name: "Pipeline" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("main")).toBeInTheDocument();
    expect(screen.getByRole("contentinfo")).toBeInTheDocument();
    for (const title of [
      "Signal",
      "Pulse width",
      "Frame",
      "Decoded frames",
      "Time",
    ]) {
      expect(screen.getByRole("region", { name: title })).toBeInTheDocument();
    }
  });

  it("shows the panels in their no-data states", () => {
    render(<App bridge={fakeBridge().bridge} clock={idleClock()} />);

    expect(
      screen.getByRole("region", { name: "Decoded frames" }),
    ).toHaveTextContent("No frames yet");
    const time = screen.getByRole("region", { name: "Time" });
    expect(time).toHaveTextContent("--:--:--");
    expect(time).toHaveTextContent("No signal");
  });

  it("says hello to the host and feeds its messages to the store", () => {
    const { bridge, emit } = fakeBridge();
    render(<App bridge={bridge} clock={idleClock()} />);

    expect(bridge.send).toHaveBeenCalledWith({ command: "hello", protocol: 1 });

    emit({
      type: "edges",
      seq: 1,
      payload: { items: [{ micros: 2_617_209_411, level: "low" }] },
    });

    expect(screen.getByRole("button", { name: /Edges/ })).toHaveTextContent(
      "L 2617209411",
    );
  });

  it("stops listening to the bridge when it unmounts", () => {
    const { bridge, isSubscribed } = fakeBridge();
    const { unmount } = render(<App bridge={bridge} clock={idleClock()} />);

    expect(isSubscribed()).toBe(true);
    unmount();
    expect(isSubscribed()).toBe(false);
  });

  it("shows the signal view, and names its window in the panel's header", () => {
    render(<App bridge={fakeBridge().bridge} clock={idleClock()} />);

    const signal = screen.getByRole("region", { name: "Signal" });
    expect(within(signal).getByText(/drawn in four lanes/).tagName).toBe(
      "CANVAS",
    );
    expect(signal).toHaveTextContent("12 s window");
  });

  it("names the 60 s window when the source runs faster than 1x", () => {
    const clock = idleClock();
    clock.setSpeed(10);
    render(<App bridge={fakeBridge().bridge} clock={clock} />);

    expect(screen.getByRole("region", { name: "Signal" })).toHaveTextContent(
      "60 s window",
    );
  });

  it("shows the pulse-width view, and the last pulse in the panel's header", () => {
    const { bridge, emit } = fakeBridge();
    render(<App bridge={bridge} clock={idleClock()} />);

    const panel = screen.getByRole("region", { name: "Pulse width" });
    expect(panel).toHaveTextContent("No pulses yet");
    expect(
      within(panel).getByRole("list", { name: "Bins" }),
    ).toBeInTheDocument();

    emit({
      type: "symbol",
      seq: 1,
      payload: { startMicros: 0, durationMicros: 502_000, kind: "one" },
    });

    expect(panel).toHaveTextContent("Last pulse 502 ms classified as 1");
    expect(within(panel).getByTitle("500 to 525 ms: 1")).toBeInTheDocument();
  });
});
