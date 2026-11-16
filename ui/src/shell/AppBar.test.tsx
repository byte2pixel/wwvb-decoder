import { fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Bridge } from "@/protocol/bridge";
import type { Command } from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import { AppBar } from "./AppBar";

const fakeBridge = () =>
  ({
    onMessage: () => () => {},
    send: vi.fn<(command: Command) => void>(),
  }) satisfies Bridge;

describe("AppBar", () => {
  afterEach(() => {
    document.documentElement.classList.remove("dark");
  });

  it("offers the three sources with only Synthetic available", () => {
    render(<AppBar bridge={fakeBridge()} />);

    const source = screen.getByRole("group", { name: "Source" });

    expect(within(source).getByRole("button", { name: "Live" })).toBeDisabled();
    expect(
      within(source).getByRole("button", { name: "Replay" }),
    ).toBeDisabled();
    expect(
      within(source).getByRole("button", { name: "Synthetic", pressed: true }),
    ).toBeEnabled();
  });

  it("starts the synthetic source with no noise", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    render(<AppBar bridge={bridge} />);

    await user.click(screen.getByRole("button", { name: "Start" }));

    expect(bridge.send).toHaveBeenCalledWith({
      command: "selectSource",
      source: "synthetic",
      noise: 0,
    });
  });

  it("starts with the noise the slider is set to", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    render(<AppBar bridge={bridge} />);

    // The slider's real control is an <input type="range"> inside the thumb.
    const slider = screen.getByLabelText("Noise", { selector: "input" });
    fireEvent.change(slider, { target: { value: "0.4" } });
    await user.click(screen.getByRole("button", { name: "Start" }));

    expect(screen.getByText("0.40")).toBeInTheDocument();
    expect(bridge.send).toHaveBeenCalledWith({
      command: "selectSource",
      source: "synthetic",
      noise: 0.4,
    });
  });

  it("stops the source", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    render(<AppBar bridge={bridge} />);

    await user.click(screen.getByRole("button", { name: "Stop" }));

    expect(bridge.send).toHaveBeenCalledWith({ command: "stop" });
  });

  it("switches between the dark and light themes", async () => {
    const user = userEvent.setup();
    document.documentElement.classList.add("dark");
    render(<AppBar bridge={fakeBridge()} />);
    const theme = screen.getByRole("button", { name: "Switch theme" });

    await user.click(theme);
    expect(document.documentElement).not.toHaveClass("dark");

    await user.click(theme);
    expect(document.documentElement).toHaveClass("dark");
  });

  it("asks for the shortcut list", async () => {
    const user = userEvent.setup();
    useAppStore.setState({ drawer: null });
    render(<AppBar bridge={fakeBridge()} />);

    await user.click(
      screen.getByRole("button", { name: "Keyboard shortcuts" }),
    );

    expect(useAppStore.getState().drawer).toBe("shortcuts");
  });
});
