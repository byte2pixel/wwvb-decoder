import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
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

const overnight = {
  path: String.raw`C:\captures\2026-11-09T0434Z-overnight.edges.txt`,
  recorded: "2026-11-09T04:34:28Z",
  minutes: 524.9,
};

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

  afterEach(() => {
    document.documentElement.classList.remove("dark");
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
    const { bridge, emit } = fakeBridge();
    render(<App bridge={bridge} clock={idleClock()} />);

    emit({
      type: "sourceStatus",
      seq: 1,
      payload: {
        source: "replay",
        name: "replay",
        state: "running",
        detail: "",
        speed: 10,
      },
    });

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

  it("leaves the keys to an open capture list, Esc included", async () => {
    const user = userEvent.setup();
    useAppStore.setState({ captures: [overnight] });
    render(<App bridge={fakeBridge().bridge} clock={idleClock()} />);
    await user.keyboard("3");
    await user.click(screen.getByRole("button", { name: "Replay" }));
    await user.click(screen.getByRole("combobox", { name: "Capture" }));
    const option = await screen.findByRole("option");
    // The focus moves into the list a moment after it opens.
    await waitFor(() => expect(option).toHaveFocus());

    await user.keyboard("2tl{PageDown}{ArrowRight}{Escape}");

    expect(
      await screen.findByRole("combobox", { name: "Capture" }),
    ).toHaveAttribute("aria-expanded", "false");
    expect(useAppStore.getState()).toMatchObject({ focus: 3, drawer: null });
    expect(document.documentElement).not.toHaveClass("dark");
  });

  it("leaves the keys to an open list that has nothing to pick", async () => {
    const user = userEvent.setup();
    render(<App bridge={fakeBridge().bridge} clock={idleClock()} />);
    await user.keyboard("3");
    await user.click(screen.getByRole("button", { name: "Replay" }));
    const capture = screen.getByRole("combobox", { name: "Capture" });
    await user.click(capture);
    await screen.findByRole("option", { name: "No captures found" });
    // With no row to highlight, the focus moves from the button to the list's own box.
    await waitFor(() => expect(capture).not.toHaveFocus());

    await user.keyboard("2tl{PageDown}{ArrowRight}{Escape}");

    expect(
      await screen.findByRole("combobox", { name: "Capture" }),
    ).toHaveAttribute("aria-expanded", "false");
    expect(useAppStore.getState()).toMatchObject({ focus: 3, drawer: null });
    expect(document.documentElement).not.toHaveClass("dark");
  });

  it("leaves the keys to the open signal settings, Esc included", async () => {
    const user = userEvent.setup();
    render(<App bridge={fakeBridge().bridge} clock={idleClock()} />);
    await user.keyboard("3");
    await user.click(screen.getByRole("button", { name: "Signal settings" }));
    const high = await screen.findByRole("button", { name: "High" });
    // The focus moves into the popover a moment after it opens.
    await waitFor(() => expect(high).toHaveFocus());

    await user.keyboard("2tl{PageDown}{ArrowRight}");

    // The arrow key went to the level buttons: it moved the focus from High to Low.
    expect(screen.getByRole("button", { name: "Low" })).toHaveFocus();
    expect(useAppStore.getState()).toMatchObject({ focus: 3, drawer: null });
    expect(document.documentElement).not.toHaveClass("dark");

    await user.keyboard("{Escape}");

    await waitFor(() => expect(high).not.toBeInTheDocument());
    expect(useAppStore.getState().focus).toBe(3);
  });

  it("acts once on an arrow key pressed on a source button", async () => {
    const user = userEvent.setup();
    render(<App bridge={fakeBridge().bridge} clock={idleClock()} />);
    await user.click(screen.getByRole("button", { name: "Replay" }));

    await user.keyboard("{ArrowRight}");

    expect(useAppStore.getState().focus).toBe(1);
    // The key was the page's alone, so the focus did not walk on to Synthetic.
    expect(document.body).toHaveFocus();
    expect(
      screen.getByRole("button", { name: "Replay", pressed: true }),
    ).toBeInTheDocument();
  });

  it("acts on a stage key pressed on a closed list or the settings button, and leaves both closed", async () => {
    const user = userEvent.setup();
    useAppStore.setState({ captures: [overnight] });
    render(<App bridge={fakeBridge().bridge} clock={idleClock()} />);
    await user.click(screen.getByRole("button", { name: "Replay" }));
    const capture = screen.getByRole("combobox", { name: "Capture" });
    const settings = screen.getByRole("button", { name: "Signal settings" });

    capture.focus();
    await user.keyboard("2");
    settings.focus();
    await user.keyboard("{PageDown}");

    expect(useAppStore.getState().focus).toBe(3);
    expect(capture).toHaveAttribute("aria-expanded", "false");
    expect(settings).toHaveAttribute("aria-expanded", "false");

    await user.click(capture);

    expect(await screen.findByRole("option")).toBeInTheDocument();
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
