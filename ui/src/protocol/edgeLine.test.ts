import { describe, expect, it } from "vitest";
import { edgeLine } from "./edgeLine";

describe("edgeLine", () => {
  // The examples of valid edge lines in docs/design/serial-protocol.md.
  it.each([
    ["low", 1_203_455, "L 1203455"],
    ["high", 1_403_461, "H 1403461"],
    ["low", 0, "L 0"],
  ] as const)("writes a %s edge at %d as '%s'", (level, micros, line) => {
    expect(edgeLine({ micros, level })).toBe(line);
  });
});
