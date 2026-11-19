import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { useAppStore } from "@/state/store";
import { useKeyboard } from "./useKeyboard";

/** A page with the map running, and one of each kind of control to press keys in. */
function Page() {
  useKeyboard();
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
      await user.keyboard("2tl{ArrowRight}");

      expect(ui()).toEqual({ focus: null, drawer: null });
      expect(isDark()).toBe(false);
    },
  );

  it("leaves a key pressed on the button of an open list to the list", async () => {
    const user = userEvent.setup();
    render(<Page />);

    screen.getByRole("button", { name: "Open list" }).focus();
    await user.keyboard("2tl{ArrowRight}{Escape}");

    expect(ui()).toEqual({ focus: null, drawer: null });
    expect(isDark()).toBe(false);
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

      await user.keyboard(`{${modifier}>}l1{/${modifier}}`);

      expect(ui()).toEqual({ focus: null, drawer: null });
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
