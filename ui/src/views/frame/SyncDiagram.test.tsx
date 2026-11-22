import { act, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { useAppStore } from "@/state/store";
import { SyncDiagram } from "./SyncDiagram";

/** The group a state's name is drawn in: its box, its name, and its count. */
const node = (label: string): SVGGElement =>
  screen.getByText(label).closest("g")!;

/** True when the diagram marks this state as the one frame sync is in. */
const isCurrent = (label: string): boolean =>
  node(label).getAttribute("aria-current") === "true";

describe("SyncDiagram", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("is a drawing named Frame sync states, with both states at rest before a source runs", () => {
    render(<SyncDiagram />);

    expect(screen.getByTitle("Frame sync states")).toBeInTheDocument();
    expect(isCurrent("Searching")).toBe(false);
    expect(isCurrent("Synced")).toBe(false);
    expect(node("Searching").querySelector("rect")).toHaveClass("stroke-input");
  });

  it("labels the arrow each way with what causes the move", () => {
    render(<SyncDiagram />);

    expect(screen.getByText("2 markers a second apart")).toBeInTheDocument();
    expect(screen.getByText("sync lost")).not.toHaveClass("fill-bad");
  });

  it("marks Searching as a warning, with the markers counted so far", () => {
    useAppStore.setState({
      frameSync: { kind: "searching", consecutiveMarkers: 1 },
    });
    render(<SyncDiagram />);

    expect(isCurrent("Searching")).toBe(true);
    expect(isCurrent("Synced")).toBe(false);
    expect(node("Searching").querySelector("rect")).toHaveClass("stroke-warn");
    expect(node("Searching")).toHaveTextContent("1 of 2 markers");
    // The state it is not in shows no count.
    expect(node("Synced")).toHaveTextContent(/^Synced$/);
  });

  it("marks Synced as good, with the symbols placed so far", () => {
    useAppStore.setState({
      frameSync: {
        kind: "slotFilled",
        frameStartMicros: 1_000_000,
        second: 36,
        symbol: "one",
      },
    });
    render(<SyncDiagram />);

    expect(isCurrent("Synced")).toBe(true);
    expect(node("Synced").querySelector("rect")).toHaveClass("stroke-good");
    expect(node("Synced")).toHaveTextContent("37 of 60 symbols");
    expect(node("Searching")).toHaveTextContent(/^Searching$/);
  });

  it("names the reason on the arrow back, in the fault color, while a loss is held", () => {
    useAppStore.setState({
      frameSync: { kind: "searching", consecutiveMarkers: 0 },
      syncLoss: { reason: "gap 2000 ms before second 15", seq: 7 },
    });
    render(<SyncDiagram />);

    expect(
      screen.getByText("sync lost: gap 2000 ms before second 15"),
    ).toHaveClass("fill-bad");
    expect(node("Searching").querySelector("rect")).toHaveClass("stroke-bad");
  });

  it("goes back to the plain arrow and the warning color when the hold ends", () => {
    useAppStore.setState({
      frameSync: { kind: "searching", consecutiveMarkers: 0 },
      syncLoss: { reason: "gap 2000 ms before second 15", seq: 7 },
    });
    render(<SyncDiagram />);

    act(() => useAppStore.setState({ syncLoss: null }));

    expect(screen.getByText("sync lost")).not.toHaveClass("fill-bad");
    expect(node("Searching").querySelector("rect")).toHaveClass("stroke-warn");
  });
});
