# ADR-0006: Validation without a checksum

**Date**: 2026-10-31 (recorded from `notes/plan.md`; decision made 2026-10-29 when the repo was scaffolded)
**Status**: accepted
**Deciders**: Mel Dommer

## Context

The WWVB amplitude time code carries no CRC and no parity. In a noisy room a single misread pulse produces a frame that looks legal but encodes the wrong time. The decoder has to decide validity from the structure of the signal alone. This is also a talking point: HART protects every byte with parity and every message with a check byte, and WWVB has neither.

## Decision

The validation stage (stage 6 in ADR-0005) accepts a frame only when all of the following hold, and reports the first failing check as the rejection reason:

1. **Marker positions**: markers at seconds 0, 9, 19, 29, 39, 49, and 59, and no marker anywhere else.
2. **Unused bits are zero**: every second not assigned to a field or marker decodes as 0.
3. **Legal ranges**: minute 0 to 59, hour 0 to 23, day of year 1 to 365 (366 only when the leap-year flag is set), each BCD digit 0 to 9, UT1 sign pattern exactly 101 or 010, UT1 magnitude 0.0 to 0.9 s.
4. **Frame-to-frame consistency**: the decoded time is exactly one minute after the previous accepted frame.

Rejected frames are reported with their reason so the UI can show accepted and rejected frames side by side.

## Alternatives Considered

### Alternative 1: Accept any frame that passes frame sync
- **Pros**: Lock in one minute; least code.
- **Cons**: One flipped bit sets the wrong time, and the demo would show it confidently.
- **Why not**: Noise is part of the demo, and this makes noise invisible.

### Alternative 2: Majority vote over several frames
- **Pros**: Robust against isolated bit errors without any range logic.
- **Cons**: Needs three to five minutes to lock, and the result does not say which bit was wrong or why. Hard to narrate.
- **Why not**: Slow and opaque; the per-check approach explains itself on screen.

### Alternative 3: Reject frames that disagree with the PC clock
- **Pros**: Catches gross errors immediately.
- **Cons**: Uses the answer to check the answer. A radio clock that trusts the PC clock has not received anything.
- **Why not**: Circular. The clock stage displays the offset from the PC clock as information only.

### Alternative 4: Decode the phase-modulated code, which has error correction
- **Pros**: The BPSK time code added in 2012 includes error-correcting bits.
- **Cons**: Needs a phase-tracking receiver or an SDR. The CME6005 outputs only the amplitude envelope, and SDR is out of scope.
- **Why not**: Wrong hardware for this project.

## Consequences

### Positive
- Rejection reasons make the noise demo legible: hold a charger near the antenna and watch the reasons change.
- Sets up the contrast with HART parity and check byte.
- No extra hardware and no dependence on the PC clock.

### Negative
- Lock takes at least two consecutive clean frames, so at least two minutes.
- Some errors still pass. Compensating bit flips can yield a legal value, and the consistency check only catches them when the result disagrees with the previous frame.
- Range checks need calendar knowledge: leap years and day-of-year limits.

### Risks
- Consistency across minute, hour, day, and year rollovers must use real time arithmetic, not field-by-field comparison.
- A leap second (23:59:60 UTC on June 30 or December 31) makes the next frame arrive 61 seconds later. The decoder loses sync and recovers by itself about two minutes later.

*Amended 2026-11-06 (end of Step 6):* the original mitigation allowed the one-second slip when the leap-second-pending flag was set. It is removed because that slip can never reach validation. Frame sync has no leap-second case, so it loses sync on the extra marker, and the next frame starts 121 seconds after the last accepted one, which skips the consistency check. Handling a leap second is also not worth the code: the last one was at the end of 2016, none will be inserted at the end of December 2026, and in 2022 the CGPM resolved to end them by 2035.
