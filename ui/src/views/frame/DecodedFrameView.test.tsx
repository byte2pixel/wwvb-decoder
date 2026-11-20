import { render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { frameLayoutSchema, type DecodedFrame } from "@/protocol/messages";
import fixture from "@/sim/fixtures/frameLayout.json";
import { useAppStore } from "@/state/store";
import { DecodedFrameLine, DecodedFrameView } from "./DecodedFrameView";

/** The host's layout, from the copy of its message example. */
const frameLayout = frameLayoutSchema.parse(fixture.payload);

/** A frame that reads 19:35 on day 344: a marker and a 1, then zeros, with an error in second 44. */
const decodedFrame: DecodedFrame = {
  frame: {
    startMicros: 61_000_000,
    endMicros: 120_800_000,
    symbols: Array.from({ length: 60 }, (_, second) => {
      if (second === 0) return "marker";
      if (second === 44) return "error";
      return second === 1 ? "one" : "zero";
    }),
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
  // Second 44 is unused, and the host lists only field seconds here.
  errorSeconds: [],
};

/** Each bracket of the list with this name, as its field's name and the value under it. */
const fields = (list: string): (string | null)[][] =>
  within(screen.getByRole("list", { name: list }))
    .getAllByRole("listitem")
    .map((item) => [...item.children].map((child) => child.textContent));

describe("DecodedFrameView", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("draws empty seconds and the fields' names alone before the first frame", () => {
    useAppStore.setState({ frameLayout });
    render(<DecodedFrameView />);

    expect(screen.getByTitle("Second 0: empty")).toBeInTheDocument();
    expect(screen.getByTitle("Second 59: empty")).toBeInTheDocument();
    expect(fields("Fields in seconds 0 to 19")).toEqual([["Minute"], ["Hour"]]);
  });

  it("shows the last complete frame's symbols, each in its second", () => {
    useAppStore.setState({ frameLayout, decodedFrame });
    render(<DecodedFrameView />);

    expect(screen.getByTitle("Second 0: marker")).toHaveTextContent("M");
    expect(screen.getByTitle("Second 1: 1")).toHaveTextContent("1");
    expect(screen.getByTitle("Second 44: error")).toHaveTextContent("×");
    expect(screen.getByTitle("Second 59: 0")).toHaveTextContent("0");
  });

  it("writes each field's decoded value under its name", () => {
    useAppStore.setState({ frameLayout, decodedFrame });
    render(<DecodedFrameView />);

    expect(fields("Fields in seconds 0 to 19")).toEqual([
      ["Minute", "35"],
      ["Hour", "19"],
    ]);
    expect(fields("Fields in seconds 20 to 39")).toEqual([
      ["Day of year", "344"],
      ["UT1 sign", "-"],
    ]);
    expect(fields("Fields in seconds 40 to 59")).toEqual([
      ["UT1", "0.3 s"],
      ["Year", "26"],
      ["LY", "no"],
      ["LS", "yes"],
      ["DST", "ends today"],
    ]);
  });

  it("marks no second as being received", () => {
    useAppStore.setState({
      frameLayout,
      decodedFrame,
      // The frame in progress is at second 21; this view shows the one before it.
      frameSync: {
        kind: "slotFilled",
        frameStartMicros: 121_000_000,
        second: 20,
        symbol: "zero",
      },
    });
    const { container } = render(<DecodedFrameView />);

    expect(container.querySelectorAll("[aria-current]")).toHaveLength(0);
  });
});

describe("DecodedFrameLine", () => {
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("says there is no frame yet, then names the seconds that held an error", () => {
    const { rerender } = render(<DecodedFrameLine />);
    expect(screen.getByText("No frame yet")).toBeInTheDocument();

    useAppStore.setState({ decodedFrame });
    rerender(<DecodedFrameLine />);
    expect(screen.getByText("Error symbol in second 44")).toBeInTheDocument();
  });
});
