import type { Edge } from "./messages";

/**
 * An edge as the serial protocol writes it.
 * See docs/design/serial-protocol.md
 */
export function edgeLine(edge: Edge): string {
  return `${edge.level === "high" ? "H" : "L"} ${edge.micros}`;
}
