import { render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { Panel } from "./Panel";

function Broken(): never {
  throw new Error("lanes exploded");
}

describe("Panel", () => {
  it("is a region named by its title and shows its children", () => {
    render(
      <Panel title="Signal" stages={[1]}>
        lanes
      </Panel>,
    );

    const region = screen.getByRole("region", { name: "Signal" });

    expect(region).toHaveTextContent("lanes");
  });

  it("shows a chip for each stage it covers", () => {
    render(
      <Panel title="Signal" stages={[1, 2, 3, 4]}>
        lanes
      </Panel>,
    );

    const chips = within(
      screen.getByRole("list", { name: "Stages" }),
    ).getAllByRole("listitem");

    expect(chips.map((chip) => chip.textContent)).toEqual(["1", "2", "3", "4"]);
  });

  it("shows its state line", () => {
    render(
      <Panel title="Frame" stages={[4]} state="Searching">
        cells
      </Panel>,
    );

    expect(screen.getByRole("region", { name: "Frame" })).toHaveTextContent(
      "Searching",
    );
  });

  it("shows an error in the panel whose view throws and keeps the others", () => {
    // React logs the error it caught; keep the test output quiet.
    const consoleError = vi
      .spyOn(console, "error")
      .mockImplementation(() => {});

    render(
      <>
        <Panel title="Signal" stages={[1, 2, 3, 4]}>
          <Broken />
        </Panel>
        <Panel title="Time" stages={[7]}>
          clock
        </Panel>
      </>,
    );

    const signal = screen.getByRole("region", { name: "Signal" });
    expect(within(signal).getByRole("alert")).toHaveTextContent(
      "lanes exploded",
    );
    expect(screen.getByRole("region", { name: "Time" })).toHaveTextContent(
      "clock",
    );

    consoleError.mockRestore();
  });

  it("takes extra classes for its place in the page grid", () => {
    render(
      <Panel title="Time" stages={[7]} className="row-span-2">
        clock
      </Panel>,
    );

    expect(screen.getByRole("region", { name: "Time" })).toHaveClass(
      "row-span-2",
    );
  });
});
