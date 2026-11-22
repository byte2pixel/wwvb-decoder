import { describe, expect, it } from "vitest";
import { infoRows } from "./infoRows";

describe("infoRows", () => {
  it("has no rows before a line arrives", () => {
    expect(infoRows([], 4)).toEqual([]);
  });

  it("lists the newest lines first, and no more than it is given room for", () => {
    const rows = infoRows(["# a", "# b", "# c"], 2);

    expect(rows.map((row) => row.line)).toEqual(["# c", "# b"]);
  });

  it("gives two equal lines different keys", () => {
    const rows = infoRows(["# dropped 1", "# wall x", "# dropped 1"], 4);

    expect(rows.map((row) => row.line)).toEqual([
      "# dropped 1",
      "# wall x",
      "# dropped 1",
    ]);
    expect(new Set(rows.map((row) => row.key)).size).toBe(3);
  });
});
