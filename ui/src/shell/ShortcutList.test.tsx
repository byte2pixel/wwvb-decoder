import { act, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it } from "vitest";
import { useAppStore } from "@/state/store";
import { ShortcutList } from "./ShortcutList";

describe("ShortcutList", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("stays closed until it is asked for", () => {
    render(<ShortcutList />);

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("lists what each key does", () => {
    render(<ShortcutList />);

    act(() => useAppStore.getState().setDrawer("shortcuts"));

    const list = screen.getByRole("dialog", { name: "Keyboard shortcuts" });
    const keys = within(list)
      .getAllByRole("term")
      .map((term) => term.textContent);
    expect(keys).toEqual([
      "1 to 7",
      "Esc or 0",
      "← → Page Up Page Down",
      "T",
      "L",
      "?",
    ]);
    expect(within(list).getByText("Back to the overview")).toBeInTheDocument();
  });

  it("tells the store when it is closed", async () => {
    const user = userEvent.setup();
    render(<ShortcutList />);
    act(() => useAppStore.getState().setDrawer("shortcuts"));

    await user.click(screen.getByRole("button", { name: "Close" }));

    expect(useAppStore.getState().drawer).toBeNull();
  });
});
