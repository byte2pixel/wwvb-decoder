# ADR-0002: Rust with Embassy for firmware, C with the Pico SDK as fallback

**Date**: 2026-10-31 (recorded from `notes/plan.md`; decision made 2026-10-29 when the repo was scaffolded)
**Status**: accepted
**Deciders**: Mel Dommer

## Context

The firmware has one job: wait for an edge on a GPIO, read a microsecond timer, and write a line over USB serial (ADR-0001). The target is a Raspberry Pi Pico 2 (RP2350). There is no debug probe, so flashing is over USB with picotool and debugging is by serial output. The firmware window in the schedule is Nov 3 to Nov 13, and the Rust toolchain has already been verified: the blinky firmware builds to a UF2.

## Decision

We write the firmware in Rust using Embassy, with `embassy-rp` 0.10 (`rp235xa` feature) and `embassy-usb` for CDC serial. One async task awaits any edge on the input pin, stamps it, and writes the line. If Embassy blocks progress on the RP2350, the fallback is C with the official Pico SDK and TinyUSB.

## Alternatives Considered

### Alternative 1: C with the Pico SDK
- **Pros**: Official toolchain, the most examples, mature USB stack, well documented for the RP2350.
- **Cons**: Edge capture means a hand-written GPIO interrupt handler and a ring buffer shared with the main loop. More ceremony for a program this small.
- **Why not**: Rejected as the first choice, kept as the fallback. The Rust toolchain already works, and the async model fits the task better.

### Alternative 2: MicroPython or CircuitPython
- **Pros**: Fastest iteration, no compile step, REPL over USB.
- **Cons**: Interpreter and garbage-collector pauses add jitter at the microsecond scale. USB CDC is shared with the REPL, so the edge stream competes with the console.
- **Why not**: Timing jitter undermines the one thing the firmware has to do well.

### Alternative 3: Arduino core for the RP2350
- **Pros**: Easy setup, large community.
- **Cons**: Same interrupt-and-buffer work as C with less control over USB and the timer.
- **Why not**: Offers nothing over the Pico SDK for this task.

## Consequences

### Positive
- Waiting for an edge is a single `await`; the borrow checker handles the shared-buffer problem.
- A single small binary that is unlikely to need changes after the first week.
- The same language can host the desk-top transmitter encoder later.

### Negative
- Embassy support for the RP2350 is younger than the Pico SDK, with fewer examples to copy from.
- No SWD debugging; mistakes are found through serial output and re-flashing over BOOTSEL.

### Risks
- An `embassy-usb` or RP2350 quirk eats the schedule. Mitigation: the firmware is time-boxed to its Nov 3 to Nov 13 window, and the C fallback is a known quantity.
