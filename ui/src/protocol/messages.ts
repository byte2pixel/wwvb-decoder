import { z } from "zod";

export const pinLevelSchema = z.enum(["low", "high"]);
export type PinLevel = z.infer<typeof pinLevelSchema>;
export const symbolKindSchema = z.enum(["zero", "one", "marker", "error"]);
export type SymbolKind = z.infer<typeof symbolKindSchema>;
export const ut1SignSchema = z.enum(["plus", "minus", "invalid"]);
export type Ut1Sign = z.infer<typeof ut1SignSchema>;
export const dstStatusSchema = z.enum([
  "standard",
  "dst",
  "beginsToday",
  "endsToday",
]);
export type DstStatus = z.infer<typeof dstStatusSchema>;
export const clockStatusSchema = z.enum([
  "noSignal",
  "acquiring",
  "locked",
  "holding",
]);
export type ClockStatus = z.infer<typeof clockStatusSchema>;
export const validationCheckSchema = z.enum([
  "markers",
  "unusedBits",
  "errorSymbols",
  "bcdDigits",
  "minuteRange",
  "hourRange",
  "dayOfYearRange",
  "ut1Sign",
  "ut1Range",
  "consecutive",
]);
export type ValidationCheck = z.infer<typeof validationCheckSchema>;
export const sourceSchema = z.enum(["serial", "replay", "synthetic", "none"]);
export type Source = z.infer<typeof sourceSchema>;
export const sourceStateSchema = z.enum(["stopped", "running", "error"]);
export type SourceState = z.infer<typeof sourceStateSchema>;

export const edgeSchema = z.object({
  micros: z.number(),
  level: pinLevelSchema,
});
export type Edge = z.infer<typeof edgeSchema>;

/** Exactly 60 symbols, indexed by second. */
export const frameSchema = z.object({
  startMicros: z.number(),
  endMicros: z.number(),
  symbols: z.array(symbolKindSchema).length(60),
});
export type Frame = z.infer<typeof frameSchema>;

export const edgesPayloadSchema = z.object({
  items: z.array(edgeSchema),
});
export type EdgesPayload = z.infer<typeof edgesPayloadSchema>;

export const pulseSchema = z.object({
  startMicros: z.number(),
  durationMicros: z.number(),
  endMicros: z.number(),
});
export type Pulse = z.infer<typeof pulseSchema>;
export const symbolSchema = z.object({
  startMicros: z.number(),
  durationMicros: z.number(),
  kind: symbolKindSchema,
});
export type Symbol = z.infer<typeof symbolSchema>;

export const frameSyncEventSchema = z.discriminatedUnion("kind", [
  z.object({ kind: z.literal("searching"), consecutiveMarkers: z.number() }),
  z.object({
    kind: z.literal("slotFilled"),
    frameStartMicros: z.number(),
    second: z.number(),
    symbol: symbolKindSchema,
  }),
  z.object({ kind: z.literal("frameComplete"), frame: frameSchema }),
  z.object({ kind: z.literal("syncLost"), reason: z.string() }),
]);
export type FrameSyncEvent = z.infer<typeof frameSyncEventSchema>;

export const decodedFrameSchema = z.object({
  frame: frameSchema,
  minute: z.number(),
  hour: z.number(),
  dayOfYear: z.number(),
  year: z.number(),
  ut1Sign: ut1SignSchema,
  ut1Tenths: z.number(),
  leapYear: z.boolean(),
  leapSecondPending: z.boolean(),
  dst: dstStatusSchema,
  errorSeconds: z.array(z.number()),
});
export type DecodedFrame = z.infer<typeof decodedFrameSchema>;

export const validationFailureSchema = z.object({
  check: validationCheckSchema,
  detail: z.string(),
});
export type ValidationFailure = z.infer<typeof validationFailureSchema>;
export const validationResultSchema = z.object({
  frame: decodedFrameSchema,
  accepted: z.boolean(),
  failures: z.array(validationFailureSchema),
});
export type ValidationResult = z.infer<typeof validationResultSchema>;

export const clockStateSchema = z.object({
  status: clockStatusSchema,
  frameUtc: z.string().nullable(),
  frameStartMicros: z.number().nullable(),
  frameEndMicros: z.number().nullable(),
  offsetFromPcMs: z.number().nullable(),
  consecutiveAccepted: z.number(),
  rejectedSinceLastAccepted: z.number(),
});
export type ClockState = z.infer<typeof clockStateSchema>;

export const sourceStatusSchema = z.object({
  source: sourceSchema,
  name: z.string(),
  state: sourceStateSchema,
  detail: z.string(),
});
export type SourceStatus = z.infer<typeof sourceStatusSchema>;

export const captureEntrySchema = z.object({
  path: z.string(),
  recorded: z.string(),
  minutes: z.number(),
});
export type CaptureEntry = z.infer<typeof captureEntrySchema>;

export const hostMessageSchema = z.discriminatedUnion("type", [
  z.object({
    type: z.literal("edges"),
    seq: z.number(),
    payload: edgesPayloadSchema,
  }),
  z.object({ type: z.literal("pulse"), seq: z.number(), payload: pulseSchema }),
  z.object({
    type: z.literal("symbol"),
    seq: z.number(),
    payload: symbolSchema,
  }),
  z.object({
    type: z.literal("frameSync"),
    seq: z.number(),
    payload: frameSyncEventSchema,
  }),
  z.object({
    type: z.literal("decodedFrame"),
    seq: z.number(),
    payload: decodedFrameSchema,
  }),
  z.object({
    type: z.literal("validation"),
    seq: z.number(),
    payload: validationResultSchema,
  }),
  z.object({
    type: z.literal("clock"),
    seq: z.number(),
    payload: clockStateSchema,
  }),
  z.object({
    type: z.literal("sourceStatus"),
    seq: z.number(),
    payload: sourceStatusSchema,
  }),
  z.object({
    type: z.literal("ports"),
    seq: z.number(),
    payload: z.object({ items: z.array(z.string()) }),
  }),
  z.object({
    type: z.literal("captures"),
    seq: z.number(),
    payload: z.object({ items: z.array(captureEntrySchema) }),
  }),
  z.object({
    type: z.literal("info"),
    seq: z.number(),
    payload: z.object({ line: z.string() }),
  }),
]);
export type HostMessage = z.infer<typeof hostMessageSchema>;

const selectSourceSchema = z.discriminatedUnion("source", [
  z.object({
    command: z.literal("selectSource"),
    source: z.literal("serial"),
    port: z.string(),
  }),
  z.object({
    command: z.literal("selectSource"),
    source: z.literal("replay"),
    path: z.string(),
    speed: z.number(),
  }),
  z.object({
    command: z.literal("selectSource"),
    source: z.literal("synthetic"),
    noise: z.number(),
  }),
]);

export const commandSchema = z.discriminatedUnion("command", [
  z.object({ command: z.literal("hello"), protocol: z.number() }),
  selectSourceSchema,
  z.object({ command: z.literal("setReplaySpeed"), speed: z.number() }),
  z.object({ command: z.literal("setNoise"), noise: z.number() }),
  z.object({
    command: z.literal("setReducedCarrierLevel"),
    level: pinLevelSchema,
  }),
  z.object({ command: z.literal("listPorts") }),
  z.object({ command: z.literal("listCaptures") }),
  z.object({ command: z.literal("stop") }),
]);
export type Command = z.infer<typeof commandSchema>;
