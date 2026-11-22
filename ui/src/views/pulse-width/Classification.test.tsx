import { render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import type { SymbolKind, Symbol as WwvbSymbol } from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import { Classification } from "./Classification";

const symbol = (durationMicros: number, kind: SymbolKind): WwvbSymbol => ({
  startMicros: 0,
  durationMicros,
  kind,
});

/** Renders the card with these symbols in the store. */
const given = (...symbols: WwvbSymbol[]) => {
  useAppStore.setState({ symbols });
  return render(<Classification />);
};

/** One of the card's three parts, found by its heading. */
const part = (heading: string): HTMLElement =>
  screen.getByRole("heading", { name: heading }).parentElement!;

/** The rule's rows, each as its duration and its class. */
const rule = (): (string | null)[][] =>
  within(screen.getByRole("list", { name: "Classes by duration" }))
    .getAllByRole("listitem")
    .map((row) => [...row.children].map((cell) => cell.textContent));

/** The row of the rule that is marked as the one the pulse landed in. */
const landedRow = (): string | null | undefined =>
  within(screen.getByRole("list", { name: "Classes by duration" }))
    .getAllByRole("listitem")
    .find((row) => row.getAttribute("aria-current") === "true")?.textContent;

describe("Classification", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("says so before the first pulse", () => {
    render(<Classification />);

    expect(screen.getByText("No pulses yet")).toBeInTheDocument();
    expect(screen.queryByRole("list")).not.toBeInTheDocument();
  });

  it("reads the newest pulse as its input, to the millisecond", () => {
    given(symbol(200_000, "zero"), symbol(501_600, "one"));

    expect(within(part("Pulse")).getByText("502 ms")).toBeInTheDocument();
  });

  it("writes the classifier's rule as five rows, shortest pulse first", () => {
    given(symbol(501_600, "one"));

    expect(rule()).toEqual([
      ["under 100 ms", "×"],
      ["from 100 ms", "0"],
      ["from 350 ms", "1"],
      ["from 650 ms", "M"],
      ["over 900 ms", "×"],
    ]);
  });

  it.each<[number, SymbolKind, string]>([
    [200_000, "zero", "from 100 ms0"],
    [501_600, "one", "from 350 ms1"],
    [800_000, "marker", "from 650 msM"],
    [93_000, "error", "under 100 ms×"],
    [1_200_000, "error", "over 900 ms×"],
  ])(
    "marks the row a pulse of %d µs landed in as a %s",
    (micros, kind, row) => {
      given(symbol(micros, kind));

      expect(landedRow()).toBe(row);
    },
  );

  it.each<[SymbolKind, string, string]>([
    ["zero", "0", "bg-zero-surface"],
    ["one", "1", "bg-signal"],
    ["marker", "M", "bg-marker-surface"],
    ["error", "×", "border-bad"],
  ])("puts out the symbol the host made of it: a %s", (kind, text, tint) => {
    given(symbol(500_000, kind));

    const output = part("Symbol");
    expect(within(output).getByText(text)).toHaveClass(tint);
    // The class in words beside the chip, as the rail writes it.
    expect(within(output).getByText(kind)).toBeInTheDocument();
  });
});
