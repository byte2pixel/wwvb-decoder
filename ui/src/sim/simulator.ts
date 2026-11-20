import type { Bridge } from "@/protocol/bridge";
import type { RecordedMessage } from "./recording";
import {
  hostMessageSchema,
  type Command,
  type HostMessage,
} from "@/protocol/messages";
import layoutFixture from "./fixtures/frameLayout.json";

const stopped: HostMessage = {
  type: "sourceStatus",
  seq: 0,
  payload: { source: "none", name: "", state: "stopped", detail: "" },
};

/** A copy of the message example. The host builds the same payload from its layout table. */
const frameLayout = hostMessageSchema.parse(layoutFixture);

const loopGapMs = 1_000;

interface SimulatorOptions {
  /** How many times faster than recorded the stream plays. */
  speed?: number;
  /** The recording to play when the synthetic source is asked for with noise. Without it, `recording` plays. */
  noisy?: readonly RecordedMessage[];
}

/**
 * A bridge that plays a recorded stream in place of the host.
 * `recording` is what a Start plays, unless the options name another for a noisy start.
 */
export function createSimulator(
  recording: readonly RecordedMessage[],
  { speed = 1, noisy = recording }: SimulatorOptions = {},
): Bridge {
  const subscribers: Set<(message: HostMessage) => void> = new Set();
  let next = 1;
  let timers: ReturnType<typeof setTimeout>[] = [];
  const emit = (message: HostMessage) => {
    const stamped =
      message.type === "sourceStatus"
        ? { ...message, payload: { ...message.payload, speed } }
        : message;
    subscribers.forEach((callback) => callback({ ...stamped, seq: next }));
    next++;
  };
  const cancel = () => {
    timers.forEach(clearTimeout);
    timers = [];
  };

  const play = (lines: readonly RecordedMessage[]) => {
    if (lines[0] === undefined) return;

    const first = lines[0].t;
    timers = lines.map(({ t, message }) => {
      return setTimeout(() => emit(message), (t - first) / speed);
    });
    const last = lines.at(-1)?.t ?? 0;
    timers.push(
      setTimeout(() => play(lines), (last - first + loopGapMs) / speed),
    );
  };
  return {
    onMessage: (callback: (message: HostMessage) => void) => {
      subscribers.add(callback);
      return () => subscribers.delete(callback);
    },
    send: (command: Command) => {
      switch (command.command) {
        case "selectSource": {
          cancel();
          play(
            command.source === "synthetic" && command.noise > 0
              ? noisy
              : recording,
          );
          break;
        }
        case "stop": {
          cancel();
          setTimeout(() => emit(stopped), 0);
          break;
        }
        case "hello": {
          setTimeout(() => {
            emit(stopped);
            emit({ type: "ports", seq: 0, payload: { items: [] } });
            emit({ type: "captures", seq: 0, payload: { items: [] } });
            emit(frameLayout);
          }, 0);
          break;
        }
      }
    },
  };
}
