import { act, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import type { ClockState, ClockStatus } from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import { ClockDiagram } from "./ClockDiagram";

const clock = (
  status: ClockStatus,
  consecutiveAccepted = 0,
  rejectedSinceLastAccepted = 0,
): ClockState => ({
  status,
  frameUtc: "2026-12-10T19:30:00Z",
  frameStartMicros: 1_000_000,
  frameEndMicros: 60_800_000,
  offsetFromPcMs: 37,
  consecutiveAccepted,
  rejectedSinceLastAccepted,
});

const STATES = ["No signal", "Acquiring", "Locked", "Holding"];

/** The state the diagram marks as the one the clock is in. */
const current = (): (string | null)[] =>
  STATES.filter(
    (label) =>
      screen.getByText(label).closest("g")?.getAttribute("aria-current") ===
      "true",
  );

/** The number drawn before a counter's label. */
const count = (label: string): string | null | undefined =>
  screen.getByText(label).closest("g")?.firstElementChild?.textContent;

describe("ClockDiagram", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("is a drawing named Clock states, with the four statuses", () => {
    render(<ClockDiagram />);

    expect(screen.getByTitle("Clock states")).toBeInTheDocument();
    for (const label of STATES) {
      expect(screen.getByText(label)).toBeInTheDocument();
    }
  });

  it("labels each arrow with what moves the clock along it", () => {
    render(<ClockDiagram />);

    for (const label of [
      "first frame",
      "2 accepted in a row",
      "rejected",
      "accepted",
      "3 rejected in a row",
      "A source reset returns the clock to No signal.",
    ]) {
      expect(screen.getByText(label)).toBeInTheDocument();
    }
  });

  it("marks No signal before the clock has any state", () => {
    render(<ClockDiagram />);

    expect(current()).toEqual(["No signal"]);
  });

  it.each<[ClockStatus, string, string]>([
    ["noSignal", "No signal", "stroke-foreground-soft"],
    ["acquiring", "Acquiring", "stroke-warn"],
    ["locked", "Locked", "stroke-good"],
    ["holding", "Holding", "stroke-warn"],
  ])("marks the status %s, in its health color", (status, label, stroke) => {
    useAppStore.setState({ clock: clock(status) });
    render(<ClockDiagram />);

    expect(current()).toEqual([label]);
    expect(
      screen.getByText(label).closest("g")?.querySelector("rect"),
    ).toHaveClass(stroke);
  });

  it("draws the other statuses at rest", () => {
    useAppStore.setState({ clock: clock("locked") });
    render(<ClockDiagram />);

    expect(
      screen.getByText("Holding").closest("g")?.querySelector("rect"),
    ).toHaveClass("stroke-input");
  });

  it("shows the two counters at 0 before the first frame, each with its limit", () => {
    render(<ClockDiagram />);

    expect(count("Accepted in a row")).toBe("0");
    expect(count("Rejected in a row")).toBe("0");
    expect(screen.getByText("2 lock the clock")).toBeInTheDocument();
    expect(screen.getByText("3 end the hold")).toBeInTheDocument();
  });

  it("follows the clock as it changes", () => {
    useAppStore.setState({ clock: clock("locked", 5, 0) });
    render(<ClockDiagram />);
    expect(count("Accepted in a row")).toBe("5");

    act(() => useAppStore.setState({ clock: clock("holding", 0, 2) }));

    expect(current()).toEqual(["Holding"]);
    expect(count("Accepted in a row")).toBe("0");
    expect(count("Rejected in a row")).toBe("2");
  });
});
