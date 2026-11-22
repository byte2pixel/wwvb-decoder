import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { Bridge } from "@/protocol/bridge";
import type { Command, SourceStatus } from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import { AppBar } from "./AppBar";

// jsdom lays nothing out, so a thumb cannot be dragged in it. This stand-in has a button for
// each thing the real slider reports: a step the thumb passes, and the thumb being let go.
vi.mock("@/components/ui/slider", () => ({
  Slider: ({
    onValueChange,
    onValueCommitted,
  }: {
    onValueChange: (value: number[]) => void;
    onValueCommitted: (value: number[]) => void;
  }) => (
    <>
      <button type="button" onClick={() => onValueChange([0.4])}>
        drag to 0.40
      </button>
      <button type="button" onClick={() => onValueChange([0.6])}>
        drag to 0.60
      </button>
      <button type="button" onClick={() => onValueCommitted([0.6])}>
        let go at 0.60
      </button>
    </>
  ),
}));

const fakeBridge = () =>
  ({
    onMessage: () => () => {},
    send: vi.fn<(command: Command) => void>(),
  }) satisfies Bridge;

const status = (
  source: SourceStatus["source"],
  state: SourceStatus["state"],
): SourceStatus => ({ source, name: "", state, detail: "", speed: 1 });

const press = (name: string) =>
  fireEvent.click(screen.getByRole("button", { name }));

describe("the noise slider while it is dragged", () => {
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("moves the readout with the thumb and sends nothing until the thumb is let go", () => {
    const bridge = fakeBridge();
    useAppStore.setState({ sourceStatus: status("synthetic", "running") });
    render(<AppBar bridge={bridge} />);

    press("drag to 0.40");
    expect(screen.getByText("0.40")).toBeInTheDocument();
    press("drag to 0.60");
    expect(screen.getByText("0.60")).toBeInTheDocument();

    expect(bridge.send).not.toHaveBeenCalled();
    expect(useAppStore.getState().picks.noise).toBe(0.6);
  });

  it("sends the noise once, when the thumb is let go", () => {
    const bridge = fakeBridge();
    useAppStore.setState({ sourceStatus: status("synthetic", "running") });
    render(<AppBar bridge={bridge} />);

    press("drag to 0.40");
    press("drag to 0.60");
    press("let go at 0.60");

    expect(bridge.send).toHaveBeenCalledExactlyOnceWith({
      command: "setNoise",
      noise: 0.6,
    });
  });

  it("sends nothing on letting go while no synthetic source runs", () => {
    const bridge = fakeBridge();
    useAppStore.setState({ sourceStatus: status("replay", "running") });
    render(<AppBar bridge={bridge} />);

    press("drag to 0.60");
    press("let go at 0.60");

    expect(bridge.send).not.toHaveBeenCalled();
    expect(screen.getByText("0.60")).toBeInTheDocument();
  });
});
