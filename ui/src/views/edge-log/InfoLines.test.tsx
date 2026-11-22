import { render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { useAppStore } from "@/state/store";
import { InfoLines } from "./InfoLines";

describe("InfoLines", () => {
  // The store is one module-level object shared by every test.
  beforeEach(() => {
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("says so while the source has sent no line", () => {
    render(<InfoLines />);

    expect(screen.getByText("No info lines yet")).toBeInTheDocument();
    expect(screen.queryByRole("list")).not.toBeInTheDocument();
  });

  it("lists the newest four lines as the source wrote them, newest first", () => {
    useAppStore.setState({
      infoLines: [
        "# wwvb-capture v1",
        "# wwvb-edge-logger 0.1.0 pin=2",
        "# wall 2026-11-02T03:11:00Z 14500398",
        "# dropped 1",
        "# dropped 1",
      ],
    });
    render(<InfoLines />);

    expect(
      within(screen.getByRole("list", { name: "Info lines" }))
        .getAllByRole("listitem")
        .map((item) => item.textContent),
    ).toEqual([
      "# dropped 1",
      "# dropped 1",
      "# wall 2026-11-02T03:11:00Z 14500398",
      "# wwvb-edge-logger 0.1.0 pin=2",
    ]);
    expect(screen.queryByText("No info lines yet")).not.toBeInTheDocument();
  });
});
