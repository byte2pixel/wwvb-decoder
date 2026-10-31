# ADR-0001: Firmware only timestamps edges

**Date**: 2026-10-31 (recorded from `notes/plan.md`; decision made 2026-10-29 when the repo was scaffolded)
**Status**: accepted
**Deciders**: Mel Dommer

## Context

The CME6005 receiver module outputs one digital pulse per second whose width carries the WWVB symbol. The pulse has to be timed somewhere, and the obvious place is the microcontroller the module is wired to. But the point of the project is a talk that shows every decoding step live on a laptop screen, and anything decided inside the Pico is invisible to the audience. The demo also needs a replay mode fed from overnight recordings, which only works if the recordings contain the rawest data available.

## Decision

The Pico 2 firmware timestamps every rising and falling edge on the receiver pin with a microsecond timer and writes one line per edge over USB CDC serial. It makes no decisions: no pulse-width measurement, no glitch filtering, no symbol or frame logic. Each line carries the raw pin level after the edge; which level means "reduced carrier" is decided on the host, because the module listing does not say whether the output idles high or low.

*Amended 2026-10-31 (construction plan review):* the original text made edge polarity a firmware setting. That would have been the firmware's one decision, so it moved to the host, where the pulse-measurement stage has a `ReducedCarrierLevel` option switchable from the UI.

## Alternatives Considered

### Alternative 1: Decode fully on the Pico and send the time
- **Pros**: Self-contained device; a few bytes per minute over serial; the usual way a radio clock is built.
- **Cons**: Every interesting step happens where nobody can see it. Recordings would contain decoded times, so replay could not demonstrate noise. Firmware changes mean re-flashing over BOOTSEL with no debug probe.
- **Why not**: Removes the content of the talk from the screen.

### Alternative 2: Measure pulse widths on the Pico and send durations
- **Pros**: Half the serial lines; the host receives something closer to a symbol.
- **Cons**: Pairing edges and dropping glitches become firmware decisions. Polarity gets baked in. Raw noise, the thing the demo wants to show, is filtered before it reaches the PC.
- **Why not**: Moves stage 2 of the pipeline off the laptop for no visible gain.

### Alternative 3: Sample the pin from the PC with a USB logic analyzer
- **Pros**: No firmware at all.
- **Cons**: The logic analyzer on the parts list is a debugging tool with a capture-and-dump workflow, not a streaming source. Timing would depend on USB polling from the laptop.
- **Why not**: Not a live source; it stays in the kit for PulseView debugging only.

## Consequences

### Positive
- Every decision is made on the laptop and can be visualized.
- Capture files hold everything the receiver produced, so replay reproduces the live run exactly.
- The firmware is small enough to finish early in the schedule and rarely needs re-flashing.
- Serial, replay, and synthetic sources all produce the same edge stream (see ADR-0004).

### Negative
- The host must pair edges, handle missing or duplicated edges, and filter glitches itself.
- The one-line-per-edge serial format becomes a contract between firmware and host and needs documenting.
- Timestamps come from the Pico clock, not the PC clock, so the host must not mix the two.

### Risks
- Timestamps are a 64-bit microsecond count from boot, so wraparound is not a practical concern, but the count restarts at zero when the Pico resets. The host treats a timestamp smaller than the previous one as a source reset and computes differences, not absolute values.
- A dropped serial line leaves an unpaired edge. Pulse measurement (stage 2) tolerates this by resynchronizing on the next edge of the expected polarity.
