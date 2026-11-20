import { render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import type { ValidationResult } from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import { createSourceClock } from "@/time/sourceClock";
import { FocusLayout } from "./FocusLayout";
import { stages } from "./stages";

/** A validated frame. Only the table's own tests look at what is in one. */
const validation: ValidationResult = {
  frame: {
    frame: {
      startMicros: 0,
      endMicros: 60_000_000,
      symbols: Array.from({ length: 60 }, () => "zero"),
    },
    minute: 35,
    hour: 19,
    dayOfYear: 344,
    year: 26,
    ut1Sign: "plus",
    ut1Tenths: 0,
    leapYear: false,
    leapSecondPending: false,
    dst: "standard",
    errorSeconds: [],
  },
  accepted: true,
  failures: [],
};

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

  it("shows stage 4 as the frame panel, with its header line", () => {
    useAppStore.setState({
      frameSync: { kind: "searching", consecutiveMarkers: 1 },
    });
    focusOn(4);

    expect(screen.getAllByRole("region")).toHaveLength(1);
    const frame = screen.getByRole("region", { name: "Frame" });
    expect(frame).toHaveTextContent("Searching, 1 of 2 markers");
    expect(
      within(frame).getByRole("list", { name: "Seconds 0 to 19" }),
    ).toBeInTheDocument();
  });

  it.each([5, 6])(
    "lists every frame the store holds for stage %d, 16 at most",
    (number) => {
      useAppStore.setState({
        validations: Array.from({ length: 16 }, () => validation),
        frameCount: 25,
      });
      focusOn(number);

      const rows = within(
        screen.getByRole("region", { name: "Decoded frames" }),
      ).getAllByRole("row");

      // The heading row, then frames 25 down to 6.
      expect(rows).toHaveLength(17);
      expect(rows[1]).toHaveTextContent("#25");
      expect(rows[16]).toHaveTextContent("#10");
    },
  );

  it("shows stage 7 as the time panel, with the clock's status and its rows", () => {
    useAppStore.setState({
      clock: {
        status: "holding",
        frameUtc: "2026-12-10T19:30:00Z",
        frameStartMicros: 1_000_000,
        frameEndMicros: 60_800_000,
        offsetFromPcMs: 37,
        consecutiveAccepted: 0,
        rejectedSinceLastAccepted: 2,
      },
    });
    focusOn(7);

    const time = screen.getByRole("region", { name: "Time" });
    expect(time).toHaveTextContent("19:30 UTC");
    expect(time).toHaveTextContent("2 of 3");
    // Once in the panel's header line and once among the four statuses.
    expect(within(time).getAllByText("Holding")).toHaveLength(2);
  });

  it.each([
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
