import { render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { useAppStore } from "@/state/store";
import { createSourceClock } from "@/time/sourceClock";
import { FocusLayout } from "./FocusLayout";
import { stages } from "./stages";

/** Renders the focus view of the stage with this number. */
const focusOn = (number: number) =>
  render(
    <FocusLayout
      stage={stages[number - 1]}
      clock={createSourceClock(() => 0)}
    />,
  );

/** What each item of a panel's named list reads. */
const items = (panel: HTMLElement, list: string): (string | null)[] =>
  within(within(panel).getByRole("list", { name: list }))
    .getAllByRole("listitem")
    .map((item) => item.textContent);

describe("FocusLayout", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("heads the view with the stage's number, name, type, and rule", () => {
    focusOn(2);

    const header = screen
      .getByRole("heading", { level: 2, name: "Pulses" })
      .closest("header")!;
    expect(within(header).getByText("2")).toBeInTheDocument();
    expect(within(header).getByText("IStage<Edge, Pulse>")).toBeInTheDocument();
    expect(header).toHaveTextContent(
      "A pulse is the time the carrier stays reduced",
    );
  });

  it("shows stage 1 as the carrier lane beside the edge log", () => {
    useAppStore.setState({ edges: [{ micros: 12_000_000, level: "high" }] });
    focusOn(1);

    const signal = screen.getByRole("region", { name: "Signal" });
    expect(items(signal, "Lanes")).toEqual(["Carrier"]);
    expect(items(signal, "Stages")).toEqual(["1"]);

    const log = screen.getByRole("region", { name: "Edge log" });
    expect(items(log, "Edges")).toEqual(["H 12000000"]);
    expect(log).toHaveTextContent("Newest first");
  });

  it("shows stage 2 as the carrier and pulse lanes", () => {
    focusOn(2);

    const signal = screen.getByRole("region", { name: "Signal" });
    expect(items(signal, "Lanes")).toEqual(["Carrier", "Pulse"]);
    expect(items(signal, "Stages")).toEqual(["1", "2"]);
    expect(screen.getAllByRole("region")).toHaveLength(1);
  });

  it("shows stage 3 as the pulse-width panel with the symbol lane", () => {
    focusOn(3);

    expect(
      within(screen.getByRole("region", { name: "Pulse width" })).getByRole(
        "list",
        { name: "Bins" },
      ),
    ).toBeInTheDocument();
    const signal = screen.getByRole("region", { name: "Signal" });
    expect(items(signal, "Lanes")).toEqual(["Symbol"]);
    expect(items(signal, "Stages")).toEqual(["3"]);
  });

  it.each([
    [4, "Frame", "No symbols placed yet"],
    [5, "Decoded frames", "No frames yet"],
    [6, "Decoded frames", "No frames yet"],
    [7, "Time", "--:--:--"],
  ])(
    "shows stage %d as the %s panel in its no-data state",
    (number, title, text) => {
      focusOn(number);

      expect(screen.getAllByRole("region")).toHaveLength(1);
      expect(screen.getByRole("region", { name: title })).toHaveTextContent(
        text,
      );
    },
  );
});
