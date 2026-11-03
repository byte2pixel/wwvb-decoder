# Pipeline contracts

The C# types the host decoder is built from: the edge source, the six host stages, the pipeline that wires them, and the supporting layout, encoder, codec, and sources. Steps 4 to 7, 10, and 11 of the construction plan implement these contracts verbatim: names, shapes, options, defaults, and behavior. A change to anything here is a change to this document first.

| Part | Project | Construction plan step |
| --- | --- | --- |
| Edges, events, ports, frame layout, frame encoder, line codec, synthetic source | `WwvbDecoder.Core` | Step 4 |
| Stage 2 `PulseMeasurement`, stage 3 `SymbolClassifier` | `WwvbDecoder.Core` | Step 5 |
| Stage 4 `FrameSync`, stage 5 `FieldDecoder` | `WwvbDecoder.Core` | Step 6 |
| Stage 6 `FrameValidator`, stage 7 `WwvbClock`, `DecoderPipeline` | `WwvbDecoder.Core` | Step 7 |
| `SerialEdgeSource`, `SerialPorts`, `CaptureFile`, `CaptureWriter` | `WwvbDecoder.Adapters` | Step 10 |
| `ReplayEdgeSource`, `CaptureInfo` (Core); `CaptureCatalog` (Adapters) | both | Step 11 |

Stage 1, edge capture, is the firmware ([`serial-protocol.md`](serial-protocol.md)). The decisions behind the stage split are ADR-0004 (sources), ADR-0005 (seven stages), and ADR-0006 (validation without a checksum). The WebView2 messages that carry these types to the page are Step 3's document, not this one.

## The WWVB time code

Sources, in order of authority:

1. NIST, [Radio Station WWVB](https://www.nist.gov/pml/time-and-frequency-division/time-distribution/radio-station-wwvb), and NIST Special Publication 432, [NIST Time and Frequency Services](https://www.nist.gov/system/files/documents/calibrations/sp432-02.pdf) (2002 edition): the amplitude time code, its frame layout, and the frame reference marker.
2. J. Lowe, [Enhanced WWVB Broadcast Format](https://www.nist.gov/system/files/documents/2017/05/09/NIST-Enhanced-WWVB-Broadcast-Format-1_01-2013-11-06.pdf), NIST, 2013: states that the minute being encoded is the one that has already started, as it always has been in the legacy format.
3. Wikipedia, [WWVB](https://en.wikipedia.org/wiki/WWVB) (secondary): the same layout in one table, used as a cross-check.

**Pulses.** WWVB reduces its 60 kHz carrier at the start of every UTC second. How long the reduction lasts carries one symbol: 0.2 s is a 0, 0.5 s is a 1, and 0.8 s is a marker. Full power returns for the rest of the second.

**Frames.** One frame is 60 symbols, one per second, starting at second 0 of a minute. The frame starts with two consecutive markers: second 59 of the previous frame (P0) and second 0 of this one (Pr). The leading edge of the second-0 marker is the frame's on-time point.

**Minute in progress.** A frame encodes the UTC time at its own second 0: the minute that has just started. A receiver has the whole frame only at the end of second 59, about 59.8 s after the instant it describes. `WwvbClock` corrects for this (stage 7).

### Frame layout

This is the only copy of the layout in this document. `WwvbFrameLayout` is its only copy in code.

| Seconds | Content | Bit weights |
| --- | --- | --- |
| 0, 9, 19, 29, 39, 49, 59 | Markers: Pr at 0, P1 to P5 at 9 to 49, P0 at 59 | 0.8 s pulses |
| 1 to 3, 5 to 8 | Minute | 40 20 10 . 8 4 2 1 |
| 12 to 13, 15 to 18 | Hour | 20 10 . 8 4 2 1 |
| 22 to 23, 25 to 28, 30 to 33 | Day of year | 200 100 . 80 40 20 10 . 8 4 2 1 |
| 36 to 38 | UT1 sign | 1 0 1 is +, 0 1 0 is − |
| 40 to 43 | UT1 correction, in tenths of a second | 8 4 2 1 |
| 45 to 48, 50 to 53 | Year, last two digits | 80 40 20 10 . 8 4 2 1 |
| 55 | Leap year | 1 in a leap year |
| 56 | Leap second pending | 1 when a leap second will be inserted at the end of the current month |
| 57, 58 | DST status, read as (second 57, second 58) | 0 0 standard time, 1 1 DST, 1 0 DST begins today, 0 1 DST ends today |
| 4, 10, 11, 14, 20, 21, 24, 34, 35, 44, 54 | Unused | Always 0 |

The dots in the weights column separate BCD digits. A digit's place value is the smallest weight in its group: 10 for `40 20 10`, 100 for `200 100`, 1 for `8 4 2 1`. A digit's value is the sum of its set bits' weights divided by its place value, so each digit can read 0 to 15. A field's value is the sum of the weights of all its set bits.

On the day DST starts, second 57 changes to 1 at 00:00 UTC and second 58 follows 24 hours later. On the day it ends, second 57 returns to 0 at 00:00 UTC and second 58 follows 24 hours later.

## Conventions

- **Namespace.** Every Core type is in namespace `WwvbDecoder.Core`, whatever folder it lives in (`Domain/`, `Ports/`, `Stages/`, `Sources/`, `Protocol/`, `Pipeline/`; see Step 4). Every Adapters type is in `WwvbDecoder.Adapters`.
- **Time.** Stage logic uses the Pico's edge timestamps: microseconds since the Pico booted, as `ulong`. Durations are `ulong` microseconds. PC time enters only through an injected `TimeProvider`, in `WwvbClock`, the paced sources, and `CaptureWriter`.
- **Stages.** A stage is synchronous, single-threaded, and owns its state. `Push` does all its work before it returns and returns a materialized collection (an array or a `List<T>`), never a lazy iterator, so calling `Push` without enumerating the result still advances the stage.
- **Options.** Options are init-only properties with the defaults given here. Three options change while running: `PulseMeasurement.ReducedCarrierLevel`, `SyntheticEdgeSource.Speed` and `.Noise`, and `ReplayEdgeSource.Speed`. They are written from the UI thread and read on the pump thread, so they use a `volatile` field (`ReducedCarrierLevel`) or `Volatile.Read` and `Volatile.Write` (the `double` options, which cannot be `volatile`).
- **Counters.** Stage counters are `long` properties, written on the pump thread. Read them on the pump thread, for example in a pipeline event handler.
- **Immutability.** Outputs are records. Collections in outputs are `ImmutableArray<T>`. `ImmutableArray<T>` compares by reference, so two records holding equal arrays are not `Equals`. Tests compare arrays with `SequenceEqual`.
- **Display strings.** `FrameSyncEvent.SyncLost.Reason` and `ValidationFailure.Detail` are for people. Tests assert the event case or the `ValidationCheck`, and at most the reason's first word.
- **Arguments.** Public members check their arguments (`ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException`). Every async method takes a `CancellationToken`.

## Edges and sources

```csharp
public enum PinLevel { Low, High }

/// One edge on the receiver's data pin: the pin level after the edge, at a Pico timestamp.
public readonly record struct Edge(ulong Micros, PinLevel Level);

/// What a source yields. Closed hierarchy: the private constructor allows only the three nested cases.
public abstract record EdgeEvent
{
    private EdgeEvent() { }

    public sealed record Sample(Edge Edge) : EdgeEvent;
    public sealed record Reset(string Reason) : EdgeEvent;
    public sealed record Info(string Line) : EdgeEvent;
}

public interface IEdgeSource : IAsyncDisposable
{
    string Name { get; }
    IAsyncEnumerable<EdgeEvent> ReadAsync(CancellationToken ct);
}

public interface IStage<in TIn, out TOut>
{
    IEnumerable<TOut> Push(TIn input);
    void Reset();
}
```

The case is `Sample`, not `Edge`, so it does not collide with the `Edge` struct.

Every `IEdgeSource` follows these rules:

- **First event.** Each enumeration starts with a `Reset`: `Reset("start")` from the synthetic and replay sources, and `Reset("open")` from the serial source once the port is open. The pipeline therefore never needs to reset stages itself when a run starts.
- **Order.** Between two `Reset` events, `Sample` timestamps never decrease. A source that reads a smaller timestamp than the previous `Sample` emits `Reset("timestamp went backwards")` before it. Equal timestamps are allowed.
- **End.** The enumeration ends at the end of the data (replay without `Loop`), when the port closes (serial), or when `ct` is cancelled, which throws `OperationCanceledException`. I/O failures surface as exceptions from the enumeration.
- **One at a time.** A source supports one enumeration at a time. Enumerating again after the previous enumeration ended starts over: replay from its first line, synthetic from `StartUtc`, serial by reopening the port.
- **Name.** A short display name: `synthetic`, `replay`, or `serial COM5`.

## Stage 2: pulse measurement

```csharp
public readonly record struct Pulse(ulong StartMicros, ulong DurationMicros)
{
    public ulong EndMicros => StartMicros + DurationMicros;
}

public sealed class PulseMeasurement : IStage<Edge, Pulse>
{
    public PinLevel ReducedCarrierLevel { get; set; }   // default High; volatile backing field; settable while running
    public ulong MinPulseMicros { get; init; }          // default 50_000

    public long GlitchesDropped { get; }                // reduced-carrier pulses shorter than MinPulseMicros
    public long GapsBridged { get; }                    // full-carrier gaps shorter than MinPulseMicros
    public long UnpairedEdges { get; }                  // edges with the same level as the edge before

    public IEnumerable<Pulse> Push(Edge input);
    public void Reset();
}
```

An **entering** edge has `Level == ReducedCarrierLevel`: reduced carrier starts. A **leaving** edge has the other level: full carrier returns.

State: the start of the pulse in progress (`open`), a finished pulse that is not yet confirmed (`pending`), the level and timestamp of the last edge, and the `ReducedCarrierLevel` those were measured with.

For each edge, in this order:

1. **Order.** If the edge's timestamp is smaller than the last edge's, throw `InvalidOperationException` and leave the state unchanged. Equal timestamps are allowed. The pipeline resets every stage on a source `Reset`, which a source emits before any decrease, so this only fires on a bug.
2. **Polarity change.** If `ReducedCarrierLevel` differs from the value the state was measured with, discard `open`, `pending`, and the last level before going on. A pulse never mixes the two polarities.
3. **Duplicate level.** If the edge has the same level as the last edge, increment `UnpairedEdges` and ignore the edge. The stage resynchronizes on the next edge of the other level. This covers both causes: an edge the firmware lost (`# dropped`), and an edge pair the firmware merged into one line.
4. **Entering edge.**
   - With a `pending` pulse and `edge.Micros - pending.EndMicros < MinPulseMicros`: **bridge**. The gap was a full-carrier glitch, so the pending pulse continues: `open = pending.StartMicros`, `pending` is cleared, `GapsBridged` is incremented.
   - With a `pending` pulse otherwise: **confirm**. Output `pending` if `pending.DurationMicros >= MinPulseMicros`; otherwise drop it and increment `GlitchesDropped`. Then `pending` is cleared and `open = edge.Micros`.
   - Without a `pending` pulse: `open = edge.Micros`.
5. **Leaving edge.**
   - With an `open` pulse: `pending = new Pulse(open, edge.Micros - open)`, and `open` is cleared.
   - Without one (the first edge after construction, `Reset()`, or a polarity change): nothing. Its start was never seen.

Each `Push` outputs zero or one pulse. A pulse comes out at the next entering edge after it ends, normally the top of the next second, so stage 2 lags by up to one second.

`Reset()` discards `open`, `pending`, and the last edge's level and timestamp. It outputs nothing, so the pending pulse is lost; this is what the plan's "`Reset()` flushes it" means. The counters are not reset: they count over the stage's lifetime.

A reduced-carrier glitch that ends less than `MinPulseMicros` before the next second's pulse is bridged into that pulse and lengthens it. This is accepted: the glitch has to land within 50 ms of the top of a second.

## Stage 3: symbol classification

```csharp
public enum SymbolKind { Zero, One, Marker, Error }

public readonly record struct Symbol(ulong StartMicros, ulong DurationMicros, SymbolKind Kind);

public sealed class SymbolClassifier : IStage<Pulse, Symbol>
{
    public ulong MinValidMicros { get; init; }           // default 100_000
    public ulong ZeroOneThresholdMicros { get; init; }   // default 350_000
    public ulong OneMarkerThresholdMicros { get; init; } // default 650_000
    public ulong MaxValidMicros { get; init; }           // default 900_000

    public IEnumerable<Symbol> Push(Pulse input);
    public void Reset();
}
```

Each pulse becomes exactly one symbol with the pulse's start and duration. A threshold belongs to the class above it:

| Duration `d` | Kind | With the defaults |
| --- | --- | --- |
| `d < MinValidMicros` or `d > MaxValidMicros` | `Error` | below 100 ms, or above 900 ms |
| `MinValidMicros <= d < ZeroOneThresholdMicros` | `Zero` | 100 ms to 349.999 ms |
| `ZeroOneThresholdMicros <= d < OneMarkerThresholdMicros` | `One` | 350 ms to 649.999 ms |
| `OneMarkerThresholdMicros <= d <= MaxValidMicros` | `Marker` | 650 ms to 900 ms |

The stage is stateless. `Reset()` does nothing.

## Stage 4: frame sync

```csharp
/// Exactly 60 symbols; the constructor throws ArgumentOutOfRangeException otherwise, including for a default array.
/// Symbols is get-only, so a with-expression cannot replace it; build a changed frame with the constructor.
/// StartMicros is the start of the second-0 symbol; EndMicros is the end of the second-59 symbol.
public sealed record Frame(ulong StartMicros, ulong EndMicros, ImmutableArray<SymbolKind> Symbols);

public abstract record FrameSyncEvent
{
    private FrameSyncEvent() { }

    public sealed record Searching(int ConsecutiveMarkers) : FrameSyncEvent;
    public sealed record SlotFilled(ulong FrameStartMicros, int Second, SymbolKind Kind) : FrameSyncEvent;
    public sealed record FrameComplete(Frame Frame) : FrameSyncEvent;
    public sealed record SyncLost(string Reason) : FrameSyncEvent;
}

public sealed class FrameSync : IStage<Symbol, FrameSyncEvent>
{
    public IEnumerable<FrameSyncEvent> Push(Symbol input);
    public void Reset();
}
```

**Gap.** For every symbol after the first since construction or `Reset()`, the gap is its `StartMicros` minus the previous symbol's `StartMicros`. A gap is in range when it is 500_000 to 1_500_000 µs inclusive.

**Searching** (the initial state). The stage counts consecutive markers, `n`:

- A `Marker` makes `n` one more than before if the previous symbol was also a counted marker and the gap is in range; otherwise `n = 1`. When `n` reaches 2, this marker is second 0: the stage starts a frame at its `StartMicros`, fills second 0, switches to **synced** expecting second 1, and outputs `SlotFilled(start, 0, Marker)`. Otherwise it outputs `Searching(n)`.
- Any other symbol sets `n = 0` and outputs `Searching(0)`.

**Synced**, expecting second `k`. Checks in order:

1. Gap out of range: output `SyncLost("gap <ms> ms before second <k>")`, with `<ms>` the gap in whole milliseconds. Then handle the symbol as in Searching with `n = 0` beforehand, which outputs `Searching(1)` for a marker and `Searching(0)` otherwise.
2. `k` is a marker second and the symbol is `Zero` or `One`: output `SyncLost("expected marker at second <k>, got <Kind>")`, then `Searching(0)`.
3. `k` is not a marker second and the symbol is a `Marker`: output `SyncLost("expected bit at second <k>, got Marker")`, then `Searching(1)`. The offending marker is the first candidate of the next search.
4. Otherwise place the symbol. If `k` is 0, the frame starts at this symbol's `StartMicros`. Output `SlotFilled(frameStart, k, Kind)`. If `k` is 59, also output `FrameComplete(new Frame(frameStart, symbol.StartMicros + symbol.DurationMicros, symbols))` and stay synced expecting second 0 of the next frame. Otherwise expect `k + 1`.

An `Error` symbol is placed wherever it arrives, marker seconds included; `FieldDecoder` and `FrameValidator` deal with it. A missing symbol (a pulse dropped as a glitch, or lost) shows up as a gap near 2 s and loses sync rather than shifting every later second. There is no special case for a leap second.

`Reset()` returns to Searching with `n = 0` and forgets the previous symbol and any partial frame.

Each `Push` outputs one event, or two: `SlotFilled` then `FrameComplete`, or `SyncLost` then `Searching`.

## Stage 5: field decode

```csharp
public enum Ut1Sign { Plus, Minus, Invalid }
public enum DstStatus { Standard, Dst, BeginsToday, EndsToday }

public sealed record DecodedFrame(
    Frame Frame,
    int Minute,
    int Hour,
    int DayOfYear,
    int Year,                          // the two transmitted digits, 0 to 165 before validation
    Ut1Sign Ut1Sign,
    int Ut1Tenths,
    bool LeapYear,
    bool LeapSecondPending,
    DstStatus Dst,
    ImmutableArray<int> ErrorSeconds); // field seconds holding Error, ascending

public sealed class FieldDecoder : IStage<Frame, DecodedFrame>
{
    public IEnumerable<DecodedFrame> Push(Frame input);
    public void Reset();
}
```

One `DecodedFrame` per frame. The decode is a pure read through `WwvbFrameLayout`, with no range checks:

- **Bits.** `One` is 1 and `Zero` is 0. `Error` is read as 0, and its second goes into `ErrorSeconds` if it belongs to a field: anything but a marker or an unused second. A `Marker` in a field second cannot come from `FrameSync`; if one arrives anyway, it is treated as `Error`.
- **Minute, hour, day of year, year.** The sum of the weights of the set bits. Nothing is checked: a units digit of `1 1 1 1` reads as 15.
- **UT1 sign.** Seconds 36, 37, 38 reading `1 0 1` give `Plus`, `0 1 0` give `Minus`, and anything else gives `Invalid`.
- **UT1 tenths.** The sum of the weights of the set bits in seconds 40 to 43, 0 to 15.
- **Flags.** `LeapYear` is second 55, and `LeapSecondPending` is second 56. `Dst` comes from (57, 58): `0 0` `Standard`, `1 1` `Dst`, `1 0` `BeginsToday`, `0 1` `EndsToday`.

The stage is stateless. `Reset()` does nothing.

## Stage 6: validation

```csharp
public enum ValidationCheck
{
    Markers, UnusedBits, ErrorSymbols, BcdDigits, MinuteRange, HourRange,
    DayOfYearRange, Ut1Sign, Ut1Range, Consecutive,
}

public sealed record ValidationFailure(ValidationCheck Check, string Detail);

public sealed record ValidationResult(DecodedFrame Frame, bool Accepted, ImmutableArray<ValidationFailure> Failures);

public sealed class FrameValidator : IStage<DecodedFrame, ValidationResult>
{
    public IEnumerable<ValidationResult> Push(DecodedFrame input);
    public void Reset();
}
```

One `ValidationResult` per frame. Every check runs, in `ValidationCheck` order, and every failure is reported in that order. `Accepted` is true when `Failures` is empty. `Failures[0]` is the headline the UI shows.

| Check | Fails when | Example `Detail` |
| --- | --- | --- |
| `Markers` | A marker second is not `Marker`, or any other second is | `second 19 is Error` |
| `UnusedBits` | An unused second is not `Zero` | `second 4 is One` |
| `ErrorSymbols` | `ErrorSeconds` is not empty | `Error at seconds 6, 31` |
| `BcdDigits` | Any of the nine minute, hour, day-of-year, and year digits, re-derived from the frame through `WwvbFrameLayout`, exceeds 9 | `minute units digit is 12` |
| `MinuteRange` | `Minute > 59` | `minute 60` |
| `HourRange` | `Hour > 23` | `hour 24` |
| `DayOfYearRange` | `DayOfYear` is 0 or above 366, or 366 without `LeapYear` | `day 366 without the leap-year flag` |
| `Ut1Sign` | `Ut1Sign` is `Invalid` | `sign bits 1 1 1` |
| `Ut1Range` | `Ut1Tenths > 9` | `UT1 correction 1.0 s` |
| `Consecutive` | See below | `expected 2026-12-10T19:31Z, got 2026-12-10T19:35Z` |

UT1 tenths are checked by `Ut1Range` only, because UT1 is not one of the BCD fields.

**Consecutive.** The frame's time is `new DateTime(2000 + Year, 1, 1, Hour, Minute, 0, DateTimeKind.Utc).AddDays(DayOfYear - 1)`. Real date arithmetic carries minute, hour, day, and year rollovers. The check compares this frame with the last accepted one, `prev`:

- **Skipped**, which is not a failure, when there is no `prev`; when `prev` started more than 90 s before this frame (`StartMicros` difference above 90_000_000); or when `BcdDigits`, `MinuteRange`, `HourRange`, or `DayOfYearRange` failed, because then the time cannot be built.
- **Passes** when this frame's time is exactly one minute after `prev`'s, and the `StartMicros` difference is 60_000_000 ± 50_000.
- **Leap second.** When `prev.LeapSecondPending` is set and this frame's time is 00:00 on the first day of a month, a `StartMicros` difference of 61_000_000 ± 50_000 also passes.
- **Fails** otherwise.

State: the last accepted `DecodedFrame`, replaced only when a frame is accepted. `Reset()` forgets it; after a source reset, timestamps from before are not comparable.

## Stage 7: clock

```csharp
public enum ClockStatus { NoSignal, Acquiring, Locked, Holding }

public sealed record ClockState(
    ClockStatus Status,
    DateTimeOffset? FrameUtc,          // UTC at second 0 of the last accepted frame
    ulong? FrameStartMicros,
    ulong? FrameEndMicros,
    TimeSpan? OffsetFromPc,            // positive when WWVB is ahead of the PC clock
    int ConsecutiveAccepted,
    int RejectedSinceLastAccepted);

public sealed class WwvbClock : IStage<ValidationResult, ClockState>
{
    public WwvbClock(TimeProvider timeProvider);
    public ClockState Current { get; }   // NoSignal with nulls and zeros before the first Push and after Reset()

    public IEnumerable<ClockState> Push(ValidationResult input);
    public void Reset();
}
```

Each `Push` outputs one `ClockState`, which is also `Current`.

| Input | Counters | `Status` before | `Status` after |
| --- | --- | --- | --- |
| Accepted | `ConsecutiveAccepted` + 1, `RejectedSinceLastAccepted` = 0 | `Locked` or `Holding` | `Locked` |
| Accepted | same | `NoSignal` or `Acquiring` | `Locked` if `ConsecutiveAccepted` ≥ 2, else `Acquiring` |
| Rejected | `ConsecutiveAccepted` = 0, `RejectedSinceLastAccepted` + 1 | `Locked` or `Holding` | `Holding`, or `Acquiring` once `RejectedSinceLastAccepted` reaches 3 |
| Rejected | same | `NoSignal` or `Acquiring` | `Acquiring` |

An accepted frame while `Holding` returns to `Locked` directly. The hold existed because the clock was already locked.

On an accepted frame:

- `FrameUtc` is the validator's frame time as a `DateTimeOffset` with offset zero.
- `FrameStartMicros` and `FrameEndMicros` come from the frame.
- `OffsetFromPc = (FrameUtc + TimeSpan.FromMicroseconds(FrameEndMicros - FrameStartMicros)) - timeProvider.GetUtcNow()`, taken during `Push`. The frame completes about 59.8 s after the instant it encodes, and the first term moves `FrameUtc` to that moment.

On a rejected frame these four values stay as they were, so `Holding` keeps showing the last good time. Local time is derived in the UI from `Dst`.

Under replay, `OffsetFromPc` compares a recorded frame with today's PC clock and means little; the recorded offset comes from `CaptureInfo.WallSamples`.

`Reset()` returns to `NoSignal` with nulls and zero counters.

## Pipeline

```csharp
public sealed class DecoderPipeline
{
    public DecoderPipeline(TimeProvider timeProvider);   // passed to WwvbClock

    public PulseMeasurement PulseMeasurement { get; }
    public SymbolClassifier SymbolClassifier { get; }
    public FrameSync FrameSync { get; }
    public FieldDecoder FieldDecoder { get; }
    public FrameValidator FrameValidator { get; }
    public WwvbClock Clock { get; }

    public event Action<Edge>? EdgeReceived;
    public event Action<Pulse>? PulseMeasured;
    public event Action<Symbol>? SymbolClassified;
    public event Action<FrameSyncEvent>? FrameSyncChanged;
    public event Action<DecodedFrame>? FrameDecoded;
    public event Action<ValidationResult>? FrameValidated;
    public event Action<ClockState>? ClockUpdated;
    public event Action<string>? SourceReset;    // the Reset reason
    public event Action<string>? InfoReceived;   // the Info line

    public Task RunAsync(IEdgeSource source, CancellationToken ct);
}
```

`RunAsync` enumerates `source.ReadAsync(ct)` and handles each event in order:

- **`Sample`.** Raise `EdgeReceived`, then push the edge through stages 2 to 7 depth-first: each output is raised as its stage's event and pushed into the next stage completely before the stage's next output. Only `FrameComplete` events go on to `FieldDecoder`, which receives their `Frame`.
- **`Reset`.** Call `Reset()` on all six stages, in stage order, then raise `SourceReset`.
- **`Info`.** Raise `InfoReceived`.

Rules:

- Events fire synchronously on the pump thread, the thread running the enumeration. Handlers must be quick and must not push into the pipeline. The WebView2 bridge marshals to its own thread.
- An exception from a stage or a handler stops the run, and the returned `Task` faults with it. Cancellation ends the run as a cancelled `Task`. The pipeline never disposes the source; its owner does.
- One run at a time: calling `RunAsync` while a run is in progress throws `InvalidOperationException`. Switching sources means cancelling the run and starting a new one. The new source's first event, a `Reset`, clears the stages.
- Stage options are reached through the stage properties. Only `PulseMeasurement.ReducedCarrierLevel` is set after construction; the init-only options keep their defaults inside the pipeline.

## Frame layout and encoder

```csharp
public enum SecondRole
{
    Marker, Unused, Minute, Hour, DayOfYear, Ut1Sign, Ut1Correction, Year,
    LeapYear, LeapSecondPending, Dst,
}

public readonly record struct LayoutBit(int Second, int Weight);

/// One BCD digit: a dot-separated group of the frame layout table.
public sealed record BcdDigit(SecondRole Field, int PlaceValue, ImmutableArray<LayoutBit> Bits);

public static class WwvbFrameLayout
{
    public const int FrameSeconds = 60;

    public static ImmutableArray<SecondRole> Roles { get; }          // indexed by second, 0 to 59
    public static ImmutableArray<int> MarkerSeconds { get; }
    public static ImmutableArray<int> UnusedSeconds { get; }
    public static ImmutableArray<BcdDigit> Digits { get; }           // the nine digits, in frame order
    public static ImmutableArray<int> Ut1SignSeconds { get; }
    public static ImmutableArray<LayoutBit> Ut1Correction { get; }   // weights in tenths of a second
    public const int LeapYearSecond = 55;
    public const int LeapSecondPendingSecond = 56;
    public static ImmutableArray<int> DstSeconds { get; }            // second 57 first
}
```

The values are exactly the frame layout table. `LayoutBit.Weight` is the weight as the table writes it: 40, not 4, for the first minute bit. `PlaceValue` is 100, 10, or 1. Every second has exactly one role. `FieldDecoder`, `FrameValidator`, and `WwvbFrameEncoder` read the layout only through this class.

```csharp
public static class WwvbFrameEncoder
{
    public static Frame Encode(
        DateTimeOffset utcMinute,
        ulong startMicros,
        Ut1Sign sign,
        int ut1Tenths,
        bool leapSecondPending,
        DstStatus dst);
}
```

`Encode` builds the 60 symbols for the frame transmitted during `utcMinute`, the minute in progress. Rules:

- **Arguments.** `utcMinute` is converted to UTC and must fall exactly on a minute, with zero seconds and sub-seconds; otherwise it throws `ArgumentException`. Its year must be 2000 to 2099. `sign` must be `Plus` or `Minus`, and `ut1Tenths` 0 to 9; otherwise it throws `ArgumentOutOfRangeException`.
- **Symbols.** `Marker` at the marker seconds, `Zero` at the unused seconds, and the minute, hour, day of year, two-digit year, UT1 sign and tenths, and the three flags written through `WwvbFrameLayout`. Set bits are `One` and clear bits `Zero`. The leap-year flag is `DateTime.IsLeapYear(year)`.
- **Times.** `StartMicros = startMicros` and `EndMicros = startMicros + 59_800_000`, the end of the 0.8 s marker at second 59.
- **Only writer.** This is the only code that writes bits into a frame. Tests that need a broken frame encode a good one and replace symbols.

## Line codec

```csharp
public static class EdgeLineCodec
{
    public static bool TryParse(ReadOnlySpan<char> line, [NotNullWhen(true)] out EdgeEvent? evt);
    public static string Format(Edge edge);
}
```

`TryParse` takes one line without its line feed and implements [`serial-protocol.md`](serial-protocol.md):

| Line | Result |
| --- | --- |
| `H` or `L`, one space, then 1 to 20 ASCII digits whose value fits a `ulong` (leading zeros allowed) | `true`, `Sample(new Edge(micros, High or Low))` |
| Exactly `# session` | `true`, `Reset("session")` |
| Any other line starting with `#` | `true`, `Info(line)`, the whole line |
| Anything else: empty, longer than 63 characters, containing a carriage return, a sign, a decimal point, or a second space | `false` (malformed) |

The 63-character limit is the protocol's 64 bytes including the line feed. It applies to every line, `#` lines included.

`Format` returns `H <micros>` or `L <micros>` without a line feed, using invariant digits. `TryParse(Format(edge))` yields `Sample(edge)` for every edge.

## Synthetic source

```csharp
public sealed class SyntheticEdgeSource : IEdgeSource
{
    public SyntheticEdgeSource(TimeProvider timeProvider);

    public string Name { get; }                           // "synthetic"
    public required DateTimeOffset StartUtc { get; init; }
    public double Speed { get; set; }                     // default 1; 0 = unpaced, N = N times real time; Volatile read and write
    public double Noise { get; set; }                     // default 0; 0 to 1; Volatile read and write
    public PinLevel ReducedCarrierLevel { get; init; }    // default High
    public int Seed { get; init; }                        // default 0

    public IAsyncEnumerable<EdgeEvent> ReadAsync(CancellationToken ct);
    public ValueTask DisposeAsync();                      // nothing to release
}
```

`Speed` below 0, or `Noise` outside 0 to 1, throws `ArgumentOutOfRangeException` from the setter. `StartUtc` must be a whole UTC minute from 2000 to 2099. Otherwise the enumeration throws `ArgumentException` when it starts.

**Timeline.**

- **First event.** `Reset("start")`.
- **Seconds.** Stream second `i` covers `i * 1_000_000` to `(i + 1) * 1_000_000` µs.
- **Lead-in.** Stream second 0 is second 59 of the minute before `StartUtc`, a marker, so `FrameSync` can lock onto `StartUtc`'s own frame.
- **Mapping.** Stream second `i ≥ 1` is second `(i - 1) % 60` of minute `StartUtc + (i - 1) / 60`. The frame for `StartUtc` plus `n` minutes therefore starts at `1_000_000 + n * 60_000_000` µs.
- **Symbols.** Each minute's symbols come from `WwvbFrameEncoder.Encode(minute, frameStart, Ut1Sign.Plus, 0, false, DstStatus.Standard)`. `Zero` lasts 200_000 µs, `One` 500_000 µs, and `Marker` 800_000 µs.
- **Edges.** Without noise, each second gives an entering edge at its start (`ReducedCarrierLevel`) and a leaving edge after the symbol's duration (the other level). That is 120 edges per minute.
- **End.** The stream never ends on its own; cancellation stops it.

**Noise.** At the start of each second, with probability `Noise`, read once for that second, the second gets one of four faults, chosen uniformly:

| Fault | What changes | What stage 2 does |
| --- | --- | --- |
| Jitter | The duration changes by a uniform amount in −300_000 to +300_000 µs, clamped to 10_000 to 990_000 | Measures the new duration; stage 3 may misclassify it |
| Gap glitch | A full-carrier gap of 10_000 to 40_000 µs inside the pulse, starting at least 20_000 µs after the pulse starts and ending at least 20_000 µs before it ends | Bridges it (`GapsBridged`) |
| Pulse glitch | A reduced-carrier pulse of 10_000 to 40_000 µs in the full-carrier part, at least 60_000 µs from the pulse before and from the next second | Drops it (`GlitchesDropped`) |
| Dropped edge | The second's entering or leaving edge, chosen uniformly, is omitted | Counts the next same-level edge (`UnpairedEdges`) |

All random draws come from one `new Random(Seed)` per enumeration. The same `Seed` with the same `Noise` gives the same stream.

**Pacing.**

- **`Speed = 0`.** Everything is yielded at once.
- **`Speed > 0`.** Before yielding each edge after the first, the source waits `(edge.Micros - previousEdge.Micros) / Speed` µs with `Task.Delay(TimeSpan, timeProvider, ct)`. `Speed` is read for each delay, so a change takes effect on the next one.
- **Example at `Speed = 1`.** `Reset("start")` and `H 0` arrive at once. `L 800000`, the end of the lead-in marker, arrives 800 ms later, and `H 1000000` 200 ms after that.

## Replay source

```csharp
public readonly record struct WallSample(DateTimeOffset Utc, ulong Micros);

public sealed record CaptureInfo(
    DateTimeOffset Recorded,
    string Source,
    PinLevel? ReducedCarrierLevel,
    ImmutableArray<WallSample> WallSamples)
{
    public static CaptureInfo Parse(IReadOnlyList<string> lines);   // FormatException if not a valid v1 capture
}

public sealed class ReplayEdgeSource : IEdgeSource
{
    public ReplayEdgeSource(IReadOnlyList<string> lines, TimeProvider timeProvider);   // parses Info; FormatException

    public string Name { get; }                // "replay"
    public CaptureInfo Info { get; }
    public double Speed { get; set; }          // default 1; 0 = unpaced; Volatile read and write
    public bool Loop { get; init; }            // default false
    public long MalformedLines { get; }

    public IAsyncEnumerable<EdgeEvent> ReadAsync(CancellationToken ct);
    public ValueTask DisposeAsync();           // nothing to release
}
```

`CaptureInfo.Parse` implements the header in [`capture-format.md`](capture-format.md):

- **Version.** The first line must be exactly `# capture v1`.
- **Required lines.** `Recorded` comes from the first `# recorded <ISO-8601 UTC>` line and `Source` from the first `# source <text>` line, the text after the key. Both are required.
- **Polarity.** `ReducedCarrierLevel` comes from `# reduced-carrier-level H` or `L`, if present. Any other value is an error.
- **Wall samples.** `WallSamples` holds every well-formed `# wall <ISO-8601 UTC> <micros>` line, in file order. A malformed `# wall` line is left out of `WallSamples` but still replays as `Info`.
- **Errors.** A missing required line, an unparseable value, or a wrong first line throws `FormatException`.

`ReadAsync` yields:

1. `Reset("start")`.
2. Each line after the first, in order:
   - The header keys `# capture`, `# recorded`, `# source`, and `# reduced-carrier-level` are metadata and are skipped.
   - `# wall` lines become `Info(line)`.
   - Every other line goes through `EdgeLineCodec.TryParse`. A malformed line increments `MalformedLines` and is skipped.
3. Before a `Sample` whose timestamp is smaller than the previous `Sample`'s since the last `Reset`, `Reset("timestamp went backwards")`.
4. At the end: with `Loop`, `Reset("loop")` and start again at step 2. Without it, the enumeration completes.

**Pacing.** With `Speed > 0`, the source waits `(micros - previousMicros) / Speed` µs before each `Sample`, using `Task.Delay(TimeSpan, timeProvider, ct)`. `Speed` is read for each delay. Every `Reset`, from `# session` lines included, clears the baseline, so the next `Sample` follows without a delay. `Info` and `Reset` events are never delayed. With `Speed = 0`, nothing waits.

## Adapters

These live in `WwvbDecoder.Adapters`, which holds the file system and `System.IO.Ports`; Core never references either.

```csharp
public sealed class SerialEdgeSource : IEdgeSource
{
    public SerialEdgeSource(string portName);                // port opened by ReadAsync: 115200, 8N1, DtrEnable = true
    public SerialEdgeSource(string name, TextReader reader); // any line source; tests use a StringReader

    public string Name { get; }                              // "serial COM5", or the given name
    public long MalformedLines { get; }
    public event Action<string>? RawLineReceived;            // every line as read, before parsing

    public IAsyncEnumerable<EdgeEvent> ReadAsync(CancellationToken ct);
    public ValueTask DisposeAsync();                         // closes the port
}

public static class SerialPorts
{
    public static IReadOnlyList<string> ListPortNames();     // SerialPort.GetPortNames(), sorted
}
```

`SerialEdgeSource` yields `Reset("open")` once the port is open. Then, for each line: it raises `RawLineReceived`, parses the line with `EdgeLineCodec`, counts and skips malformed lines, and applies the backwards-timestamp rule exactly as `ReplayEdgeSource` does. It stops when the reader returns end of stream or `ct` is cancelled. The recorder subscribes to `RawLineReceived`, because a capture keeps every raw line, malformed ones included, and the parsed events do not carry them.

```csharp
public static class CaptureFile
{
    public static IReadOnlyList<string> Open(string path);   // whole file in memory, split on "\n" only
    public static TextWriter Create(string path);            // UTF-8 without BOM, NewLine "\n"; throws if the file exists
    public static string FileName(DateTimeOffset recordedUtc, string label);   // "2026-11-02T0310Z-home-west-window.edges.txt"
}

public sealed class CaptureWriter
{
    public CaptureWriter(TextWriter writer, TimeProvider timeProvider);
    public void WriteHeader(DateTimeOffset recorded, string source, PinLevel? reducedCarrierLevel);
    public void WriteLine(string rawLine);
}

public sealed record CaptureEntry(string Path, DateTimeOffset Recorded, TimeSpan Duration);

public static class CaptureCatalog
{
    public static IReadOnlyList<CaptureEntry> List(string directory);
}
```

- **`CaptureFile.Open`.** Splits on `\n` only, so a stray `\r` stays in its line and the codec reports it as malformed. A final empty line after the last `\n` is dropped.
- **`CaptureFile.FileName`.** Follows the naming rule in `capture-format.md`. It throws `ArgumentException` for a label with anything but lowercase letters, digits, and hyphens.
- **`CaptureWriter.WriteHeader`.** Writes the header lines in `capture-format.md` order. It must be called once, before any `WriteLine`; otherwise it throws `InvalidOperationException`.
- **`CaptureWriter.WriteLine`.** Writes the raw line verbatim plus `\n`, and remembers the timestamp of the last edge line. After writing, if an edge has been seen and `timeProvider.GetUtcNow()` has reached the next wall time, it writes `# wall <UTC, yyyy-MM-ddTHH:mm:ssZ> <micros>` and moves the next wall time to the following minute boundary. The first wall time is the first minute boundary after the first edge line. The caller owns, flushes, and disposes the `TextWriter`.
- **`CaptureCatalog.List`.** Covers `*.edges.txt` in `directory`, newest `Recorded` first. `Duration` is the last edge timestamp minus the first, or zero with fewer than two edges. A file whose header fails `CaptureInfo.Parse` is left out.

The Tools commands (`ports`, `record`, `decode`) are specified in Steps 10 and 11 of the construction plan; they define no types of their own.

## Worked examples

Defaults throughout: `ReducedCarrierLevel = High`, `MinPulseMicros = 50_000`, and the classifier thresholds above.

### 1. A clean "0" second through stages 2 and 3

The first three edge lines of the example in `serial-protocol.md`, after a `# session` reset:

| Edge | Stage 2 rule | Stage 2 state after | Stage 2 output | Stage 3 output |
| --- | --- | --- | --- | --- |
| `H 12000000` | Entering, nothing pending | `open = 12_000_000` | none | none |
| `L 12200412` | Leaving, pulse open | `pending = Pulse(12_000_000, 200_412)` | none | none |
| `H 13000003` | Entering; gap `13_000_003 - 12_200_412 = 799_591 ≥ 50_000`, so confirm; `200_412 ≥ 50_000`, so emit | `open = 13_000_003` | `Pulse(12_000_000, 200_412)` | `Symbol(12_000_000, 200_412, Zero)`: `100_000 ≤ 200_412 < 350_000` |

The 0.2 s pulse ends at 12.200 s but leaves stage 2 only at 13.000 s, when the next entering edge shows the gap after it is longer than 50 ms. Until then it could still have been the first half of a pulse split by a full-carrier glitch.

If a 20 ms full-carrier glitch had split that pulse (`H 12000000`, `L 12100000`, `H 12120000`, `L 12200412`), then `H 12120000` would find the gap `12_120_000 - 12_100_000 = 20_000 < 50_000` and bridge it: `open` goes back to 12_000_000 and `GapsBridged` becomes 1. The same `Pulse(12_000_000, 200_412)` would still come out at `H 13000003`.

### 2. A frame start through stage 4

`FrameSync` starts in Searching. The symbols are the end of one minute and the start of the next, with second 0 of the new minute at 100.000 s:

| Symbol (start µs, duration µs, kind) | Rule | Output |
| --- | --- | --- |
| `97_000_000, 200_000, Zero` | Not a marker: `n = 0` | `Searching(0)` |
| `98_000_000, 200_000, Zero` | Not a marker: `n = 0` | `Searching(0)` |
| `99_000_000, 800_000, Marker` | Marker after a non-marker: `n = 1` | `Searching(1)` |
| `100_000_000, 800_000, Marker` | Marker after a counted marker, gap 1_000_000 in range: `n = 2`, so second 0 | `SlotFilled(100_000_000, 0, Marker)` |
| `101_000_000, 200_000, Zero` | Synced, second 1, gap in range, bit at a bit second | `SlotFilled(100_000_000, 1, Zero)` |
| `102_000_000, 500_000, One` | Synced, second 2 | `SlotFilled(100_000_000, 2, One)` |

The marker at 99 s is second 59 (P0) of the previous frame and the one at 100 s is second 0 (Pr). The frame's `StartMicros` is 100_000_000.

Had the symbol at second 9 arrived as `109_000_000, 500_000, One`, check 2 would output `SyncLost("expected marker at second 9, got One")` and `Searching(0)`, discarding seconds 0 to 8.

### 3. A flipped bit through stages 5 and 6

The previous accepted frame encodes 2026-12-10T19:30Z (day of year 344) with `StartMicros = 1_000_000`. The next frame should encode 19:31 and start at 61_000_000. Its 60 symbols, as `WwvbFrameEncoder.Encode(2026-12-10T19:31Z, 61_000_000, Plus, 0, false, Standard)` writes them (`M` is a marker):

| Seconds | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 to 9 | M | 0 | 1 | 1 | 0 | 0 | 0 | 0 | 1 | M |
| 10 to 19 | 0 | 0 | 0 | 1 | 0 | 1 | 0 | 0 | 1 | M |
| 20 to 29 | 0 | 0 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | M |
| 30 to 39 | 0 | 1 | 0 | 0 | 0 | 0 | 1 | 0 | 1 | M |
| 40 to 49 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | M |
| 50 to 59 | 0 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | M |

Read against the layout:

| Field | Seconds and bits | Value |
| --- | --- | --- |
| Minute | 1 to 3 = `0 1 1` (20 + 10), 5 to 8 = `0 0 0 1` (1) | 31 |
| Hour | 12 to 13 = `0 1` (10), 15 to 18 = `1 0 0 1` (8 + 1) | 19 |
| Day of year | 22 to 23 = `1 1` (200 + 100), 25 to 28 = `0 1 0 0` (40), 30 to 33 = `0 1 0 0` (4) | 344 |
| UT1 | 36 to 38 = `1 0 1`, 40 to 43 = `0 0 0 0` | `Plus`, 0 tenths |
| Year | 45 to 48 = `0 0 1 0` (20), 50 to 53 = `0 1 1 0` (4 + 2) | 26 |
| Flags | 55 = 0, 56 = 0, (57, 58) = `0 0` | not a leap year, no leap second, `Standard` |

Noise lengthens the 0.2 s pulse at second 6 into a 1: second 6 reads `One`. Every marker is still in place, so the frame stays synced and completes.

**Stage 5.** Minute units are now `0 1 0 1` (4 + 1), so `DecodedFrame` has `Minute = 35`, `Hour = 19`, `DayOfYear = 344`, `Year = 26`, `Ut1Sign = Plus`, `Ut1Tenths = 0`, both flags false, `Dst = Standard`, and `ErrorSeconds` empty.

**Stage 6.** Every check but the last passes. The markers are in place, the unused seconds are zero, there are no `Error` symbols, every BCD digit is at most 9, and minute 35, hour 19, day 344, the UT1 sign, and 0 tenths are all legal. `Consecutive` runs because `prev` started 60 s earlier, within 90 s. The `StartMicros` difference is exactly 60_000_000, but the frame's time is 19:35, not 19:31. The result is `Accepted = false`, with `Failures = [Consecutive: "expected 2026-12-10T19:31Z, got 2026-12-10T19:35Z"]`.

This is ADR-0006's case: one flipped bit makes a frame that is legal on its own, and only the comparison with the previous frame catches it. Without a checksum, nothing else can.

The last accepted frame stays 19:30. A clean 19:32 frame at 121_000_000 then starts 120 s after it, so `Consecutive` is skipped and the frame is accepted on the other checks alone. If the clock was `Locked`, it went to `Holding` on the rejection and returns to `Locked` with that frame.
