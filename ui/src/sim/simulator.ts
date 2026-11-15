import type { Bridge } from "@/protocol/bridge";
import type { RecordedMessage } from "./recording";
import type { Command, HostMessage } from "@/protocol/messages";

const stopped: HostMessage = {
  type: "sourceStatus",
  seq: 0,
  payload: { source: "none", name: "", state: "stopped", detail: "" },
};

const loopGapMs = 1_000;

export function createSimulator(
  recording: readonly RecordedMessage[],
  { speed = 1 }: { speed?: number } = {},
): Bridge {
  const subscribers: Set<(message: HostMessage) => void> = new Set();
  let next = 1;
  let timers: ReturnType<typeof setTimeout>[] = [];
  const emit = (message: HostMessage) => {
    subscribers.forEach((callback) => callback({ ...message, seq: next }));
    next++;
  };
  const cancel = () => {
    timers.forEach(clearTimeout);
    timers = [];
  };

  const play = () => {
    if (recording[0] === undefined) return;

    const first = recording[0].t;
    timers = recording.map(({ t, message }) => {
      return setTimeout(() => emit(message), (t - first) / speed);
    });
    const last = recording.at(-1)?.t ?? 0;
    timers.push(setTimeout(play, (last - first + loopGapMs) / speed));
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
          play();
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
          }, 0);
          break;
        }
      }
    },
  };
}
