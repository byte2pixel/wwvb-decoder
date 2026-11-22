import { fireEvent, render, screen, within } from "@testing-library/react";
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

  it("shows stage 1 as the carrier lane over the info lines, beside the edge log", () => {
    useAppStore.setState({
      edges: [{ micros: 12_000_000, level: "high" }],
      infoLines: ["# wwvb-edge-logger 0.1.0 pin=2", "# dropped 3"],
    });
    focusOn(1);

    expect(screen.getAllByRole("region")).toHaveLength(3);
    const info = screen.getByRole("region", { name: "Info lines" });
    expect(items(info, "Stages")).toEqual(["1"]);
    expect(items(info, "Info lines")).toEqual([
      "# dropped 3",
      "# wwvb-edge-logger 0.1.0 pin=2",
    ]);
    expect(info).toHaveTextContent("Newest first");

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

  it("shows stage 3 as the pulse-width panel with its zones named, the last pulse, and the symbol lane", () => {
    useAppStore.setState({
      symbols: [{ startMicros: 0, durationMicros: 502_000, kind: "one" }],
    });
    focusOn(3);

    expect(screen.getAllByRole("region")).toHaveLength(3);
    const width = screen.getByRole("region", { name: "Pulse width" });
    expect(within(width).getByRole("list", { name: "Bins" })).toBeVisible();
    expect(items(width, "Classes")).toEqual(["error", "0", "1", "M", "error"]);

    const last = screen.getByRole("region", { name: "Last pulse" });
    expect(items(last, "Stages")).toEqual(["3"]);
    expect(last).toHaveTextContent("Input, rule, output");
    expect(within(last).getByText("502 ms")).toBeInTheDocument();
    const signal = screen.getByRole("region", { name: "Signal" });
    expect(items(signal, "Lanes")).toEqual(["Symbol"]);
    expect(items(signal, "Stages")).toEqual(["3"]);
  });

  it("shows stage 4 as the frame panel over its state diagram and the last losses of sync", () => {
    useAppStore.setState({
      frameSync: { kind: "searching", consecutiveMarkers: 1 },
      syncLosses: [
        { number: 1, reason: "gap 2000 ms before second 42" },
        { number: 2, reason: "expected marker at second 9, got Zero" },
      ],
    });
    focusOn(4);

    expect(screen.getAllByRole("region")).toHaveLength(3);
    const states = screen.getByRole("region", { name: "Frame sync states" });
    expect(items(states, "Stages")).toEqual(["4"]);
    expect(within(states).getByText("Searching").closest("g")).toHaveAttribute(
      "aria-current",
      "true",
    );
    expect(within(states).getByText("1 of 2 markers")).toBeVisible();
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

  it("shows stage 5 as the last complete frame beside the record handed to stage 6", () => {
    useAppStore.setState({ decodedFrame: validation.frame });
    focusOn(5);

    expect(screen.getAllByRole("region")).toHaveLength(2);
    const record = screen.getByRole("region", { name: "Decoded frame" });
    expect(items(record, "Stages")).toEqual(["5"]);
    expect(record).toHaveTextContent("Handed to stage 6");
    expect(within(record).getByText("DayOfYear")).toBeInTheDocument();
    expect(within(record).getByText("344")).toBeInTheDocument();

    const frame = screen.getByRole("region", { name: "Last complete frame" });
    expect(items(frame, "Stages")).toEqual(["5"]);
    expect(frame).toHaveTextContent("No error symbols");
    // Every second of the frame is a 0, and none is the one being received.
    expect(within(frame).getByTitle("Second 59: 0")).toBeInTheDocument();
    expect(frame.querySelectorAll("[aria-current]")).toHaveLength(0);
  });

  it("lists the newest 16 frames for stage 6, beside the checks of the newest", () => {
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
    expect(rows[1]).toHaveTextContent("Rejected: error symbols");

    const checks = screen.getByRole("region", { name: "Checks" });
    expect(items(checks, "Stages")).toEqual(["6"]);
    expect(checks).toHaveTextContent("Frame #25, the newest");
    expect(items(checks, "Checks of frame 25")[2]).toBe(
      "Error symbols: failedError at seconds 17",
    );
  });

  it("lists the checks of the frame picked in the stage 6 table", () => {
    useAppStore.setState({
      validations: [
        {
          ...validation,
          accepted: false,
          failures: [{ check: "markers", detail: "second 9 is Zero" }],
        },
        validation,
      ],
      frameCount: 2,
    });
    focusOn(6);
    const checks = screen.getByRole("region", { name: "Checks" });
    expect(items(checks, "Checks of frame 2")[0]).toContain("Markers: passed");

    fireEvent.click(screen.getByRole("button", { name: "#1" }));

    expect(checks).toHaveTextContent("Frame #1, selected");
    expect(items(checks, "Checks of frame 1")[0]).toBe(
      "Markers: failedsecond 9 is Zero",
    );
  });

  it("shows stage 7 as the time panel beside the clock's state diagram", () => {
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

    const states = screen.getByRole("region", { name: "Clock states" });
    expect(items(states, "Stages")).toEqual(["7"]);
    expect(within(states).getByText("Holding").closest("g")).toHaveAttribute(
      "aria-current",
      "true",
    );
    expect(within(states).getByText("3 rejected in a row")).toBeVisible();
  });

  it.each([
    [1, "Info lines", "No info lines yet"],
    [3, "Last pulse", "No pulses yet"],
    [5, "Last complete frame", "No frame yet"],
    [5, "Decoded frame", "No frame yet"],
    [6, "Decoded frames", "No frames yet"],
    [6, "Checks", "No frames yet"],
    [7, "Time", "--:--:--"],
    [7, "Clock states", "No signal"],
  ])(
    "shows stage %d with the %s panel in its no-data state",
    (number, title, text) => {
      focusOn(number);

      expect(screen.getByRole("region", { name: title })).toHaveTextContent(
        text,
      );
    },
  );
});
