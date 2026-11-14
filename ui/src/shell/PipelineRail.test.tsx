import { act, render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { initialState } from "@/state/appState";
import { useAppStore } from "@/state/store";
import { PipelineRail } from "./PipelineRail";

describe("PipelineRail", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(initialState);
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
});
