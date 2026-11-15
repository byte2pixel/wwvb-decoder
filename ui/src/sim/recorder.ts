import type { HostMessage } from "@/protocol/messages";

declare global {
  interface Window {
    wwvbRecorder?: Recorder;
  }
}

export interface Recorder {
  /** Clears the recording and starts collecting. */
  start(): void;
  /** A handler for bridge.onMessage. Ignored until start(). */
  add(message: HostMessage): void;
  /** The recording so far, one JSON object per line: the envelope plus t, ms since start(). */
  dump(): string;
}

export function createRecorder(wallMs: () => number): Recorder {
  let startMs: number | null = null;
  const messages: Array<string> = [];
  return {
    start(): void {
      startMs = wallMs();
      messages.length = 0;
    },
    add(message: HostMessage): void {
      if (startMs === null) return;
      const t = Math.round(wallMs() - startMs);
      messages.push(JSON.stringify({ t, ...message }));
    },
    dump(): string {
      return messages.join("\n");
    },
  };
}
