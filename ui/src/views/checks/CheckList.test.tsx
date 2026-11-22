import { render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import type { ValidationFailure, ValidationResult } from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import { CheckList, ChecksLine } from "./CheckList";

/** A frame that reads this minute past 19 on day 344, accepted unless it is given failures. */
const validation = (
  minute: number,
  failures: ValidationFailure[] = [],
): ValidationResult => ({
  frame: {
    frame: {
      startMicros: 0,
      endMicros: 60_000_000,
      symbols: Array.from({ length: 60 }, () => "zero"),
    },
    minute,
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
  accepted: failures.length === 0,
  failures,
});

const consecutive: ValidationFailure = {
  check: "consecutive",
  detail: "expected 2026-12-10T19:33Z, got 2026-12-10T19:32Z",
};

/** The checks of the frame with this number, each as its name line and its second line. */
const checks = (frame: number): (string | null)[][] =>
  within(screen.getByRole("list", { name: `Checks of frame ${frame}` }))
    .getAllByRole("listitem")
    .map((item) =>
      [...item.querySelectorAll("p")].map((line) => line.textContent),
    );

describe("CheckList", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("says so before the first frame", () => {
    render(<CheckList maxRows={16} />);

    expect(screen.getByText("No frames yet")).toBeInTheDocument();
    expect(screen.queryByRole("list")).not.toBeInTheDocument();
  });

  it("lists the ten checks of the newest frame, each passed, with what it asks", () => {
    useAppStore.setState({
      validations: [validation(30), validation(31)],
      frameCount: 2,
    });
    render(<CheckList maxRows={16} />);

    const lines = checks(2);
    expect(lines).toHaveLength(10);
    expect(lines[0]).toEqual([
      "Markers: passed",
      "A marker in each of the seven marker seconds, and nowhere else",
    ]);
    expect(lines[9][0]).toBe("Consecutive: passed");
    expect(screen.getByText("#2")).toBeInTheDocument();
    expect(screen.getByText("19:31 day 344")).toBeInTheDocument();
    expect(screen.getByText("Accepted")).toHaveClass("text-good");
  });

  it("marks a failed check with the host's detail, in the fault color", () => {
    useAppStore.setState({
      validations: [validation(32, [consecutive])],
      frameCount: 1,
    });
    render(<CheckList maxRows={16} />);

    expect(checks(1)[9]).toEqual(["Consecutive: failed", consecutive.detail]);
    const detail = screen.getByText(consecutive.detail);
    expect(detail).toHaveClass("text-bad");
    // The line can be cut short where the panel is narrow, so the whole of it is in a tooltip.
    expect(detail).toHaveAttribute("title", consecutive.detail);
    expect(screen.getByText("Rejected, 1 of 10 checks failed")).toHaveClass(
      "text-bad",
    );
    // A check that passed has no tooltip: its line is the page's own.
    expect(screen.getByText("The minute is 0 to 59")).not.toHaveAttribute(
      "title",
    );
  });

  it("lists the selected frame's checks while the table shows that frame", () => {
    useAppStore.setState({
      validations: [validation(32, [consecutive]), validation(33)],
      frameCount: 2,
      selectedFrame: 1,
    });
    render(<CheckList maxRows={16} />);

    expect(checks(1)[9][0]).toBe("Consecutive: failed");
    expect(screen.getByText("19:32 day 344")).toBeInTheDocument();
  });

  it("goes back to the newest frame once the selected one has left the table", () => {
    useAppStore.setState({
      validations: [validation(32, [consecutive]), validation(33)],
      frameCount: 2,
      selectedFrame: 1,
    });
    // A table with room for one row shows only frame 2.
    render(<CheckList maxRows={1} />);

    expect(checks(2)[9][0]).toBe("Consecutive: passed");
  });
});

describe("ChecksLine", () => {
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("is empty before the first frame, then names the newest frame", () => {
    const { container, rerender } = render(<ChecksLine maxRows={16} />);
    expect(container).toBeEmptyDOMElement();

    useAppStore.setState({ validations: [validation(30)], frameCount: 7 });
    rerender(<ChecksLine maxRows={16} />);
    expect(screen.getByText("Frame #7, the newest")).toBeInTheDocument();
  });

  it("says when the frame is the one selected", () => {
    useAppStore.setState({
      validations: [validation(30), validation(31)],
      frameCount: 7,
      selectedFrame: 6,
    });
    render(<ChecksLine maxRows={16} />);

    expect(screen.getByText("Frame #6, selected")).toBeInTheDocument();
  });
});
