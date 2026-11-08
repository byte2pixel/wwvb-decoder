# WebView2 messages

The host (C#, WPF) and the page (React in WebView2) talk over WebView2 web messages, as ADR-0003 decides. The host pushes one message per stage output with `CoreWebView2.PostWebMessageAsJson`; the page listens for `message` on `window.chrome.webview` and sends commands back with `postMessage`. This document is the contract between the two sides. A TypeScript developer can write the page's types from it without reading C#.

| Side | Implemented in | Role |
| --- | --- | --- |
| Host bridge | `WwvbDecoder.Host`, `Bridge/WebMessageBridge.cs` and `Bridge/CommandHandler.cs` (construction plan Step 8) | Serializes stage events, parses commands |
| Page bridge | `ui/src/protocol/messages.ts` and `bridge.ts` (Step 8) | Validates envelopes with zod, sends commands |
| Examples | [`message-examples/`](message-examples/) | One JSON file per message type and per command. Both test suites assert against them |

The C# types behind the payloads are in [`pipeline-contracts.md`](pipeline-contracts.md). When that document and this one disagree, this one is wrong: payloads are derived from the records mechanically, by the rules below.

## Serialization rules

Both sides use JSON with these conventions. The host configures `System.Text.Json` to produce them; the page's zod schemas expect them.

| C# | JSON | Note |
| --- | --- | --- |
| Property `FrameStartMicros` | `"frameStartMicros"` | camelCase names |
| Enum member `SymbolKind.Marker` | `"marker"` | Enums are camelCase strings, never numbers |
| `ulong` microseconds | number | Pico timestamps stay below 2^53 for over 285 years of uptime, so they are exact in a JavaScript `number` |
| `int`, `bool` | number, boolean | |
| `DateTimeOffset` | string, ISO-8601 UTC with `Z` | `"2026-12-10T19:30:00Z"`. Always UTC, always `Z`; `System.Text.Json` writes `+00:00` by default, so the host registers a converter |
| `TimeSpan` | number of milliseconds | Property name ends in `Ms`: `OffsetFromPc` becomes `"offsetFromPcMs"`. May be fractional and negative |
| `ImmutableArray<T>` | array | |
| Nullable (`T?`) | `null` when absent | The property is always present |
| Closed record hierarchy (`FrameSyncEvent`) | object with a `"kind"` discriminator, the case's fields beside it | See `frameSync` |
| Get-only computed property (`Pulse.EndMicros`) | included | `System.Text.Json` serializes it like any other property |

Unknown properties are ignored on both sides, so either side can add a field without breaking the other. Removing or renaming a field, or changing a type, bumps the protocol number in `hello`.

## Host to page

### Envelope

```json
{ "type": "<type>", "seq": 1, "payload": { } }
```

| Field | Type | Meaning |
| --- | --- | --- |
| `type` | string | One of the eleven types below |
| `seq` | number | Monotonic, starting at 1 when the host starts, shared by every type. A gap means a message was lost; the page may log it |
| `payload` | object | The shape the type selects |

Every message is one JSON object. Edges are batched into one `edges` message; every other type is one message per event.

TypeScript sketch of the envelope:

```ts
type Envelope =
  | { type: "edges"; seq: number; payload: EdgesPayload }
  | { type: "pulse"; seq: number; payload: Pulse }
  | { type: "symbol"; seq: number; payload: Symbol }
  | { type: "frameSync"; seq: number; payload: FrameSyncEvent }
  | { type: "decodedFrame"; seq: number; payload: DecodedFrame }
  | { type: "validation"; seq: number; payload: ValidationResult }
  | { type: "clock"; seq: number; payload: ClockState }
  | { type: "sourceStatus"; seq: number; payload: SourceStatus }
  | { type: "ports"; seq: number; payload: { items: string[] } }
  | { type: "captures"; seq: number; payload: { items: CaptureEntry[] } }
  | { type: "info"; seq: number; payload: { line: string } };
```

### Shared shapes

```ts
type PinLevel = "low" | "high";
type SymbolKind = "zero" | "one" | "marker" | "error";
type Ut1Sign = "plus" | "minus" | "invalid";
type DstStatus = "standard" | "dst" | "beginsToday" | "endsToday";
type ClockStatus = "noSignal" | "acquiring" | "locked" | "holding";
type ValidationCheck =
  | "markers" | "unusedBits" | "errorSymbols" | "bcdDigits" | "minuteRange"
  | "hourRange" | "dayOfYearRange" | "ut1Sign" | "ut1Range" | "consecutive";

interface Edge { micros: number; level: PinLevel }

/** Exactly 60 symbols, indexed by second. */
interface Frame { startMicros: number; endMicros: number; symbols: SymbolKind[] }
```

### `edges` (stage 1)

Pipeline event `EdgeReceived`, batched. The host flushes the batch every 100 ms or when it holds 200 edges, whichever comes first, and never sends an empty batch. Items are in arrival order.

```ts
interface EdgesPayload { items: Edge[] }
```

```json
{
  "type": "edges",
  "seq": 42,
  "payload": {
    "items": [
      { "micros": 12000000, "level": "high" },
      { "micros": 12200412, "level": "low" },
      { "micros": 13000003, "level": "high" }
    ]
  }
}
```

### `pulse` (stage 2)

Pipeline event `PulseMeasured`. `endMicros` is `startMicros + durationMicros`.

```ts
interface Pulse { startMicros: number; durationMicros: number; endMicros: number }
```

```json
{
  "type": "pulse",
  "seq": 43,
  "payload": { "startMicros": 12000000, "durationMicros": 200412, "endMicros": 12200412 }
}
```

### `symbol` (stage 3)

Pipeline event `SymbolClassified`.

```ts
interface Symbol { startMicros: number; durationMicros: number; kind: SymbolKind }
```

```json
{
  "type": "symbol",
  "seq": 44,
  "payload": { "startMicros": 12000000, "durationMicros": 200412, "kind": "zero" }
}
```

### `frameSync` (stage 4)

Pipeline event `FrameSyncChanged`. The payload is a discriminated union on `kind`, one case per `FrameSyncEvent` record. The C# `SlotFilled.Kind` property, the symbol placed, is serialized as `symbol`, because `kind` is the discriminator.

```ts
type FrameSyncEvent =
  | { kind: "searching"; consecutiveMarkers: number }
  | { kind: "slotFilled"; frameStartMicros: number; second: number; symbol: SymbolKind }
  | { kind: "frameComplete"; frame: Frame }
  | { kind: "syncLost"; reason: string };
```

| `kind` | When | Fields |
| --- | --- | --- |
| `searching` | Not synced; counting consecutive markers | `consecutiveMarkers`: 0 or 1 |
| `slotFilled` | A symbol was placed at `second` of the frame that started at `frameStartMicros` | `second`: 0 to 59; `symbol` |
| `frameComplete` | Second 59 was placed; the whole frame follows | `frame` |
| `syncLost` | Sync was lost; a `searching` event follows in the same push | `reason`: display text for people, not for parsing |

```json
{
  "type": "frameSync",
  "seq": 45,
  "payload": { "kind": "slotFilled", "frameStartMicros": 100000000, "second": 1, "symbol": "zero" }
}
```

### `decodedFrame` (stage 5)

Pipeline event `FrameDecoded`. A plain read of the frame with no range checks: `minute` can be 0 to 85, `hour` 0 to 45, `dayOfYear` 0 to 465, `year` 0 to 165 (the two transmitted digits), `ut1Tenths` 0 to 15. `errorSeconds` lists the field seconds that held an `error` symbol, ascending.

```ts
interface DecodedFrame {
  frame: Frame;
  minute: number;
  hour: number;
  dayOfYear: number;
  year: number;
  ut1Sign: Ut1Sign;
  ut1Tenths: number;
  leapYear: boolean;
  leapSecondPending: boolean;
  dst: DstStatus;
  errorSeconds: number[];
}
```

The example is the frame transmitted during 2026-12-10T19:30Z, the plan's test vector, on the synthetic source's timeline (frame start at 1 s of stream time).

```json
{
  "type": "decodedFrame",
  "seq": 46,
  "payload": {
    "frame": {
      "startMicros": 1000000,
      "endMicros": 60800000,
      "symbols": [
        "marker", "zero", "one", "one", "zero", "zero", "zero", "zero", "zero", "marker",
        "zero", "zero", "zero", "one", "zero", "one", "zero", "zero", "one", "marker",
        "zero", "zero", "one", "one", "zero", "zero", "one", "zero", "zero", "marker",
        "zero", "one", "zero", "zero", "zero", "zero", "one", "zero", "one", "marker",
        "zero", "zero", "zero", "zero", "zero", "zero", "zero", "one", "zero", "marker",
        "zero", "one", "one", "zero", "zero", "zero", "zero", "zero", "zero", "marker"
      ]
    },
    "minute": 30,
    "hour": 19,
    "dayOfYear": 344,
    "year": 26,
    "ut1Sign": "plus",
    "ut1Tenths": 0,
    "leapYear": false,
    "leapSecondPending": false,
    "dst": "standard",
    "errorSeconds": []
  }
}
```

### `validation` (stage 6)

Pipeline event `FrameValidated`. `failures` is in `ValidationCheck` order, at most one per check; `failures[0]` is the headline to show. `accepted` is true exactly when `failures` is empty. `detail` is display text; code branches on `check`.

```ts
interface ValidationFailure { check: ValidationCheck; detail: string }
interface ValidationResult { frame: DecodedFrame; accepted: boolean; failures: ValidationFailure[] }
```

The example is the contracts document's worked example 3: the frame after the one above, with the bit at second 6 flipped by noise, so it reads 19:35 instead of 19:31.

```json
{
  "type": "validation",
  "seq": 47,
  "payload": {
    "frame": {
      "frame": {
        "startMicros": 61000000,
        "endMicros": 120800000,
        "symbols": [
          "marker", "zero", "one", "one", "zero", "zero", "one", "zero", "one", "marker",
          "zero", "zero", "zero", "one", "zero", "one", "zero", "zero", "one", "marker",
          "zero", "zero", "one", "one", "zero", "zero", "one", "zero", "zero", "marker",
          "zero", "one", "zero", "zero", "zero", "zero", "one", "zero", "one", "marker",
          "zero", "zero", "zero", "zero", "zero", "zero", "zero", "one", "zero", "marker",
          "zero", "one", "one", "zero", "zero", "zero", "zero", "zero", "zero", "marker"
        ]
      },
      "minute": 35,
      "hour": 19,
      "dayOfYear": 344,
      "year": 26,
      "ut1Sign": "plus",
      "ut1Tenths": 0,
      "leapYear": false,
      "leapSecondPending": false,
      "dst": "standard",
      "errorSeconds": []
    },
    "accepted": false,
    "failures": [
      { "check": "consecutive", "detail": "expected 2026-12-10T19:31Z, got 2026-12-10T19:35Z" }
    ]
  }
}
```

### `clock` (stage 7)

Pipeline event `ClockUpdated`, one per validated frame. The four nullable fields are `null` until the first accepted frame and after a source reset; on a rejected frame they keep the last accepted values. `offsetFromPcMs` is positive when WWVB is ahead of the PC clock. Under replay it compares a recorded frame with today's clock and means little; the recorded offset comes from the capture's `# wall` lines, delivered as `info`.

```ts
interface ClockState {
  status: ClockStatus;
  frameUtc: string | null;
  frameStartMicros: number | null;
  frameEndMicros: number | null;
  offsetFromPcMs: number | null;
  consecutiveAccepted: number;
  rejectedSinceLastAccepted: number;
}
```

```json
{
  "type": "clock",
  "seq": 48,
  "payload": {
    "status": "locked",
    "frameUtc": "2026-12-10T19:30:00Z",
    "frameStartMicros": 1000000,
    "frameEndMicros": 60800000,
    "offsetFromPcMs": 37,
    "consecutiveAccepted": 3,
    "rejectedSinceLastAccepted": 0
  }
}
```

### `sourceStatus`

Not a stage output. The host posts it whenever the source changes state: in reply to `hello`, after every `selectSource` and `stop`, when a run ends on its own or fails, and on every source reset.

```ts
interface SourceStatus {
  source: "serial" | "replay" | "synthetic" | "none";
  name: string;
  state: "stopped" | "running" | "error";
  detail: string;
}
```

| Field | Meaning |
| --- | --- |
| `source` | Which kind of source is selected; `none` before the first `selectSource` and after `stop` |
| `name` | The source's display name: `serial COM5`, `replay`, `synthetic`, or empty for `none` |
| `state` | `running` while the pipeline is pumping; `stopped` after `stop` or when a replay without looping reaches its end; `error` when the source could not start or the run faulted |
| `detail` | Display text. For a serial source the banner line once seen; for an error the exception message; on a source reset `reset: <reason>` with the reason from the contracts document (`open`, `start`, `session`, `loop`, `timestamp went backwards`); otherwise empty or a short description |

Step 8 wires only the synthetic source. Until Steps 10 and 11 land, `selectSource` with `serial` or `replay` answers `state: "error"` and `detail: "not available yet"`.

```json
{
  "type": "sourceStatus",
  "seq": 3,
  "payload": {
    "source": "serial",
    "name": "serial COM5",
    "state": "running",
    "detail": "# wwvb-edge-logger 0.1.0 pin=2"
  }
}
```

### `ports`

Reply to `listPorts`, and sent once after `hello`. Port names sorted, as `SerialPorts.ListPortNames()` returns them.

```json
{ "type": "ports", "seq": 4, "payload": { "items": ["COM3", "COM5"] } }
```

### `captures`

Reply to `listCaptures`, and sent once after `hello`. One item per `*.edges.txt` file in the captures directory, newest `recorded` first. `path` is what `selectSource` takes back. `minutes` is the last edge timestamp minus the first, in minutes, possibly fractional.

```ts
interface CaptureEntry { path: string; recorded: string; minutes: number }
```

```json
{
  "type": "captures",
  "seq": 5,
  "payload": {
    "items": [
      { "path": "captures/2026-11-02T0310Z-home-west-window.edges.txt", "recorded": "2026-11-02T03:10:27Z", "minutes": 487.5 }
    ]
  }
}
```

### `info`

Pipeline event `InfoReceived`: a `#` line from the source, verbatim. From a serial source these are the banner and `# dropped <n>`; from replay also the capture's `# wall` lines, which carry the recorded PC-clock offset.

```json
{ "type": "info", "seq": 49, "payload": { "line": "# wall 2026-11-02T03:11:00Z 14500398" } }
```

## Page to host

A command is one JSON object with a `command` string and its arguments beside it. There is no envelope and no sequence number: commands are rare and the host handles them in order. The host ignores a command it does not recognize and logs it. A command that fails, for example a port that cannot open, is answered with `sourceStatus` in `state: "error"`; the host never throws into the page.

```ts
type Command =
  | { command: "hello"; protocol: number }
  | { command: "selectSource"; source: "serial"; port: string }
  | { command: "selectSource"; source: "replay"; path: string; speed: number }
  | { command: "selectSource"; source: "synthetic"; noise: number }
  | { command: "setReplaySpeed"; speed: number }
  | { command: "setNoise"; noise: number }
  | { command: "setReducedCarrierLevel"; level: PinLevel }
  | { command: "listPorts" }
  | { command: "listCaptures" }
  | { command: "stop" };
```

### `hello`

Sent once when the page has loaded and subscribed. `protocol` is the version of this document the page was built against, currently 1. The host logs a mismatch and carries on. It answers with `sourceStatus`, `ports`, and `captures`, in that order, so a reloaded page recovers the host's state.

```json
{ "command": "hello", "protocol": 1 }
```

### `selectSource`

Stops the current run, if any, builds the requested source, and starts the pipeline on it. The pipeline's stages are cleared by the new source's first event, a reset. The host answers with `sourceStatus`. Three variants, selected by `source`:

| `source` | Arguments | Meaning |
| --- | --- | --- |
| `serial` | `port`: a name from `ports` | Opens the port at 115200 8N1 with DTR asserted |
| `replay` | `path`: a path from `captures`; `speed`: 0 for as fast as possible, otherwise a multiple of real time | Replays the capture, looping at the end |
| `synthetic` | `noise`: 0 to 1 | Generates a clean or noisy signal starting at the current UTC minute |

```json
{ "command": "selectSource", "source": "serial", "port": "COM5" }
```

```json
{ "command": "selectSource", "source": "replay", "path": "captures/2026-11-02T0310Z-home-west-window.edges.txt", "speed": 1 }
```

```json
{ "command": "selectSource", "source": "synthetic", "noise": 0.0 }
```

### `setReplaySpeed`

Changes the replay speed while running; takes effect on the next edge. Ignored unless the current source is replay.

```json
{ "command": "setReplaySpeed", "speed": 10 }
```

### `setNoise`

Changes the synthetic noise level, 0 to 1, while running; takes effect on the next second. Ignored unless the current source is synthetic.

```json
{ "command": "setNoise", "noise": 0.2 }
```

### `setReducedCarrierLevel`

Tells stage 2 which pin level means reduced carrier. Applies to every source, takes effect on the next edge, and discards the pulse in progress. The default is `high`.

```json
{ "command": "setReducedCarrierLevel", "level": "high" }
```

### `listPorts`

Asks for a fresh `ports` message, for example after plugging the Pico in.

```json
{ "command": "listPorts" }
```

### `listCaptures`

Asks for a fresh `captures` message.

```json
{ "command": "listCaptures" }
```

### `stop`

Cancels the current run and disposes the source. The host answers with `sourceStatus` showing `source: "none"`, `state: "stopped"`.

```json
{ "command": "stop" }
```

## Threading

`DecoderPipeline` raises its events synchronously on the pump thread, the thread enumerating the source. The bridge's handlers do only two things there: serialize the event to a string, and queue it. Edges go into the batch; everything else goes straight to the dispatcher. `CoreWebView2.PostWebMessageAsJson` must be called on the WPF dispatcher thread, so the bridge posts through `Dispatcher.BeginInvoke`. The `seq` counter is assigned on the dispatcher, where posting happens, so sequence numbers match posting order.

Commands arrive on the dispatcher in `CoreWebView2.WebMessageReceived`. Starting or stopping a source happens there too; the running pipeline is cancelled through its `CancellationTokenSource` and the new run starts on a thread-pool thread. Options changed by `setNoise`, `setReplaySpeed`, and `setReducedCarrierLevel` are written on the dispatcher and read on the pump thread; the Core types make those reads and writes volatile.

On the page, `window.chrome.webview` delivers `message` events on the main thread, one per posted message, with the parsed object in `event.data`. The page should validate each envelope against its schema and drop, with a console warning, any it cannot parse.
