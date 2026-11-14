import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Panel } from "./Panel";

describe("Panel", () => {
  it("is a region named by its title and shows its children", () => {
    render(<Panel title="Signal">lanes</Panel>);

    const region = screen.getByRole("region", { name: "Signal" });

    expect(region).toHaveTextContent("lanes");
  });
});
