import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import type {
  SymbolKind,
  ValidationCheck,
  ValidationResult,
} from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import { FramesTable } from "./FramesTable";

const KINDS: Record<string, SymbolKind> = {
  M: "marker",
  "0": "zero",
  "1": "one",
  x: "error",
};

/** A frame that reads 19:35 on day 344, with these symbols first and zeros after them. */
const frame = (first = "M01"): ValidationResult["frame"] => ({
  frame: {
    startMicros: 61_000_000,
    endMicros: 120_800_000,
    symbols: [...first.padEnd(60, "0")].map((letter) => KINDS[letter]),
  },
  minute: 35,
  hour: 19,
  dayOfYear: 344,
  year: 26,
  ut1Sign: "plus",
  ut1Tenths: 3,
  leapYear: false,
  leapSecondPending: false,
  dst: "standard",
  errorSeconds: [],
});

const accepted = (first?: string): ValidationResult => ({
  frame: frame(first),
  accepted: true,
  failures: [],
});

const rejected = (...checks: ValidationCheck[]): ValidationResult => ({
  frame: frame(),
  accepted: false,
  failures: checks.map((check) => ({ check, detail: `${check} detail` })),
});

/** Puts these results in the store as the newest of `frameCount` frames. */
const given = (
  validations: ValidationResult[],
  frameCount = validations.length,
) => useAppStore.setState({ validations, frameCount });

/** The table's body rows, each as the text of its cells. */
const bodyRows = (): (string | null)[][] =>
  screen
    .getAllByRole("row")
    .slice(1)
    .map((row) =>
      within(row)
        .getAllByRole("cell")
        .map((cell) => cell.textContent),
    );

describe("FramesTable", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("heads its columns and says so while there are no frames", () => {
    render(<FramesTable maxRows={3} />);

    expect(
      screen.getAllByRole("columnheader").map((head) => head.textContent),
    ).toEqual([
      "Frame",
      "Symbols 0 to 59",
      "Min",
      "Hour",
      "Day",
      "Year",
      "UT1",
      "DST",
      "Result",
    ]);
    expect(bodyRows()).toEqual([]);
    expect(screen.getByText("No frames yet")).toBeInTheDocument();
  });

  it("writes a frame as its number, its decoded values, and its result", () => {
    given([accepted()], 41);
    render(<FramesTable maxRows={3} />);

    expect(bodyRows()).toEqual([
      ["#41", "", "35", "19", "344", "26", "+0.3 s", "standard", "Accepted"],
    ]);
    expect(screen.queryByText("No frames yet")).not.toBeInTheDocument();
  });

  it("draws the frame's 60 symbols as a strip of ticks", () => {
    given([accepted("M01x")]);
    render(<FramesTable maxRows={3} />);

    const strip = screen.getByTitle(`M01×${"0".repeat(56)}`);
    const ticks = [...strip.children];

    expect(ticks).toHaveLength(60);
    // A marker and a 1 are tall, a 0 is short, and an error is in the fault color.
    expect(ticks[0]).toHaveClass("h-full", "bg-marker");
    expect(ticks[1]).toHaveClass("h-1/3");
    expect(ticks[2]).toHaveClass("h-full", "bg-signal");
    expect(ticks[3]).toHaveClass("h-full", "bg-bad");
  });

  it("tints a rejected row and lists every failure in the result's tooltip", () => {
    given([rejected("errorSymbols", "consecutive")]);
    render(<FramesTable maxRows={3} />);

    const result = screen.getByRole("cell", {
      name: "Rejected: error symbols",
    });

    expect(result.closest("tr")).toHaveClass("bg-bad/10");
    expect(result).toHaveClass("text-bad");
    expect(result).toHaveAttribute(
      "title",
      "error symbols: errorSymbols detail\nconsecutive: consecutive detail",
    );
  });

  it("leaves an accepted row plain, with no tooltip", () => {
    given([accepted()]);
    render(<FramesTable maxRows={3} />);

    const result = screen.getByRole("cell", { name: "Accepted" });

    expect(result.closest("tr")).not.toHaveClass("bg-bad/10");
    expect(result).not.toHaveAttribute("title");
  });

  it("gives each column but the result a share of the width", () => {
    const { container } = render(<FramesTable maxRows={3} />);

    expect(
      [...container.querySelectorAll("col")].map((col) => col.className),
    ).toEqual([
      "w-[7%]",
      "w-[36%]",
      "w-[5%]",
      "w-[5%]",
      "w-[5%]",
      "w-[5%]",
      "w-[8%]",
      "w-[10%]",
      "",
    ]);
  });

  describe("when a frame arrives", () => {
    /** True for each body row that slides in, newest first. */
    const slidesIn = (): boolean[] =>
      screen
        .getAllByRole("row")
        .slice(1)
        .map((row) => row.classList.contains("slide-in-from-top-2"));

    it("draws the frames that were already there in place", () => {
      given([accepted(), accepted()]);
      render(<FramesTable maxRows={3} />);

      expect(slidesIn()).toEqual([false, false]);
    });

    it("slides the new frame's row in, and none under reduced motion", () => {
      given([accepted(), accepted()]);
      render(<FramesTable maxRows={3} />);

      act(() => given([accepted(), accepted(), accepted()]));

      expect(slidesIn()).toEqual([true, false, false]);
      expect(screen.getAllByRole("row")[1]).toHaveClass(
        "animate-in",
        "motion-reduce:animate-none",
      );
    });

    it("slides in the first frames after a source reset", () => {
      given([accepted(), accepted()]);
      render(<FramesTable maxRows={3} />);

      act(() => given([]));
      act(() => given([accepted()]));

      expect(slidesIn()).toEqual([true]);
    });
  });

  it("has nothing to press unless its rows can be selected", () => {
    given([accepted(), accepted()]);
    render(<FramesTable maxRows={3} />);

    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    expect(screen.getAllByRole("row")[1]).not.toHaveClass("outline-signal");
  });

  describe("with rows that can be selected", () => {
    /** The body row of the frame with this number. */
    const rowOf = (number: number): HTMLElement =>
      screen.getByRole("button", { name: `#${number}` }).closest("tr")!;

    it("makes each frame's number a button and outlines the newest frame", () => {
      given([accepted(), rejected("markers"), accepted()]);
      render(<FramesTable maxRows={3} selectable />);

      expect(
        screen.getAllByRole("button").map((button) => button.textContent),
      ).toEqual(["#3", "#2", "#1"]);
      // Nothing is selected yet, so the view follows the newest frame.
      expect(rowOf(3)).toHaveClass("outline-signal");
      expect(rowOf(2)).not.toHaveClass("outline-signal");
      expect(
        screen.queryByRole("button", { pressed: true }),
      ).not.toBeInTheDocument();
    });

    it("selects the frame whose number is clicked, and outlines its row", () => {
      given([accepted(), rejected("markers"), accepted()]);
      render(<FramesTable maxRows={3} selectable />);

      fireEvent.click(screen.getByRole("button", { name: "#2" }));

      expect(useAppStore.getState().selectedFrame).toBe(2);
      expect(screen.getByRole("button", { pressed: true })).toHaveTextContent(
        "#2",
      );
      expect(rowOf(2)).toHaveClass("outline-signal");
      expect(rowOf(3)).not.toHaveClass("outline-signal");
    });

    it("keeps the selected frame when a newer one arrives", () => {
      given([accepted(), rejected("markers")]);
      render(<FramesTable maxRows={3} selectable />);
      fireEvent.click(screen.getByRole("button", { name: "#1" }));

      act(() => given([accepted(), rejected("markers"), accepted()]));

      expect(rowOf(1)).toHaveClass("outline-signal");
      expect(rowOf(3)).not.toHaveClass("outline-signal");
    });

    it("lets go of the frame on a second click, and follows the newest again", () => {
      given([accepted(), accepted()]);
      render(<FramesTable maxRows={3} selectable />);

      fireEvent.click(screen.getByRole("button", { name: "#1" }));
      fireEvent.click(screen.getByRole("button", { name: "#1" }));

      expect(useAppStore.getState().selectedFrame).toBeNull();
      expect(rowOf(2)).toHaveClass("outline-signal");
    });

    it("outlines the newest frame when the selected one is no longer in the table", () => {
      given([accepted(), accepted(), accepted(), accepted()]);
      useAppStore.setState({ selectedFrame: 1 });
      render(<FramesTable maxRows={3} selectable />);

      expect(rowOf(4)).toHaveClass("outline-signal");
      expect(
        screen.queryByRole("button", { pressed: true }),
      ).not.toBeInTheDocument();
    });
  });

  it("shows the newest frames first, and no more rows than it is given room for", () => {
    given([
      accepted(),
      accepted(),
      rejected("markers"),
      accepted(),
      accepted(),
    ]);
    render(<FramesTable maxRows={3} />);

    expect(
      bodyRows().map(([number, , , , , , , , result]) => [number, result]),
    ).toEqual([
      ["#5", "Accepted"],
      ["#4", "Accepted"],
      ["#3", "Rejected: markers"],
    ]);
  });
});
