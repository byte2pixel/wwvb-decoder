# ADR-0005: Seven composable pipeline stages, each with typed input and output

**Date**: 2026-10-31 (recorded from `notes/plan.md`; decision made 2026-10-29 when the repo was scaffolded)
**Status**: accepted
**Deciders**: Mel Dommer

## Context

The talk walks through decoding layer by layer and draws a HART parallel at each layer: physical layer, bit timing, mark/space decision, preamble, field parsing, check byte, process value. The visualizer has to show the output of every layer at once. A decoder written as one loop would produce a time and nothing else to look at.

## Decision

The decoder is seven small stages:

| # | Stage | Input | Output |
| --- | --- | --- | --- |
| 1 | Edge capture (firmware) | GPIO | Timestamped edges |
| 2 | Pulse measurement | Edges | Reduced-power durations; glitches under ~50 ms dropped |
| 3 | Symbol classification | Durations | 0, 1, marker, or error (thresholds near 0.35 s and 0.65 s) |
| 4 | Frame sync | Symbols | 60-symbol frames aligned on the double marker at seconds 59 and 0 |
| 5 | Field decode | Frames | Minute, hour, day of year, year, UT1, and flag fields |
| 6 | Validation | Decoded fields | Accepted or rejected frame with a reason |
| 7 | Clock | Accepted frames | UTC and local time, time since sync, offset from the PC clock |

Each host stage is its own class with a typed input and a typed output. The visualizer is one more subscriber to each stage output; stages know nothing about the UI.

## Alternatives Considered

### Alternative 1: One decoder loop
- **Pros**: Simplest and fastest to write.
- **Cons**: No intermediate output to show or test. Noise effects are invisible until the final time is wrong.
- **Why not**: The talk is about the intermediate steps.

### Alternative 2: Two coarse stages (signal to symbols, symbols to time)
- **Pros**: Fewer types and less plumbing.
- **Cons**: Hides the histogram, the frame grid filling in, and the rejection reasons, which are the three visuals the demo is built around.
- **Why not**: Too coarse for the slides.

### Alternative 3: Decode in the React page
- **Pros**: One language for decoding and drawing; no serialized intermediate types.
- **Cons**: Moves the pipeline out of C# and away from .NET test tooling, and couples decoding to the UI.
- **Why not**: Conflicts with ADR-0003 and with keeping the pipeline testable on its own.

## Consequences

### Positive
- Each stage is unit-testable in isolation with hand-built inputs.
- Each stage maps to one slide and one HART parallel.
- Adding a visualization means adding a subscriber, not touching a stage.
- Any stage can be fed from replay or synthetic data (ADR-0004).

### Negative
- More types and more wiring than a single loop.
- Threading and backpressure between stages need a deliberate design.
- Every intermediate type needs a JSON shape for the WebView2 bridge.

### Risks
- A boundary is drawn in the wrong place, such as glitch filtering landing in symbol classification instead of pulse measurement. Mitigation: stages are small, so moving a responsibility is cheap.
- C# output types and TypeScript message types drift. Mitigation: one message type per stage, defined once and checked by tests on both sides.
