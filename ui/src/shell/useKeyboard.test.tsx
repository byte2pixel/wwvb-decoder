import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Bridge } from "@/protocol/bridge";
import type { Command, SourceStatus } from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import { useKeyboard } from "./useKeyboard";

const bridge = {
  onMessage: () => () => {},
  send: vi.fn<(command: Command) => void>(),
} satisfies Bridge;

const status = (
  source: SourceStatus["source"],
  state: SourceStatus["state"],
): SourceStatus => ({ source, name: "", state, detail: "", speed: 1 });

/** A page with the map running, and one of each kind of control to press keys in. */
function Page() {
  useKeyboard(bridge);
  return (
    <>
      <input aria-label="Port" />
      <input type="range" aria-label="Noise" />
      <textarea aria-label="Notes" />
      <select aria-label="Capture">
        <option>overnight</option>
      </select>
      <button type="button">Start</button>
      <button type="button" aria-haspopup="listbox" aria-expanded="true">
        Open list
      </button>
      <button type="button" aria-haspopup="listbox" aria-expanded="false">
        Closed list
      </button>
    </>
  );
}

const ui = () => {
  const { focus, drawer } = useAppStore.getState();
  return { focus, drawer };
};

const isDark = () => document.documentElement.classList.contains("dark");

describe("useKeyboard", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
    bridge.send.mockClear();
  });

  afterEach(() => {
    document.documentElement.classList.remove("dark");
  });

  it("focuses a stage on its number and goes back on Esc", async () => {
    const user = userEvent.setup();
    render(<Page />);

    await user.keyboard("3");
    expect(ui().focus).toBe(3);

    await user.keyboard("{Escape}");
    expect(ui().focus).toBeNull();
  });

  it("walks the stages with the arrow keys and a presenter remote's page keys", async () => {
    const user = userEvent.setup();
    render(<Page />);

    await user.keyboard("{ArrowRight}");
    expect(ui().focus).toBe(1);
    await user.keyboard("{PageDown}");
    expect(ui().focus).toBe(2);
    await user.keyboard("{ArrowLeft}");
    expect(ui().focus).toBe(1);
    await user.keyboard("{PageUp}");
    expect(ui().focus).toBeNull();
  });

  it("switches the theme on T", async () => {
    const user = userEvent.setup();
    render(<Page />);

    await user.keyboard("t");
    expect(isDark()).toBe(true);

    await user.keyboard("t");
    expect(isDark()).toBe(false);
  });

  it("starts the noise demo on D, and the picks show it", async () => {
    const user = userEvent.setup();
    useAppStore.setState({
      sourceStatus: status("replay", "running"),
      picks: {
        source: "replay",
        port: "COM5",
        capture: "overnight",
        speed: 10,
        noise: 0.4,
      },
    });
    render(<Page />);

    await user.keyboard("d");

    expect(bridge.send.mock.calls).toEqual([
      [{ command: "selectSource", source: "synthetic", noise: 0.1 }],
    ]);
    // The source and the noise change, and what was picked for the other sources stays.
    expect(useAppStore.getState().picks).toEqual({
      source: "synthetic",
      port: "COM5",
      capture: "overnight",
      speed: 10,
      noise: 0.1,
    });
    // And nothing else on the page does: the view, the drawers, and the theme stay.
    expect(ui()).toEqual({ focus: null, drawer: null });
    expect(isDark()).toBe(false);
  });

  it("switches the noise of a running synthetic source on and off on D, and does not start it again", async () => {
    const user = userEvent.setup();
    useAppStore.setState({ sourceStatus: status("synthetic", "running") });
    render(<Page />);

    await user.keyboard("d");
    expect(useAppStore.getState().picks.noise).toBe(0.1);

    await user.keyboard("D");
    expect(useAppStore.getState().picks.noise).toBe(0);

    await user.keyboard("d");
    expect(bridge.send.mock.calls).toEqual([
      [{ command: "setNoise", noise: 0.1 }],
      [{ command: "setNoise", noise: 0 }],
      [{ command: "setNoise", noise: 0.1 }],
    ]);
  });

  it.each([
    ["has stopped", status("synthetic", "stopped")],
    ["has failed", status("synthetic", "error")],
    ["never ran", null],
  ])(
    "starts the synthetic source on D when it %s, the demo's noise picked or not",
    async (_, sourceStatus) => {
      const user = userEvent.setup();
      useAppStore.setState({ sourceStatus });
      useAppStore.getState().setPicks({ noise: 0.1 });
      render(<Page />);

      await user.keyboard("d");

      expect(bridge.send.mock.calls).toEqual([
        [{ command: "selectSource", source: "synthetic", noise: 0.1 }],
      ]);
    },
  );

  it("opens the protocol log on L and the shortcut list on ?", async () => {
    const user = userEvent.setup();
    render(<Page />);

    await user.keyboard("l");
    expect(ui().drawer).toBe("log");

    await user.keyboard("?");
    expect(ui().drawer).toBe("shortcuts");

    await user.keyboard("?");
    expect(ui().drawer).toBeNull();
  });

  it("still listens while a button has focus", async () => {
    const user = userEvent.setup();
    render(<Page />);

    await user.click(screen.getByRole("button", { name: "Start" }));
    await user.keyboard("2");

    expect(ui().focus).toBe(2);
  });

  it("takes focus off a clicked button when it acts on a key, so no focus ring lights up", async () => {
    const user = userEvent.setup();
    render(<Page />);
    const start = screen.getByRole("button", { name: "Start" });

    await user.click(start);
    expect(start).toHaveFocus();
    await user.keyboard("2");

    expect(start).not.toHaveFocus();
  });

  it("leaves the focus where it is on a key it does not act on", async () => {
    const user = userEvent.setup();
    render(<Page />);
    const start = screen.getByRole("button", { name: "Start" });

    await user.click(start);
    await user.keyboard("a");

    expect(start).toHaveFocus();
  });

  it.each(["Port", "Noise", "Notes", "Capture"])(
    "leaves a key pressed in the %s control to the control",
    async (name) => {
      const user = userEvent.setup();
      render(<Page />);

      screen.getByLabelText(name).focus();
      await user.keyboard("2tld{ArrowRight}");

      expect(ui()).toEqual({ focus: null, drawer: null });
      expect(isDark()).toBe(false);
      expect(bridge.send).not.toHaveBeenCalled();
    },
  );

  it("leaves a key pressed on the button of an open list to the list", async () => {
    const user = userEvent.setup();
    render(<Page />);

    screen.getByRole("button", { name: "Open list" }).focus();
    await user.keyboard("2tld{ArrowRight}{Escape}");

    expect(ui()).toEqual({ focus: null, drawer: null });
    expect(isDark()).toBe(false);
    expect(bridge.send).not.toHaveBeenCalled();
  });

  it("still listens while the button of a closed list has focus", async () => {
    const user = userEvent.setup();
    render(<Page />);

    screen.getByRole("button", { name: "Closed list" }).focus();
    await user.keyboard("2");

    expect(ui().focus).toBe(2);
  });

  it.each(["Control", "Alt", "Meta"])(
    "leaves a key pressed with %s to the browser",
    async (modifier) => {
      const user = userEvent.setup();
      render(<Page />);

      await user.keyboard(`{${modifier}>}l1d{/${modifier}}`);

      expect(ui()).toEqual({ focus: null, drawer: null });
      expect(bridge.send).not.toHaveBeenCalled();
    },
  );

  it("acts once on a key that is held down", () => {
    render(<Page />);

    fireEvent.keyDown(document.body, { key: "t" });
    fireEvent.keyDown(document.body, { key: "t", repeat: true });
    fireEvent.keyDown(document.body, { key: "t", repeat: true });

    expect(isDark()).toBe(true);
  });

  it("keeps a key it acts on from scrolling the page, and lets the others through", () => {
    render(<Page />);

    // fireEvent returns false when a listener called preventDefault.
    expect(fireEvent.keyDown(document.body, { key: "PageDown" })).toBe(false);
    expect(fireEvent.keyDown(document.body, { key: "ArrowDown" })).toBe(true);
  });

  it("stops listening when the page unmounts", async () => {
    const user = userEvent.setup();
    const { unmount } = render(<Page />);

    unmount();
    await user.keyboard("3");

    expect(ui().focus).toBeNull();
  });
});
