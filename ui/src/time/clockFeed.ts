import type { HostMessage } from "@/protocol/messages";
import type { SourceClock } from "./sourceClock";

export const feedClock =
  (clock: SourceClock) =>
  (message: HostMessage): void => {
    switch (message.type) {
      case "edges": {
        const lastEdge = message.payload.items.at(-1);
        if (!lastEdge) return;
        clock.sync(lastEdge.micros);
        break;
      }
      case "sourceStatus": {
        clock.setSpeed(message.payload.speed ?? 1);
        if (
          message.payload.detail.startsWith("reset:") ||
          message.payload.state !== "running"
        ) {
          clock.reset();
        }
        break;
      }
    }
  };
