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

  it("shows stage 4 as the frame panel over the last losses of sync", () => {
    useAppStore.setState({
      frameSync: { kind: "searching", consecutiveMarkers: 1 },
      syncLosses: [
        { number: 1, reason: "gap 2000 ms before second 42" },
        { number: 2, reason: "expected marker at second 9, got Zero" },
      ],
    });
    focusOn(4);

    expect(screen.getAllByRole("region")).toHaveLength(2);
    const frame = screen.getByRole("region", { name: "Frame" });
    expect(frame).toHaveTextContent("Searching, 1 of 2 markers");
    expect(
      within(frame).getByRole("list", { name: "Seconds 0 to 19" }),
    ).toBeInTheDocument();

    const losses = screen.getByRole("region", { name: "Sync losses" });
    expect(items(losses, "Stages")).toEqual(["4"]);
    expect(items(losses, "Sync losses")).toEqual([
      "#2expected marker at second 9, got Zero",
      "#1gap 2000 ms before second 42",
    ]);
    expect(losses).toHaveTextContent("Newest first");
  });

  it("shows stage 5 as the last complete frame, with each field's value", () => {
    useAppStore.setState({ decodedFrame: validation.frame });
    focusOn(5);

    expect(screen.getAllByRole("region")).toHaveLength(1);
    const frame = screen.getByRole("region", { name: "Last complete frame" });
    expect(items(frame, "Stages")).toEqual(["5"]);
    expect(frame).toHaveTextContent("No error symbols");
    // Every second of the frame is a 0, and none is the one being received.
    expect(within(frame).getByTitle("Second 59: 0")).toBeInTheDocument();
    expect(frame.querySelectorAll("[aria-current]")).toHaveLength(0);
  });

  it("lists the newest 16 frames for stage 6, each failure in its row", () => {
    useAppStore.setState({
      validations: [
        ...Array.from({ length: 19 }, () => validation),
        {
          ...validation,
          accepted: false,
          failures: [{ check: "errorSymbols", detail: "Error at seconds 17" }],
        },
      ],
      frameCount: 25,
    });
    focusOn(6);

    const table = screen.getByRole("region", { name: "Decoded frames" });
    const rows = within(table).getAllByRole("row");

    // The heading row, then frames 25 down to 10 of the 20 the store holds.
    expect(rows).toHaveLength(17);
    expect(rows[1]).toHaveTextContent("#25");
    expect(rows[16]).toHaveTextContent("#10");
    expect(items(table, "Failures of frame 25")).toEqual([
      "error symbols: Error at seconds 17",
    ]);
  });

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
    [5, "Last complete frame", "No frame yet"],
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
