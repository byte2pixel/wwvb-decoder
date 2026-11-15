import { act, render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import type { HostMessage } from "@/protocol/messages";
import { initialState } from "@/state/appState";
import { useAppStore } from "@/state/store";
import { ProtocolLog } from "./ProtocolLog";

const info = (seq: number, line: string): HostMessage => ({
  type: "info",
  seq,
  payload: { line },
});

describe("ProtocolLog", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(initialState);
  });

  it("lists the messages newest first", () => {
    render(<ProtocolLog />);

    act(() => {
      useAppStore.getState().dispatch(info(1, "# first"));
      useAppStore.getState().dispatch(info(2, "# second"));
    });

    const rows = within(
      screen.getByRole("list", { name: "Messages" }),
    ).getAllByRole("listitem");

    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent("# second");
    expect(rows[1]).toHaveTextContent("# first");
  });

  it("counts the messages of each type", () => {
    render(<ProtocolLog />);

    act(() => {
      useAppStore.getState().dispatch(info(1, "# first"));
      useAppStore.getState().dispatch(info(2, "# second"));
    });

    const counts = screen.getByRole("list", { name: "Message counts" });

    expect(within(counts).getByText("info 2")).toBeInTheDocument();
    expect(within(counts).getByText("edges 0")).toBeInTheDocument();
  });
});
