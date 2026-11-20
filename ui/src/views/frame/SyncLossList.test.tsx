import { render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { useAppStore } from "@/state/store";
import { SyncLossList } from "./SyncLossList";

describe("SyncLossList", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("says so while frame sync has not been lost", () => {
    render(<SyncLossList />);

    expect(screen.getByText("No sync loss yet")).toBeInTheDocument();
    expect(screen.queryByRole("list")).not.toBeInTheDocument();
  });

  it("lists each loss with its number and the host's reason, newest first", () => {
    useAppStore.setState({
      syncLosses: [
        { number: 3, reason: "gap 2000 ms before second 42" },
        { number: 4, reason: "expected bit at second 31, got Marker" },
      ],
    });
    render(<SyncLossList />);

    expect(
      within(screen.getByRole("list", { name: "Sync losses" }))
        .getAllByRole("listitem")
        .map((item) => [...item.children].map((child) => child.textContent)),
    ).toEqual([
      ["#4", "expected bit at second 31, got Marker"],
      ["#3", "gap 2000 ms before second 42"],
    ]);
    expect(screen.queryByText("No sync loss yet")).not.toBeInTheDocument();
  });
});
