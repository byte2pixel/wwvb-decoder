# ADR-0004: IEdgeSource with serial, replay, and synthetic implementations

**Date**: 2026-10-31 (recorded from `notes/plan.md`; decision made 2026-10-29 when the repo was scaffolded)
**Status**: accepted
**Deciders**: Mel Dommer

## Context

Live reception in a daytime conference room is uncertain, so the demo cannot depend on live RF. The Picos do not arrive until Nov 3, but pipeline work starts Nov 2. The pipeline needs deterministic unit tests. And the talk is a sequel to a Composition over Inheritance talk, so swapping inputs without touching the decoder is itself a point to make.

## Decision

The pipeline input is one interface, `IEdgeSource`, that produces timestamped edges. Three implementations:

- `SerialEdgeSource`: the live Pico over USB serial.
- `ReplayEdgeSource`: capture files recorded overnight at home, played at 1x or fast-forward.
- `SyntheticEdgeSource`: generates perfect frames with a noise knob.

Decoder stages never know which source is active, and the UI can switch sources at runtime.

## Alternatives Considered

### Alternative 1: Replay at a later stage (record symbols or frames instead of edges)
- **Pros**: Much smaller capture files; simpler format.
- **Cons**: Replay would skip pulse measurement and symbol classification, so the strip chart and histogram would show nothing during the fallback demo, and recorded noise would never reach them.
- **Why not**: The fallback demo has to exercise the same stages as the live one.

### Alternative 2: Virtual COM port fed by a replay tool
- **Pros**: No abstraction in the host; the serial source is the only source.
- **Cons**: Needs a virtual serial driver on Windows, cannot fast-forward without rewriting timestamps, and adds a second process to launch on stage.
- **Why not**: Fragile, and contrary to the one-executable rule in ADR-0003.

### Alternative 3: One source class with a mode switch
- **Pros**: Fewer classes.
- **Cons**: Branching on mode inside the source, and no clean seam for tests.
- **Why not**: Defeats the composition story the talk is making.

## Consequences

### Positive
- Pipeline and UI development start on day one against the synthetic source, before any hardware arrives.
- The replay source is the demo safety net and reproduces real recorded noise.
- Unit tests run against deterministic synthetic edges.
- Switching sources on stage is a live illustration of composition.

### Negative
- The capture file format becomes a second contract alongside the serial line format (ADR-0001) and needs versioning.
- Fast-forward replay breaks the relationship between edge timestamps and wall-clock time, so the clock stage must derive elapsed time from edge timestamps rather than the PC clock.

### Risks
- The synthetic noise model may not resemble real interference. Mitigation: calibrate it against the overnight captures once they exist.
