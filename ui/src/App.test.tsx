import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { Bridge } from "@/protocol/bridge";
import type { Command, HostMessage } from "@/protocol/messages";
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

/** The overview's panels, in document order. */
const overviewPanels = [
  "Signal",
  "Pulse width",
  "Frame",
  "Time",
  "Decoded frames",
];

/** The titles of the panels on screen, in document order. */
const panels = (): (string | null)[] =>
  screen
    .getAllByRole("region")
    .map((panel) => within(panel).getByRole("heading").textContent);

describe("App", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
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
    for (const title of overviewPanels) {
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
    expect(within(signal).getByText(/drawn in lanes/).tagName).toBe("CANVAS");
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

  it("opens a stage's focus view on its key, in place of the panels, and goes back on Esc", async () => {
    const user = userEvent.setup();
    render(<App bridge={fakeBridge().bridge} clock={idleClock()} />);

    await user.keyboard("2");

    expect(
      screen.getByRole("heading", { level: 2, name: "Pulses" }),
    ).toBeInTheDocument();
    expect(screen.getByText("IStage<Edge, Pulse>")).toBeInTheDocument();
    expect(panels()).toEqual(["Signal"]);
    // The bar, the rail, and the status bar stay, and the rail highlights the stage.
    expect(
      screen.getByRole("heading", { level: 1, name: "WWVB decoder" }),
    ).toBeInTheDocument();
    const rail = screen.getByRole("navigation", { name: "Pipeline" });
    expect(
      within(rail).getByRole("button", { pressed: true }),
    ).toHaveTextContent("Pulses");
    expect(screen.getByRole("contentinfo")).toBeInTheDocument();

    await user.keyboard("{Escape}");

    expect(panels()).toEqual(overviewPanels);
  });

  it("opens a stage's focus view from the rail", async () => {
    const user = userEvent.setup();
    render(<App bridge={fakeBridge().bridge} clock={idleClock()} />);

    await user.click(screen.getByRole("button", { name: /Edges/ }));

    expect(screen.getByText("IEdgeSource")).toBeInTheDocument();
    expect(panels()).toEqual(["Signal", "Edge log"]);
  });

  it("closes a drawer on Esc and keeps the focus view behind it", async () => {
    const user = userEvent.setup();
    render(<App bridge={fakeBridge().bridge} clock={idleClock()} />);
    await user.keyboard("3l");
    expect(
      screen.getByRole("dialog", { name: "Protocol log" }),
    ).toBeInTheDocument();

    await user.keyboard("{Escape}");

    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
    expect(useAppStore.getState().focus).toBe(3);
  });

  it("opens the shortcut list on ? and from the app bar", async () => {
    const user = userEvent.setup();
    render(<App bridge={fakeBridge().bridge} clock={idleClock()} />);

    await user.keyboard("?");
    expect(
      screen.getByRole("dialog", { name: "Keyboard shortcuts" }),
    ).toBeInTheDocument();

    await user.keyboard("?");
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );

    await user.click(
      screen.getByRole("button", { name: "Keyboard shortcuts" }),
    );
    expect(
      screen.getByRole("dialog", { name: "Keyboard shortcuts" }),
    ).toBeInTheDocument();
  });

  it("leaves a key pressed in the noise slider to the slider", async () => {
    const user = userEvent.setup();
    render(<App bridge={fakeBridge().bridge} clock={idleClock()} />);

    // The slider's real control is an <input type="range"> inside the thumb.
    screen.getByLabelText("Noise", { selector: "input" }).focus();
    await user.keyboard("2{ArrowRight}");

    expect(panels()).toEqual(overviewPanels);
  });
});
