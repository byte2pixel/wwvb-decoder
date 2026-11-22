# WebView2 messages

The host (C#, WPF) and the page (React in WebView2) talk over WebView2 web messages, as ADR-0003 decides. The host pushes one message per stage output with `CoreWebView2.PostWebMessageAsJson`; the page listens for `message` on `window.chrome.webview` and sends commands back with `postMessage`. This document is the contract between the two sides. A TypeScript developer can write the page's types from it without reading C#.

| Side | Implemented in | Role |
| --- | --- | --- |
| Host bridge | `WwvbDecoder.Host`, `Bridge/` (construction plan Step 8): `MessageJson.cs` and `Messages.cs` serialize envelopes, `CommandJson.cs` and `Commands.cs` parse commands, `WebMessageBridge.cs` posts to WebView2 and `Services/SourceController.cs` handles the parsed commands | Serializes stage events, parses commands |
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
| `type` | string | One of the thirteen types below |
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
  | { type: "frameLayout"; seq: number; payload: FrameLayout }
  | { type: "selection"; seq: number; payload: Selection }
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

The host also flushes the batch before every `sourceStatus`, so an edge is never sent after a status that it came before. A `stopped` or `error` status is the last message of its run, and no edge from before a source reset follows the reset. The page depends on this: it stops its source clock on such a status, and a batch arriving afterwards would start the clock again.

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

Not a stage output. The host posts it whenever the source changes state: in reply to `hello`, after every `selectSource` and `stop`, when a run ends on its own or fails, on every source reset, when `setReplaySpeed` changes the speed of a running replay, and when a warning appears or goes away.

```ts
interface SourceStatus {
  source: "serial" | "replay" | "synthetic" | "none";
  name: string;
  state: "stopped" | "running" | "error";
  detail: string;
  speed: number;
  warning: string;
}
```

| Field | Meaning |
| --- | --- |
| `source` | Which kind of source is selected; `none` before the first `selectSource` and after `stop` |
| `name` | The source's display name: `serial COM5`, `replay`, `synthetic`, or empty for `none` |
| `state` | `running` while the pipeline is pumping; `stopped` after `stop` or when a replay without looping reaches its end; `error` when the source could not start or the run faulted |
| `detail` | Display text. For a serial source the banner line once seen; for an error the exception message; on a source reset `reset: <reason>` with the reason from the contracts document (`open`, `start`, `session`, `loop`, `timestamp went backwards`); otherwise empty or a short description |
| `speed` | How fast the source's time runs against real time. While a replay is the source it is the replay's speed, 0 for as fast as possible. In every other status it is 1. The page runs its source clock at this speed |
| `warning` | Display text for something the presenter should know and may have to act on, or empty. The page shows it as a banner |

The page has no other way to learn the speed, and a reloaded page gets it back with the reply to `hello`. A page that receives a status without `speed` takes it as 1.

`detail` belongs to one status: the next reset or banner line replaces it. A `warning` belongs to the run. Every status of the run carries it, through source resets, the banner line, and a change of speed, until its cause is gone or the run ends. The status that reports the end of the run is the last to carry it. A status that starts a run carries a warning only when the host has one to give at that moment, and the status that answers `stop` carries none. A page that receives a status without `warning` takes it as empty.

| Warning | When | Until |
| --- | --- | --- |
| `<port> was not found. Replaying the newest capture.`, and the three like it in [What the host remembers](#what-the-host-remembers) | At launch, when the host cannot start the source it remembers | The run ends |
| `The reduced-carrier level looks inverted. Try the other level in the signal settings.` | More than 20 markers start within 60 s of the source's time, with no frame completed among them | A frame is completed, `setReducedCarrierLevel` arrives, the source resets, or the run ends |

The texts are for people. The page shows them and does not parse them.

A status carries one warning. While the level looks inverted its warning takes the place of the launch warning, and the launch warning is back when it goes.

**Why 20 markers.** A single pulse cannot show that the level is inverted: 0.2 s of reduced carrier in a second is 0.8 s of full carrier, so every 0 reads as a marker and every marker as a 0. A frame has seven markers and some forty zeros. Measured on 2026-11-21: with the right level, the most markers within 60 s and no frame among them was 11, on the synthetic source at every noise from 0 to 1; with the level inverted it was 33 to 42, on the overnight capture and on the synthetic source, and no frame was completed in 8 h 45 min. The time is the source's, so a replay at 60× is judged over one second of the PC's time. Once the level looks inverted it stays so until one of the four things in the table happens, so the warning does not come and go with a noisy signal.

```json
{
  "type": "sourceStatus",
  "seq": 3,
  "payload": {
    "source": "serial",
    "name": "serial COM5",
    "state": "running",
    "detail": "# wwvb-edge-logger 0.1.0 pin=2",
    "speed": 1,
    "warning": ""
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

### `frameLayout`

Not a stage output. Sent once after `hello`. It carries the frame layout of the contracts document as `WwvbFrameLayout` holds it, so the page can draw bit weights and field brackets without a copy of the layout in TypeScript. No command asks for it again, and the layout does not change while the host runs, so the page keeps it across source resets.

```ts
type SecondRole =
  | "marker" | "unused" | "minute" | "hour" | "dayOfYear" | "ut1Sign"
  | "ut1Correction" | "year" | "leapYear" | "leapSecondPending" | "dst";

/** Both arrays hold exactly 60 items, indexed by second. */
interface FrameLayout { roles: SecondRole[]; weights: (number | null)[] }
```

| Field | Meaning |
| --- | --- |
| `roles` | `WwvbFrameLayout.Roles`: what each second carries. A field is the seconds that share its role, and they need not be adjacent: the day of year runs from second 22 to second 33 across an unused second and a marker |
| `weights` | The weight of the second's bit as the layout table writes it: 40, not 4, for the first minute bit. The UT1 correction's weights, 8 4 2 1, are tenths of a second. `null` for a second without a weight: the markers, the unused seconds, the UT1 sign, the two flags, and the DST pair |

The payload is not one of the pipeline's records. The host builds it from `WwvbFrameLayout`: `roles` is `Roles`, and `weights` takes each `LayoutBit` in `Digits` and `Ut1Correction` and puts its `Weight` at its `Second`.

```json
{
  "type": "frameLayout",
  "seq": 6,
  "payload": {
    "roles": [
      "marker", "minute", "minute", "minute", "unused", "minute", "minute", "minute", "minute", "marker",
      "unused", "unused", "hour", "hour", "unused", "hour", "hour", "hour", "hour", "marker",
      "unused", "unused", "dayOfYear", "dayOfYear", "unused", "dayOfYear", "dayOfYear", "dayOfYear", "dayOfYear", "marker",
      "dayOfYear", "dayOfYear", "dayOfYear", "dayOfYear", "unused", "unused", "ut1Sign", "ut1Sign", "ut1Sign", "marker",
      "ut1Correction", "ut1Correction", "ut1Correction", "ut1Correction", "unused", "year", "year", "year", "year", "marker",
      "year", "year", "year", "year", "unused", "leapYear", "leapSecondPending", "dst", "dst", "marker"
    ],
    "weights": [
      null, 40, 20, 10, null, 8, 4, 2, 1, null,
      null, null, 20, 10, null, 8, 4, 2, 1, null,
      null, null, 200, 100, null, 80, 40, 20, 10, null,
      8, 4, 2, 1, null, null, null, null, null, null,
      8, 4, 2, 1, null, 80, 40, 20, 10, null,
      8, 4, 2, 1, null, null, null, null, null, null
    ]
  }
}
```

### `selection`

Not a stage output. Sent once after `hello`, as the last message of the reply. It carries what the host remembers of the page's choices, so the app bar of a page that has just loaded shows what the host is running, or ran last, and not the page's own defaults. [What the host remembers](#what-the-host-remembers) says where the values come from.

```ts
interface Selection {
  source: "serial" | "replay" | "synthetic";
  port: string | null;
  capture: string | null;
  speed: number;
  noise: number;
  reducedCarrierLevel: PinLevel;
}
```

| Field | Meaning |
| --- | --- |
| `source` | The source the app bar shows as chosen: the one last started. `synthetic` when none ever was. Never `none` |
| `port` | The port of the last serial source, or `null` when there was none. It need not be in `ports`: the logger may be unplugged |
| `capture` | The `path`, as `captures` gives it, of the capture last replayed. `null` when there was none, or when its file is no longer in the captures directory |
| `speed` | The replay speed last asked for, by `selectSource` or `setReplaySpeed`. 1 when none was |
| `noise` | The synthetic noise last asked for, by `selectSource` or `setNoise`. 0 when none was |
| `reducedCarrierLevel` | The level stage 2 is using |

Two rules keep it small:

- The host sends it in reply to `hello` and at no other time. Every later change comes from the page, in a command, so the page already has it, and an echo arriving late would move a slider back under the presenter's hand.
- At launch the host may start something other than what it remembers, a capture where the logger is unplugged. `source` and `capture` then describe what it is about to start, because that is what the app bar should show.

```json
{
  "type": "selection",
  "seq": 7,
  "payload": {
    "source": "replay",
    "port": "COM5",
    "capture": "captures/2026-11-02T0310Z-home-west-window.edges.txt",
    "speed": 10,
    "noise": 0.1,
    "reducedCarrierLevel": "high"
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

Sent once when the page has loaded and subscribed. `protocol` is the version of this document the page was built against, currently 1. The host logs a mismatch and carries on. It answers with `sourceStatus`, `ports`, `captures`, `frameLayout`, and `selection`, in that order, so a reloaded page recovers the host's state.

The first `hello` after the host starts does one thing more. When the reply has been sent, the host starts the source it remembers, as [What the host remembers](#what-the-host-remembers) describes, and the page sees that as the `sourceStatus` of any start. It waits for `hello` so that the page is there to see the run from its first edge. A later `hello`, from a reloaded page, starts nothing.

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

A source that cannot start leaves nothing running, and the answer is a `sourceStatus` in `state: "error"` whose `detail` says why:

- **`replay`.** The file must be inside the host's captures directory, as every `path` in `captures` is. A path outside it is refused with `Not a capture in the captures directory.` A file that cannot be read, or is not a valid capture, is refused with the reader's message. A `speed` below 0 is refused too.
- **`serial`.** The port opens when the run starts to read it, so `sourceStatus` first says `running`, and a port that cannot open follows as `error` a moment later. Once the logger's banner line arrives, `detail` holds it: `# wwvb-edge-logger 0.1.0 pin=2`. It confirms that the port belongs to the logger.
- **`synthetic`.** A `noise` outside 0 to 1 is refused.

The host does not apply a capture's `# reduced-carrier-level` line. `setReducedCarrierLevel` is the only thing that changes the level.

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

Changes the replay speed while running; takes effect on the next edge. The host answers with `sourceStatus` carrying the new `speed`. That status has an empty `detail`, so the page does not take it for a source reset. Ignored unless the current source is replay, and then there is no answer.

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

## What the host remembers

The host keeps the page's choices in a file, so that the app starts the way it was left. On the day of a talk that means: start the app, and the receiver is being decoded again without a click.

### The file

`%LocalAppData%\WwvbDecoder\settings.json`, beside the WebView2 user data. `--settings <file>` on the command line names another file, which is how a test run keeps away from the real one.

```json
{
  "source": "replay",
  "port": "COM5",
  "capture": "2026-11-02T0310Z-home-west-window.edges.txt",
  "speed": 10,
  "noise": 0.1,
  "reducedCarrierLevel": "high",
  "running": true
}
```

The first six are the fields of [`selection`](#selection), with one difference: `capture` is the file's name and not its path. The folder that holds the app can be copied to another place, or to the laptop, and the name still finds the capture in the captures directory beside it. `running` says whether a source was running when the file was last written.

The file follows the serialization rules at the top of this document. Without a file the values are `synthetic`, `null`, `null`, 1, 0, `high`, and `false`.

- **Reading.** Once, when the host starts. A file that is missing, cannot be read, is not JSON, or holds a value that is not valid gives the defaults, all of them, and a line in the log. Valid means: all seven properties are there, `source` is one of the three, `port` and `capture` are strings or `null`, `speed` is a finite number of 0 or more, `noise` is from 0 to 1, `reducedCarrierLevel` is `high` or `low`, and `running` is `true` or `false`. An enum written as a number is not valid. An unknown property is ignored. Nothing in the file can stop the host from starting.
- **Writing.** In the command that changed a value, before the command returns. A file that cannot be written is logged, and the command carries on.

### What changes it

| Command | The host remembers |
| --- | --- |
| `selectSource` with `serial` | `source`, `port`, and that a source is running |
| `selectSource` with `replay` | `source`, `capture` as the name of the file in `path`, `speed`, and that a source is running |
| `selectSource` with `synthetic` | `source`, `noise`, and that a source is running |
| `setReplaySpeed` | `speed`, while a replay is running. At any other time the command is ignored, and nothing is remembered |
| `setNoise` | `noise`, while the synthetic source is running. At any other time the command is ignored, and nothing is remembered |
| `setReducedCarrierLevel` | `reducedCarrierLevel` |
| `stop` | That no source is running. The six values stay |

- A `selectSource` is remembered when its answer is `running`. One that is refused, a capture that cannot be read or a noise out of range, changes nothing.
- A run that ends on its own or fails changes nothing either. A logger that was unplugged in the middle of a run is still the remembered source, and still counts as running, the next time the host starts.
- What the host starts at launch in place of the remembered source, below, is not written to the file. When the logger is plugged in again, the next launch is live again.

### At launch

The remembered level goes to stage 2 before any source starts.

After its reply to the first `hello`, the host starts the remembered source if one was running. A source that cannot be started as remembered is replaced by the newest capture, the first item of `captures`, at the remembered speed:

| Remembered | What the host finds | It starts | `warning` |
| --- | --- | --- | --- |
| Nothing running | | Nothing | |
| `synthetic` | | The synthetic source at the remembered noise | |
| `serial` | The port is in `ports` | The serial source on that port | |
| `serial` | The port is not in `ports`, and there is a capture | A replay of the newest capture | `COM5 was not found. Replaying the newest capture.` |
| `serial` | The port is not in `ports`, and there is no capture | Nothing | `COM5 was not found, and there is no capture to replay.` |
| `replay` | The capture is in the captures directory | A replay of that capture at the remembered speed | |
| `replay` | The capture is gone, and there is another | A replay of the newest capture | `<name> was not found. Replaying the newest capture.` |
| `replay` | There is no capture | Nothing | `<name> was not found, and there is no capture to replay.` |

- A start at launch is the start a `selectSource` would make, with the same answers. A port that is listed and cannot be opened follows as `error`, as it does for a command. A capture that is listed and cannot be read is answered with `error` at once, and that status carries no warning.
- Nothing at launch is written to the file, whether the host starts the remembered source or the newest capture in its place.
- Where nothing can be started, the warning comes in a `sourceStatus` with `source: "none"` and `state: "stopped"`.
- A port is looked for in `ports` without regard to case. One that is listed is taken as it is: the host does not know that the port is the logger's until the banner line arrives, and the status bar shows that line.
- A file edited by hand can say `serial` with no port, or `replay` with no capture. Either counts as not found, and the warning says `The port` or `The capture` where the name would be.
- The `selection` in that first reply names what is about to start: in the fourth and seventh rows, `replay` and the newest capture. While that capture replays, the `selection` of a later `hello` names it too. `stop`, or any `selectSource`, ends that, and `selection` is what is remembered again.
- The start waits for the first `hello` and happens once. A `selectSource` or a `stop` that arrives before that `hello` cancels it, since a command has then chosen.

## Threading

`DecoderPipeline` raises its events synchronously on the pump thread, the thread enumerating the source. The bridge's handlers do only two things there: serialize the event to a string, and queue it. Edges go into the batch; everything else goes straight to the dispatcher, and a `sourceStatus` sends the waiting batch ahead of itself. The batch is taken and queued under the lock that assigns `seq`, so the batch timer cannot queue it behind the status. `CoreWebView2.PostWebMessageAsJson` must be called on the WPF dispatcher thread, so the bridge posts through `Dispatcher.BeginInvoke`. The `seq` counter is assigned when a message is queued for the dispatcher, and the dispatcher queue is first in, first out, so sequence numbers match posting order.

Commands arrive on the dispatcher in `CoreWebView2.WebMessageReceived`. Starting or stopping a source happens there too; the running pipeline is cancelled through its `CancellationTokenSource` and the new run starts on a thread-pool thread. Options changed by `setNoise`, `setReplaySpeed`, and `setReducedCarrierLevel` are written on the dispatcher and read on the pump thread; the Core types make those reads and writes volatile. The settings file is read when the host starts and written on the dispatcher, in the command that changed it.

While a replay runs, `sourceStatus` is posted from both threads: a source reset from the pump thread, and the answer to `setReplaySpeed` from the dispatcher. The controller reads the source's speed and posts the status under one lock, so the last status posted always carries the current speed.

On the page, `window.chrome.webview` delivers `message` events on the main thread, one per posted message, with the parsed object in `event.data`. The page should validate each envelope against its schema and drop, with a console warning, any it cannot parse.
