import { act, render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import type { Edge, PinLevel } from "@/protocol/messages";
import { initialState } from "@/state/appState";
import { useAppStore } from "@/state/store";
import { EdgeLog } from "./EdgeLog";

const edge = (micros: number, level: PinLevel): Edge => ({ micros, level });

/** What each line of the log reads, top to bottom. */
const lines = (): (string | null)[] =>
  within(screen.getByRole("list", { name: "Edges" }))
    .getAllByRole("listitem")
    .map((item) => item.textContent);

// The store is one module-level object shared by every test.
beforeEach(() => {
  useAppStore.setState(initialState);
});

describe("EdgeLog", () => {
  it("says there are no edges yet before the first", () => {
    render(<EdgeLog />);

    expect(screen.getByText("No edges yet")).toBeInTheDocument();
    expect(screen.queryByRole("listitem")).toBeNull();
  });

  it("lists the edges newest first, as the serial protocol writes them", () => {
    useAppStore.setState({
      edges: [
        edge(12_000_000, "high"),
        edge(12_200_412, "low"),
        edge(13_000_003, "high"),
      ],
    });
    render(<EdgeLog />);

    expect(lines()).toEqual(["H 13000003", "L 12200412", "H 12000000"]);
  });

  it("keeps the newest 50", () => {
    const edges = Array.from({ length: 60 }, (_, index) =>
      edge(index * 1_000, index % 2 === 0 ? "high" : "low"),
    );
    useAppStore.setState({ edges });
    render(<EdgeLog />);

    const shown = lines();

    expect(shown).toHaveLength(50);
    expect(shown.at(0)).toBe("L 59000");
    expect(shown.at(-1)).toBe("H 10000");
  });

  it("adds a line at the top when an edge arrives", () => {
    useAppStore.setState({ edges: [edge(12_000_000, "high")] });
    render(<EdgeLog />);

    act(() =>
      useAppStore.getState().dispatch({
        type: "edges",
        seq: 1,
        payload: { items: [edge(12_200_412, "low")] },
      }),
    );

    expect(lines()).toEqual(["L 12200412", "H 12000000"]);
  });
});
