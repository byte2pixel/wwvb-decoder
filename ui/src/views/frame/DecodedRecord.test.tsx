import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import type { DecodedFrame } from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import { DecodedRecord } from "./DecodedRecord";

/** A frame that reads 19:35 on day 344, with an error symbol in second 6. */
const decodedFrame: DecodedFrame = {
  frame: {
    startMicros: 61_000_000,
    endMicros: 120_800_000,
    symbols: Array.from({ length: 60 }, () => "zero"),
  },
  minute: 35,
  hour: 19,
  dayOfYear: 344,
  year: 26,
  ut1Sign: "minus",
  ut1Tenths: 3,
  leapYear: false,
  leapSecondPending: true,
  dst: "endsToday",
  errorSeconds: [6],
};

/** What a list of elements reads, in order. */
const texts = (elements: HTMLElement[]): (string | null)[] =>
  elements.map((element) => element.textContent);

describe("DecodedRecord", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("says so before the first complete frame", () => {
    render(<DecodedRecord />);

    expect(screen.getByText("No frame yet")).toBeInTheDocument();
    expect(screen.queryByRole("term")).not.toBeInTheDocument();
  });

  it("lists the record's properties under their C# names, each with its value", () => {
    useAppStore.setState({ decodedFrame });
    render(<DecodedRecord />);

    expect(texts(screen.getAllByRole("term"))).toEqual([
      "Frame",
      "Minute",
      "Hour",
      "DayOfYear",
      "Year",
      "Ut1Sign",
      "Ut1Tenths",
      "LeapYear",
      "LeapSecondPending",
      "Dst",
      "ErrorSeconds",
    ]);
    expect(texts(screen.getAllByRole("definition"))).toEqual([
      "60 symbols",
      "35",
      "19",
      "344",
      "26",
      "Minus",
      "3",
      "false",
      "true",
      "EndsToday",
      "6",
    ]);
    expect(screen.queryByText("No frame yet")).not.toBeInTheDocument();
  });
});
