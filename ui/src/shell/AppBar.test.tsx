import { act, fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Bridge } from "@/protocol/bridge";
import type { Command, SourceStatus } from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import { AppBar } from "./AppBar";

const overnight = {
  path: String.raw`C:\captures\2026-11-09T0434Z-overnight.edges.txt`,
  recorded: "2026-11-09T04:34:28Z",
  minutes: 524.9,
};
const desk = {
  path: String.raw`C:\captures\2026-11-08T2300Z-desk.edges.txt`,
  recorded: "2026-11-08T23:00:15Z",
  minutes: 3.2,
};

const status = (
  source: SourceStatus["source"],
  state: SourceStatus["state"],
): SourceStatus => ({ source, name: "", state, detail: "", speed: 1 });

const fakeBridge = () =>
  ({
    onMessage: () => () => {},
    send: vi.fn<(command: Command) => void>(),
  }) satisfies Bridge;

describe("AppBar", () => {
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });
  afterEach(() => {
    document.documentElement.classList.remove("dark");
  });

  it("offers the three sources with Synthetic selected", () => {
    render(<AppBar bridge={fakeBridge()} />);

    const source = screen.getByRole("group", { name: "Source" });

    expect(
      within(source).getByRole("button", { name: "Live", pressed: false }),
    ).toBeEnabled();
    expect(
      within(source).getByRole("button", { name: "Replay", pressed: false }),
    ).toBeEnabled();
    expect(
      within(source).getByRole("button", { name: "Synthetic", pressed: true }),
    ).toBeEnabled();
  });

  it("shows the noise control for the synthetic source only", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    render(<AppBar bridge={bridge} />);
    fireEvent.change(screen.getByLabelText("Noise", { selector: "input" }), {
      target: { value: "0.4" },
    });

    await user.click(screen.getByRole("button", { name: "Replay" }));

    expect(
      screen.getByRole("button", { name: "Replay", pressed: true }),
    ).toBeInTheDocument();
    expect(
      screen.queryByLabelText("Noise", { selector: "input" }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText("0.40")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Synthetic" }));

    expect(screen.getByText("0.40")).toBeInTheDocument();
    expect(bridge.send).not.toHaveBeenCalled();
  });

  it("keeps a source selected when its button is pressed again", async () => {
    const user = userEvent.setup();
    render(<AppBar bridge={fakeBridge()} />);

    await user.click(screen.getByRole("button", { name: "Synthetic" }));

    expect(
      screen.getByRole("button", { name: "Synthetic", pressed: true }),
    ).toBeInTheDocument();
    expect(
      screen.getByLabelText("Noise", { selector: "input" }),
    ).toBeInTheDocument();
  });

  it("starts the newest capture when none was picked", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    useAppStore.setState({ captures: [overnight, desk] });
    render(<AppBar bridge={bridge} />);

    await user.click(screen.getByRole("button", { name: "Replay" }));

    expect(screen.getByRole("combobox", { name: "Capture" })).toHaveTextContent(
      "2026-11-09 04:34 UTC · 8 h 45 min",
    );

    await user.click(screen.getByRole("button", { name: "Start" }));

    expect(bridge.send).toHaveBeenCalledExactlyOnceWith({
      command: "selectSource",
      source: "replay",
      path: overnight.path,
      speed: 1,
    });
  });

  it("asks for the captures when the list opens", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    useAppStore.setState({ captures: [overnight, desk] });
    render(<AppBar bridge={bridge} />);
    await user.click(screen.getByRole("button", { name: "Replay" }));

    await user.click(screen.getByRole("combobox", { name: "Capture" }));

    const options = await screen.findAllByRole("option");
    expect(options.map((option) => option.textContent)).toEqual([
      "2026-11-09 04:34 UTC · 8 h 45 min",
      "2026-11-08 23:00 UTC · 3 min",
    ]);
    expect(bridge.send).toHaveBeenCalledExactlyOnceWith({
      command: "listCaptures",
    });
  });

  it("starts the capture picked from the list", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    useAppStore.setState({ captures: [overnight, desk] });
    render(<AppBar bridge={bridge} />);
    await user.click(screen.getByRole("button", { name: "Replay" }));

    await user.click(screen.getByRole("combobox", { name: "Capture" }));
    await user.click(
      await screen.findByRole("option", {
        name: "2026-11-08 23:00 UTC · 3 min",
      }),
    );
    await user.click(await screen.findByRole("button", { name: "Start" }));

    expect(screen.getByRole("combobox", { name: "Capture" })).toHaveTextContent(
      "2026-11-08 23:00 UTC · 3 min",
    );
    expect(bridge.send).toHaveBeenLastCalledWith({
      command: "selectSource",
      source: "replay",
      path: desk.path,
      speed: 1,
    });
  });

  it("starts the picked capture again when it is back in the list", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    useAppStore.setState({ captures: [overnight, desk] });
    render(<AppBar bridge={bridge} />);
    await user.click(screen.getByRole("button", { name: "Replay" }));
    await user.click(screen.getByRole("combobox", { name: "Capture" }));
    await user.click(
      await screen.findByRole("option", {
        name: "2026-11-08 23:00 UTC · 3 min",
      }),
    );

    act(() => useAppStore.setState({ captures: [overnight] }));

    expect(
      await screen.findByRole("combobox", { name: "Capture" }),
    ).toHaveTextContent("2026-11-09 04:34 UTC · 8 h 45 min");

    act(() => useAppStore.setState({ captures: [overnight, desk] }));
    await user.click(screen.getByRole("button", { name: "Start" }));

    expect(screen.getByRole("combobox", { name: "Capture" })).toHaveTextContent(
      "2026-11-08 23:00 UTC · 3 min",
    );
    expect(bridge.send).toHaveBeenLastCalledWith({
      command: "selectSource",
      source: "replay",
      path: desk.path,
      speed: 1,
    });
  });

  it("keeps the capture when a key is typed on the closed list", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    useAppStore.setState({ captures: [overnight, desk] });
    render(<AppBar bridge={bridge} />);
    await user.click(screen.getByRole("button", { name: "Replay" }));

    screen.getByRole("combobox", { name: "Capture" }).focus();
    await user.keyboard("2");
    await user.click(screen.getByRole("button", { name: "Start" }));

    expect(screen.getByRole("combobox", { name: "Capture" })).toHaveTextContent(
      "2026-11-09 04:34 UTC · 8 h 45 min",
    );
    expect(bridge.send).toHaveBeenCalledExactlyOnceWith({
      command: "selectSource",
      source: "replay",
      path: overnight.path,
      speed: 1,
    });
  });

  it("cannot start a replay while no capture is listed", async () => {
    const user = userEvent.setup();
    render(<AppBar bridge={fakeBridge()} />);
    await user.click(screen.getByRole("button", { name: "Replay" }));

    expect(screen.getByRole("combobox", { name: "Capture" })).toHaveTextContent(
      "No captures found",
    );
    expect(screen.getByRole("button", { name: "Start" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Stop" })).toBeEnabled();

    await user.click(screen.getByRole("combobox", { name: "Capture" }));

    expect(
      await screen.findByRole("option", { name: "No captures found" }),
    ).toHaveAttribute("aria-disabled", "true");
  });

  it("starts the first port listed when none was picked", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    useAppStore.setState({ ports: ["COM3", "COM7"] });
    render(<AppBar bridge={bridge} />);

    await user.click(screen.getByRole("button", { name: "Live" }));

    expect(screen.getByRole("combobox", { name: "Port" })).toHaveTextContent(
      "COM3",
    );

    await user.click(screen.getByRole("button", { name: "Start" }));

    expect(bridge.send).toHaveBeenCalledExactlyOnceWith({
      command: "selectSource",
      source: "serial",
      port: "COM3",
    });
  });

  it("asks for the ports when the list opens", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    useAppStore.setState({ ports: ["COM3", "COM7"] });
    render(<AppBar bridge={bridge} />);
    await user.click(screen.getByRole("button", { name: "Live" }));

    await user.click(screen.getByRole("combobox", { name: "Port" }));

    const options = await screen.findAllByRole("option");
    expect(options.map((option) => option.textContent)).toEqual([
      "COM3",
      "COM7",
    ]);
    expect(bridge.send).toHaveBeenCalledExactlyOnceWith({
      command: "listPorts",
    });
  });

  it("starts the port picked from the list", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    useAppStore.setState({ ports: ["COM3", "COM7"] });
    render(<AppBar bridge={bridge} />);
    await user.click(screen.getByRole("button", { name: "Live" }));

    await user.click(screen.getByRole("combobox", { name: "Port" }));
    await user.click(await screen.findByRole("option", { name: "COM7" }));
    await user.click(await screen.findByRole("button", { name: "Start" }));

    expect(screen.getByRole("combobox", { name: "Port" })).toHaveTextContent(
      "COM7",
    );
    expect(bridge.send).toHaveBeenLastCalledWith({
      command: "selectSource",
      source: "serial",
      port: "COM7",
    });
  });

  it("starts the first port when the picked one leaves the list", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    useAppStore.setState({ ports: ["COM3", "COM7"] });
    render(<AppBar bridge={bridge} />);
    await user.click(screen.getByRole("button", { name: "Live" }));
    await user.click(screen.getByRole("combobox", { name: "Port" }));
    await user.click(await screen.findByRole("option", { name: "COM7" }));

    act(() => useAppStore.setState({ ports: ["COM1", "COM3"] }));
    await user.click(await screen.findByRole("button", { name: "Start" }));

    expect(screen.getByRole("combobox", { name: "Port" })).toHaveTextContent(
      "COM1",
    );
    expect(bridge.send).toHaveBeenLastCalledWith({
      command: "selectSource",
      source: "serial",
      port: "COM1",
    });
  });

  it("starts the picked port again when it is back in the list", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    useAppStore.setState({ ports: ["COM3", "COM7"] });
    render(<AppBar bridge={bridge} />);
    await user.click(screen.getByRole("button", { name: "Live" }));
    await user.click(screen.getByRole("combobox", { name: "Port" }));
    await user.click(await screen.findByRole("option", { name: "COM7" }));

    act(() => useAppStore.setState({ ports: ["COM3"] }));

    expect(
      await screen.findByRole("combobox", { name: "Port" }),
    ).toHaveTextContent("COM3");

    act(() => useAppStore.setState({ ports: ["COM3", "COM7"] }));
    await user.click(screen.getByRole("button", { name: "Start" }));

    expect(screen.getByRole("combobox", { name: "Port" })).toHaveTextContent(
      "COM7",
    );
    expect(bridge.send).toHaveBeenLastCalledWith({
      command: "selectSource",
      source: "serial",
      port: "COM7",
    });
  });

  it("cannot start Live while no port is listed", async () => {
    const user = userEvent.setup();
    render(<AppBar bridge={fakeBridge()} />);
    await user.click(screen.getByRole("button", { name: "Live" }));

    expect(screen.getByRole("combobox", { name: "Port" })).toHaveTextContent(
      "No ports found",
    );
    expect(screen.getByRole("button", { name: "Start" })).toBeDisabled();

    await user.click(screen.getByRole("combobox", { name: "Port" }));

    expect(
      await screen.findByRole("option", { name: "No ports found" }),
    ).toHaveAttribute("aria-disabled", "true");
  });

  it("offers the replay speeds with 1× selected", async () => {
    const user = userEvent.setup();
    render(<AppBar bridge={fakeBridge()} />);

    expect(
      screen.queryByRole("group", { name: "Speed" }),
    ).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Replay" }));

    const speed = screen.getByRole("group", { name: "Speed" });
    expect(
      within(speed)
        .getAllByRole("button")
        .map((button) => [button.textContent, button.ariaPressed]),
    ).toEqual([
      ["1×", "true"],
      ["10×", "false"],
      ["60×", "false"],
    ]);
  });

  it("starts the replay at the speed picked", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    useAppStore.setState({ captures: [overnight, desk] });
    render(<AppBar bridge={bridge} />);
    await user.click(screen.getByRole("button", { name: "Replay" }));

    await user.click(screen.getByRole("button", { name: "10×" }));
    await user.click(screen.getByRole("button", { name: "Start" }));

    expect(
      screen.getByRole("button", { name: "10×", pressed: true }),
    ).toBeInTheDocument();
    expect(bridge.send).toHaveBeenCalledExactlyOnceWith({
      command: "selectSource",
      source: "replay",
      path: overnight.path,
      speed: 10,
    });
  });

  it("changes the speed of a running replay", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    useAppStore.setState({ sourceStatus: status("replay", "running") });
    render(<AppBar bridge={bridge} />);
    await user.click(screen.getByRole("button", { name: "Replay" }));

    await user.click(screen.getByRole("button", { name: "60×" }));

    expect(bridge.send).toHaveBeenCalledExactlyOnceWith({
      command: "setReplaySpeed",
      speed: 60,
    });
  });

  it.each([
    ["a stopped replay", status("replay", "stopped")],
    ["a failed replay", status("replay", "error")],
    ["another source running", status("synthetic", "running")],
  ])("sends no speed to %s", async (_, sourceStatus) => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    useAppStore.setState({ sourceStatus });
    render(<AppBar bridge={bridge} />);
    await user.click(screen.getByRole("button", { name: "Replay" }));

    await user.click(screen.getByRole("button", { name: "10×" }));

    expect(bridge.send).not.toHaveBeenCalled();
  });

  it("keeps a speed selected when its button is pressed again", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    useAppStore.setState({ sourceStatus: status("replay", "running") });
    render(<AppBar bridge={bridge} />);
    await user.click(screen.getByRole("button", { name: "Replay" }));

    await user.click(screen.getByRole("button", { name: "1×" }));

    expect(
      screen.getByRole("button", { name: "1×", pressed: true }),
    ).toBeInTheDocument();
    expect(bridge.send).not.toHaveBeenCalled();
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

  it("sets the noise of a running synthetic source", () => {
    const bridge = fakeBridge();
    useAppStore.setState({ sourceStatus: status("synthetic", "running") });
    render(<AppBar bridge={bridge} />);

    fireEvent.change(screen.getByLabelText("Noise", { selector: "input" }), {
      target: { value: "0.4" },
    });

    expect(screen.getByText("0.40")).toBeInTheDocument();
    expect(bridge.send).toHaveBeenCalledExactlyOnceWith({
      command: "setNoise",
      noise: 0.4,
    });
  });

  it.each([
    ["a stopped synthetic source", status("synthetic", "stopped")],
    ["a failed synthetic source", status("synthetic", "error")],
    ["another source running", status("replay", "running")],
  ])("sends no noise to %s", (_, sourceStatus) => {
    const bridge = fakeBridge();
    useAppStore.setState({ sourceStatus });
    render(<AppBar bridge={bridge} />);

    fireEvent.change(screen.getByLabelText("Noise", { selector: "input" }), {
      target: { value: "0.4" },
    });

    expect(screen.getByText("0.40")).toBeInTheDocument();
    expect(bridge.send).not.toHaveBeenCalled();
  });

  it("stops the source", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    render(<AppBar bridge={bridge} />);

    await user.click(screen.getByRole("button", { name: "Stop" }));

    expect(bridge.send).toHaveBeenCalledWith({ command: "stop" });
  });

  it("opens the signal settings with high as the reduced-carrier level", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    render(<AppBar bridge={bridge} />);
    expect(
      screen.queryByRole("group", { name: "Reduced carrier level" }),
    ).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Signal settings" }));

    const level = await screen.findByRole("group", {
      name: "Reduced carrier level",
    });
    expect(
      within(level)
        .getAllByRole("button")
        .map((button) => [button.textContent, button.ariaPressed]),
    ).toEqual([
      ["High", "true"],
      ["Low", "false"],
    ]);
    expect(bridge.send).not.toHaveBeenCalled();
  });

  it("sets the reduced-carrier level", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    render(<AppBar bridge={bridge} />);
    await user.click(screen.getByRole("button", { name: "Signal settings" }));

    await user.click(await screen.findByRole("button", { name: "Low" }));

    expect(
      screen.getByRole("button", { name: "Low", pressed: true }),
    ).toBeInTheDocument();
    expect(useAppStore.getState().reducedLevel).toBe("low");
    expect(bridge.send).toHaveBeenCalledExactlyOnceWith({
      command: "setReducedCarrierLevel",
      level: "low",
    });
  });

  it("keeps the level when its button is pressed again", async () => {
    const user = userEvent.setup();
    const bridge = fakeBridge();
    render(<AppBar bridge={bridge} />);
    await user.click(screen.getByRole("button", { name: "Signal settings" }));

    await user.click(await screen.findByRole("button", { name: "High" }));

    expect(
      screen.getByRole("button", { name: "High", pressed: true }),
    ).toBeInTheDocument();
    expect(bridge.send).not.toHaveBeenCalled();
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
