import { hostMessageSchema, type HostMessage } from "@/protocol/messages.ts";

export interface ProtocolLog {
  entries: HostMessage[]; // newest last, at most 200
  counts: Record<HostMessage["type"], number>;
}

const messageTypes = hostMessageSchema.options.map((o) => o.shape.type.value);

export const emptyLog: ProtocolLog = {
  entries: [],
  counts: Object.fromEntries(
    messageTypes.map((t) => [t, 0]),
  ) as ProtocolLog["counts"],
};

export function appendMessage(
  log: ProtocolLog,
  message: HostMessage,
): ProtocolLog {
  const entries = [...log.entries, message].slice(-200);
  const counts = { ...log.counts };
  counts[message.type] += 1;
  return { entries, counts };
}
