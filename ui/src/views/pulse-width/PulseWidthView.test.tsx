import { act, render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { initialState } from "@/state/appState";
import { useAppStore } from "@/state/store";
import { PulseWidthView, LastPulseLine } from "./PulseWidthView";
import type { SymbolKind, Symbol as WwvbSymbol } from "@/protocol/messages";

const symbol = (durationMicros: number, kind: SymbolKind): WwvbSymbol => ({
  startMicros: 0,
  durationMicros,
  kind,
});

/** A style percentage as a number, so a test does not depend on how it was computed. */
const percent = (value: string): number => Number.parseFloat(value);

/** The items of the list with this name. */
const items = (list: string): HTMLElement[] =>
  within(screen.getByRole("list", { name: list })).queryAllByRole("listitem");

// The store is one module-level object shared by every test.
beforeEach(() => {
  useAppStore.setState(initialState);
});

describe("PulseWidthView", () => {
  it("marks the four class boundaries along the axis", () => {
    render(<PulseWidthView />);

    const boundaries = items("Boundaries");

    expect(boundaries.map((mark) => mark.textContent)).toEqual([
      "100",
      "350",
      "650",
      "900",
    ]);
    expect(boundaries.map((mark) => percent(mark.style.left))).toEqual([
      10, 35, 65, 90,
    ]);
  });

  it("draws the two limits dashed and the two thresholds solid", () => {
    render(<PulseWidthView />);

    const dashed = items("Boundaries").map((mark) =>
      mark.classList.contains("border-dashed"),
    );

    expect(dashed).toEqual([true, false, false, true]);
  });

  it("tints the error zones outside the limits", () => {
    render(<PulseWidthView />);

    const under = screen.getByTitle("Error: under 100 ms");
    const over = screen.getByTitle("Error: over 900 ms");

    expect([percent(under.style.left), percent(under.style.width)]).toEqual([
      0, 10,
    ]);
    expect([percent(over.style.left), percent(over.style.width)]).toEqual([
      90, 10,
    ]);
  });

  it("draws no bars and no last-pulse line before any pulse", () => {
    render(<PulseWidthView />);

    expect(items("Bins")).toEqual([]);
    expect(screen.queryByTitle("Last pulse")).toBeNull();
  });

  it("draws a bar over each bin that holds a pulse, as tall as its share", () => {
    useAppStore.setState({
      symbols: [
        symbol(200_000, "zero"),
        symbol(210_000, "zero"),
        symbol(500_000, "one"),
      ],
    });
    render(<PulseWidthView />);

    const bars = items("Bins");

    expect(bars.map((bar) => bar.title)).toEqual([
      "200 to 225 ms: 2",
      "500 to 525 ms: 1",
    ]);
    expect(bars.map((bar) => percent(bar.style.left))).toEqual([20, 50]);
    expect(bars.map((bar) => percent(bar.style.width))).toEqual([2.5, 2.5]);
    expect(bars.map((bar) => percent(bar.style.height))).toEqual([100, 50]);
  });

  it.each([
    ["zero", "bg-signal/60"],
    ["one", "bg-signal"],
    ["marker", "bg-marker"],
    ["error", "bg-bad"],
  ] as const)("colors a %s bar with %s", (kind, color) => {
    useAppStore.setState({ symbols: [symbol(500_000, kind)] });
    render(<PulseWidthView />);

    expect(items("Bins")[0]).toHaveClass(color);
  });

  it("adds a bar when a symbol arrives", () => {
    render(<PulseWidthView />);

    act(() =>
      useAppStore.getState().dispatch({
        type: "symbol",
        seq: 1,
        payload: symbol(800_000, "marker"),
      }),
    );

    expect(items("Bins").map((bar) => bar.title)).toEqual(["800 to 825 ms: 1"]);
  });

  it("marks the last pulse with a line at its duration", () => {
    useAppStore.setState({
      symbols: [symbol(200_000, "zero"), symbol(502_000, "one")],
    });
    render(<PulseWidthView />);

    const line = screen.getByTitle("Last pulse");

    expect(percent(line.style.left)).toBeCloseTo(50.2);
  });

  it("keeps the last-pulse line at the right edge for a pulse past the range", () => {
    useAppStore.setState({ symbols: [symbol(1_400_000, "error")] });
    render(<PulseWidthView />);

    expect(percent(screen.getByTitle("Last pulse").style.left)).toBe(100);
  });

  it("enlarges the axis text when its panel is wide, as in the focus view", () => {
    const { container } = render(<PulseWidthView />);

    const axisText = [
      ...items("Boundaries").map((mark) => mark.firstElementChild),
      screen.getByText("ms"),
    ];

    // The view is the container whose width the text follows.
    expect(container.firstElementChild).toHaveClass("@container");
    expect(
      axisText.map((text) => text?.classList.contains("@2xl:text-base")),
    ).toEqual([true, true, true, true, true]);
  });
});

describe("LastPulseLine", () => {
  it("says there are no pulses yet before the first", () => {
    render(<LastPulseLine />);

    expect(screen.getByText("No pulses yet")).toBeInTheDocument();
  });

  it("reads the newest pulse in the store", () => {
    useAppStore.setState({
      symbols: [symbol(200_000, "zero"), symbol(502_000, "one")],
    });
    render(<LastPulseLine />);

    expect(
      screen.getByText("Last pulse 502 ms classified as 1"),
    ).toBeInTheDocument();
  });
});
