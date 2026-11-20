import { render, screen, within } from "@testing-library/react";
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

  it("lists every failure in a rejected row when it is asked for the details", () => {
    given([rejected("errorSymbols", "consecutive")]);
    render(<FramesTable maxRows={3} details />);

    const failures = screen.getByRole("list", { name: "Failures of frame 1" });
    const result = failures.closest("td")!;

    expect(
      within(failures)
        .getAllByRole("listitem")
        .map((item) => item.textContent),
    ).toEqual([
      "error symbols: errorSymbols detail",
      "consecutive: consecutive detail",
    ]);
    // The lines say which check failed, so the result does not name the first again.
    expect(result.firstChild).toHaveTextContent(/^Rejected$/);
    expect(result).toHaveClass("text-bad");
    expect(result).not.toHaveClass("truncate");
    expect(result).not.toHaveAttribute("title");
    expect(result.closest("tr")).toHaveClass("bg-bad/10");
  });

  it("lists nothing under an accepted row, with or without the details", () => {
    given([accepted()]);
    render(<FramesTable maxRows={3} details />);

    expect(screen.getByRole("cell", { name: "Accepted" })).toHaveClass(
      "truncate",
    );
    expect(screen.queryByRole("list")).not.toBeInTheDocument();
  });

  it("gives the result more of the width beside the details", () => {
    const widths = (container: HTMLElement) =>
      [...container.querySelectorAll("col")].map((col) => col.className);

    const plain = render(<FramesTable maxRows={3} />);
    const detailed = render(<FramesTable maxRows={3} details />);

    expect(widths(plain.container)).toEqual([
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
    expect(widths(detailed.container)[1]).toBe("w-[28%]");
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
