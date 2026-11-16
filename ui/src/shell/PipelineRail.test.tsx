import { act, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it } from "vitest";
import { useAppStore } from "@/state/store";
import { PipelineRail } from "./PipelineRail";

/** The one highlighted button. It throws when there is none, or more than one. */
const highlighted = () => screen.getByRole("button", { pressed: true });

describe("PipelineRail", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("shows the seven stages in pipeline order", () => {
    render(<PipelineRail />);

    const rail = screen.getByRole("navigation", { name: "Pipeline" });
    const buttons = within(rail).getAllByRole("button");
    const names = [
      "Edges",
      "Pulses",
      "Symbols",
      "Frame sync",
      "Decode",
      "Validate",
      "Clock",
    ];

    expect(buttons).toHaveLength(7);
    names.forEach((name, i) => expect(buttons[i]).toHaveTextContent(name));
  });

  it("shows a dash and a no-data mark on every stage before a source starts", () => {
    render(<PipelineRail />);

    for (const button of screen.getAllByRole("button")) {
      expect(button).toHaveTextContent("—");
      expect(within(button).getByText("no data")).toBeInTheDocument();
    }
  });

  it("follows the store", () => {
    render(<PipelineRail />);

    act(() => {
      useAppStore.getState().dispatch({
        type: "edges",
        seq: 1,
        payload: { items: [{ micros: 2_617_209_411, level: "low" }] },
      });
    });

    const edges = screen.getByRole("button", { name: /Edges/ });
    expect(edges).toHaveTextContent("L 2617209411");
    expect(within(edges).getByText("good")).toBeInTheDocument();
  });

  it("highlights no stage in the overview", () => {
    render(<PipelineRail />);

    expect(screen.queryAllByRole("button", { pressed: true })).toEqual([]);
  });

  it("focuses a stage when its button is clicked, and highlights it", async () => {
    const user = userEvent.setup();
    render(<PipelineRail />);

    await user.click(screen.getByRole("button", { name: /Symbols/ }));

    expect(useAppStore.getState().focus).toBe(3);
    expect(highlighted()).toHaveTextContent("Symbols");
  });

  it("goes back to the overview when the highlighted stage is clicked", async () => {
    const user = userEvent.setup();
    render(<PipelineRail />);
    const symbols = screen.getByRole("button", { name: /Symbols/ });

    await user.click(symbols);
    await user.click(symbols);

    expect(useAppStore.getState().focus).toBeNull();
    expect(screen.queryAllByRole("button", { pressed: true })).toEqual([]);
  });

  it("highlights the stage a key focused", () => {
    render(<PipelineRail />);

    act(() => useAppStore.getState().setFocus(5));

    expect(highlighted()).toHaveTextContent("Decode");
  });
});
