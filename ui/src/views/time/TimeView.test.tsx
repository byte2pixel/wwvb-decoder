import { act, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ClockState, DecodedFrame } from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import type { SourceClock } from "@/time/sourceClock";
import { ClockStatusLine, TimeView } from "./TimeView";

/** Locked on the frame of 19:30, which began 1 s into the source's time. */
const locked: ClockState = {
  status: "locked",
  frameUtc: "2026-12-10T19:30:00Z",
  frameStartMicros: 1_000_000,
  frameEndMicros: 60_800_000,
  offsetFromPcMs: 37,
  consecutiveAccepted: 3,
  rejectedSinceLastAccepted: 0,
};

/** The newest decoded frame; the view reads only its DST status. */
const decodedFrame: DecodedFrame = {
  frame: {
    startMicros: 1_000_000,
    endMicros: 60_800_000,
    symbols: Array.from({ length: 60 }, () => "zero"),
  },
  minute: 30,
  hour: 19,
  dayOfYear: 344,
  year: 26,
  ut1Sign: "plus",
  ut1Tenths: 0,
  leapYear: false,
  leapSecondPending: false,
  dst: "standard",
  errorSeconds: [],
};

/** A source clock a test moves by hand. */
function handClock(micros: number | null) {
  const hand = { micros };
  const clock: SourceClock = {
    now: () => hand.micros,
    speed: () => 1,
    sync: () => {},
    setSpeed: () => {},
    reset: () => {},
  };
  return { clock, hand };
}

/** Lets the view read the clock once more. */
const nextFrame = () => act(() => vi.advanceTimersToNextFrame());

/** The rows under the statuses, each as its label and its value. */
const rows = (): (string | null)[][] => {
  const terms = screen.getAllByRole("term");
  const values = screen.getAllByRole("definition");
  return terms.map((term, index) => [
    term.textContent,
    values[index].textContent,
  ]);
};

/** The status the view marks as the clock's. */
const currentStatus = (): string | null =>
  within(screen.getByRole("list", { name: "Clock status" }))
    .getAllByRole("listitem")
    .filter((item) => item.getAttribute("aria-current") === "true")
    .map((item) => item.textContent)
    .join(", ");

describe("TimeView", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("reads no time, no signal, and four empty rows before any frame", () => {
    render(<TimeView clock={handClock(null).clock} />);
    nextFrame();

    expect(screen.getByText("--:--:--")).toBeInTheDocument();
    expect(currentStatus()).toBe("No signal");
    expect(rows()).toEqual([
      ["Last accepted frame", "—"],
      ["Accepted in a row", "0"],
      ["Offset from the PC clock", "—"],
      ["Local time, US Central", "—"],
    ]);
  });

  it("shows the decoded time at the source clock's present, with its date and rows", () => {
    useAppStore.setState({ clock: locked, decodedFrame });
    // 7.5 s after the frame began.
    render(<TimeView clock={handClock(8_500_000).clock} />);
    nextFrame();

    expect(screen.getByText("19:30:07")).toBeInTheDocument();
    expect(screen.getByText("2026-12-10 · day 344")).toBeInTheDocument();
    expect(currentStatus()).toBe("Locked");
    expect(rows()).toEqual([
      ["Last accepted frame", "19:30 UTC"],
      ["Accepted in a row", "3"],
      ["Offset from the PC clock", "+37 ms"],
      ["Local time, US Central", "13:30:07 CST"],
    ]);
  });

  it("moves on when the source clock reaches the next second, and not before", () => {
    useAppStore.setState({ clock: locked, decodedFrame });
    const { clock, hand } = handClock(8_500_000);
    render(<TimeView clock={clock} />);
    nextFrame();
    expect(screen.getByText("19:30:07")).toBeInTheDocument();

    hand.micros = 8_900_000;
    nextFrame();
    expect(screen.getByText("19:30:07")).toBeInTheDocument();

    hand.micros = 9_100_000;
    nextFrame();
    expect(screen.getByText("19:30:08")).toBeInTheDocument();
    expect(rows().at(-1)).toEqual(["Local time, US Central", "13:30:08 CST"]);
  });

  it("shows the recorded offset when the source is a replay", () => {
    useAppStore.setState({
      clock: locked,
      decodedFrame,
      sourceStatus: {
        source: "replay",
        name: "replay",
        state: "running",
        detail: "",
        speed: 10,
      },
      infoLines: ["# wall 2026-12-10T19:30:10Z 11040000"],
    });
    render(<TimeView clock={handClock(8_500_000).clock} />);
    nextFrame();

    expect(rows()[2]).toEqual(["Recorded offset", "+40 ms"]);
  });
});

describe("ClockStatusLine", () => {
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("names the clock's status, and no signal before any clock message", () => {
    const { rerender } = render(<ClockStatusLine />);
    expect(screen.getByText("No signal")).toBeInTheDocument();

    useAppStore.setState({ clock: { ...locked, status: "holding" } });
    rerender(<ClockStatusLine />);
    expect(screen.getByText("Holding")).toBeInTheDocument();
  });
});
