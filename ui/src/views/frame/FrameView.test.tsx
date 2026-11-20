import { render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { frameLayoutSchema, type SymbolKind } from "@/protocol/messages";
import fixture from "@/sim/fixtures/frameLayout.json";
import type { FilledSlot } from "@/state/appState";
import { useAppStore } from "@/state/store";
import { FrameHeaderLine, FrameView } from "./FrameView";

/** The host's layout, from the copy of its message example. */
const frameLayout = frameLayoutSchema.parse(fixture.payload);

/** A slot of the frame that started 60 s into the source's time. */
const slot = (second: number, symbol: SymbolKind): FilledSlot => ({
  startMicros: (60 + second) * 1_000_000,
  frameStartMicros: 60_000_000,
  second,
  symbol,
});

/** A style percentage as a number, so a test does not depend on how it was computed. */
const percent = (value: string): number => Number.parseFloat(value);

/** The items of the list with this name. */
const items = (list: string): HTMLElement[] =>
  within(screen.getByRole("list", { name: list })).queryAllByRole("listitem");

/** What a row's cells read: the label above each one and the symbol in it. */
const row = (list: string): [string | null, string | null][] =>
  items(list).map((item) => [
    item.children[0].textContent,
    item.children[1].textContent,
  ]);

describe("FrameView", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("draws three rows of twenty empty seconds before the layout arrives", () => {
    render(<FrameView />);

    for (const list of [
      "Seconds 0 to 19",
      "Seconds 20 to 39",
      "Seconds 40 to 59",
    ]) {
      expect(row(list)).toEqual(Array.from({ length: 20 }, () => ["", ""]));
    }
    expect(screen.getByTitle("Second 0: empty")).toBeInTheDocument();
    expect(screen.getByTitle("Second 59: empty")).toBeInTheDocument();
    expect(items("Fields in seconds 0 to 19")).toEqual([]);
  });

  it("writes each second's weight or marker name above its cell", () => {
    useAppStore.setState({ frameLayout });
    render(<FrameView />);

    expect(row("Seconds 0 to 19").map(([label]) => label)).toEqual([
      "Pr",
      "40",
      "20",
      "10",
      "",
      "8",
      "4",
      "2",
      "1",
      "P1",
      "",
      "",
      "20",
      "10",
      "",
      "8",
      "4",
      "2",
      "1",
      "P2",
    ]);
    expect(row("Seconds 40 to 59").at(-1)).toEqual(["P0", ""]);
  });

  it("shows the symbols frame sync has placed, each in its second", () => {
    useAppStore.setState({
      frameLayout,
      slots: [
        slot(0, "marker"),
        slot(1, "zero"),
        slot(2, "one"),
        slot(3, "error"),
      ],
    });
    render(<FrameView />);

    expect(
      row("Seconds 0 to 19")
        .slice(0, 5)
        .map(([, symbol]) => symbol),
    ).toEqual(["M", "0", "1", "×", ""]);
    expect(screen.getByTitle("Second 0: marker")).toHaveTextContent("M");
    expect(screen.getByTitle("Second 3: error")).toHaveTextContent("×");
  });

  it("draws an unused second with a dashed border, empty or holding its 0", () => {
    useAppStore.setState({ frameLayout });
    const { rerender } = render(<FrameView />);

    // Second 4 is unused. Second 1 is the minute's 40.
    expect(screen.getByTitle("Second 4: empty")).toHaveClass("border-dashed");
    expect(screen.getByTitle("Second 1: empty")).not.toHaveClass(
      "border-dashed",
    );

    useAppStore.setState({ slots: [slot(1, "zero"), slot(4, "zero")] });
    rerender(<FrameView />);

    expect(screen.getByTitle("Second 4: 0")).toHaveClass("border-dashed");
    expect(screen.getByTitle("Second 1: 0")).not.toHaveClass("border-dashed");
  });

  it("marks the second being received, and no other", () => {
    useAppStore.setState({
      frameLayout,
      slots: [slot(19, "marker"), slot(20, "zero")],
      frameSync: {
        kind: "slotFilled",
        frameStartMicros: 60_000_000,
        second: 20,
        symbol: "zero",
      },
    });
    const { container } = render(<FrameView />);

    expect(screen.getByTitle("Second 21: empty")).toHaveAttribute(
      "aria-current",
      "true",
    );
    expect(container.querySelectorAll("[aria-current]")).toHaveLength(1);
  });

  it("brackets each field under its seconds, in its own row", () => {
    useAppStore.setState({ frameLayout });
    render(<FrameView />);

    const spans = (list: string) =>
      items(list).map((item) => [
        item.textContent,
        percent(item.style.left),
        percent(item.style.width),
      ]);

    // A second is 5% of a row. The minute runs from second 1 to second 8.
    expect(spans("Fields in seconds 0 to 19")).toEqual([
      ["Minute", 5, 40],
      ["Hour", 60, 35],
    ]);
    expect(spans("Fields in seconds 20 to 39")).toEqual([
      ["Day of year", 10, 60],
      ["UT1 sign", 80, 15],
    ]);
    expect(spans("Fields in seconds 40 to 59")).toEqual([
      ["UT1", 0, 20],
      ["Year", 25, 45],
      ["LY", 75, 5],
      ["LS", 80, 5],
      ["DST", 85, 10],
    ]);
  });
});

describe("FrameHeaderLine", () => {
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("says how much of the frame is placed, in words and as a bar", () => {
    useAppStore.setState({
      frameSync: {
        kind: "slotFilled",
        frameStartMicros: 60_000_000,
        second: 29,
        symbol: "marker",
      },
    });
    render(<FrameHeaderLine />);

    expect(screen.getByText("Synced, 30 of 60 symbols")).toBeInTheDocument();
    const bar = screen.getByRole("progressbar", { name: "Frame placed" });
    expect(bar).toHaveAttribute("value", "50");
    expect(bar).toHaveAttribute("max", "100");
  });

  it("draws no bar while frame sync is searching", () => {
    useAppStore.setState({
      frameSync: { kind: "searching", consecutiveMarkers: 1 },
    });
    render(<FrameHeaderLine />);

    expect(screen.getByText("Searching, 1 of 2 markers")).toBeInTheDocument();
    expect(screen.queryByRole("progressbar")).not.toBeInTheDocument();
  });

  it("shows a loss of sync in the fault color while the store holds it", () => {
    useAppStore.setState({
      frameSync: { kind: "searching", consecutiveMarkers: 0 },
      syncLoss: { reason: "gap 1800 ms before second 37", seq: 12 },
    });
    render(<FrameHeaderLine />);

    expect(
      screen.getByText("Sync lost: gap 1800 ms before second 37"),
    ).toHaveClass("text-bad");
  });
});
