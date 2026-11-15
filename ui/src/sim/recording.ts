import { hostMessageSchema, type HostMessage } from "@/protocol/messages";
import { z } from "zod";

export interface RecordedMessage {
  /** Milliseconds since the recording started. */
  t: number;
  message: HostMessage;
}

export function parseRecording(text: string): RecordedMessage[] {
  const lines = text.split(/\r?\n/);
  const result: RecordedMessage[] = [];
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (!line.trim()) continue;
    let obj;
    try {
      obj = JSON.parse(line);
      const timestampSchema = z.object({ t: z.number() });
      const parsed = timestampSchema.parse(obj);
      const message = hostMessageSchema.parse(obj);
      result.push({ t: parsed.t, message });
    } catch {
      throw new Error(`line ${i + 1}`);
    }
  }
  return result;
}
