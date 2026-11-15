import { act, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it } from "vitest";
import type { HostMessage } from "@/protocol/messages";
import { initialState } from "@/state/appState";
import { useAppStore } from "@/state/store";
import { StatusBar } from "./StatusBar";

const info = (seq: number, line = "#"): HostMessage => ({
  type: "info",
  seq,
  payload: { line },
});

const dispatch = (...messages: HostMessage[]) =>
  act(() => messages.forEach(useAppStore.getState().dispatch));

describe("StatusBar", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(initialState);
  });

  it("says there is no host before the first source status", () => {
    render(<StatusBar />);

    expect(screen.getByRole("contentinfo")).toHaveTextContent("no host");
  });

  it("shows the run state, the source, and its detail", () => {
    render(<StatusBar />);

    dispatch({
      type: "sourceStatus",
      seq: 1,
      payload: {
        source: "serial",
        name: "serial COM5",
        state: "running",
        detail: "# wwvb-edge-logger 0.1.0 pin=2",
      },
    });

    const bar = screen.getByRole("contentinfo");
    expect(bar).toHaveTextContent("running");
    expect(bar).toHaveTextContent("serial COM5");
    expect(bar).toHaveTextContent("# wwvb-edge-logger 0.1.0 pin=2");
    expect(bar).not.toHaveTextContent("no host");
  });

  it("shows the last sequence number, and lost messages only when there are some", () => {
    render(<StatusBar />);
    const bar = screen.getByRole("contentinfo");

    dispatch(info(1), info(2));
    expect(bar).toHaveTextContent("seq 2");
    expect(bar).not.toHaveTextContent("lost");

    dispatch(info(5));
    expect(bar).toHaveTextContent("seq 5");
    expect(bar).toHaveTextContent("2 lost");
  });

  it("opens the protocol log", async () => {
    const user = userEvent.setup();
    render(<StatusBar />);
    dispatch(info(1, "# wall 2026-11-02T03:11:00Z 14500398"));

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Log" }));

    const log = screen.getByRole("dialog", { name: "Protocol log" });
    expect(within(log).getByText(/# wall 2026-11-02/)).toBeInTheDocument();
  });
});
