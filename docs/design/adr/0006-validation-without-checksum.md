# ADR-0006: Validation without a checksum

**Date**: 2026-10-31 (recorded from `notes/plan.md`; decision made 2026-10-29 when the repo was scaffolded)
**Status**: accepted
**Deciders**: Mel Dommer

## Context

The WWVB amplitude time code carries no CRC and no parity. In a noisy room a single misread pulse produces a frame that looks legal but encodes the wrong time. The decoder has to decide validity from the structure of the signal alone. This is also a talking point: HART protects every byte with parity and every message with a check byte, and WWVB has neither.

## Decision

The validation stage (stage 6 in ADR-0005) accepts a frame only when all of the following hold. Every check runs on every frame, in this order, and each failing check is reported as one rejection reason. The first reason is the headline the UI shows.

1. **Marker positions**: markers at seconds 0, 9, 19, 29, 39, 49, and 59, and no marker anywhere else.
2. **Unused bits are zero**: every second not assigned to a field or marker decodes as 0.
3. **No error symbols**: no second was classified as an error, meaning a pulse too short or too long to read.
4. **Legal ranges**: minute 0 to 59, hour 0 to 23, day of year 1 to 365 (366 only when the leap-year flag is set), each BCD digit 0 to 9, UT1 sign pattern exactly 101 or 010, UT1 magnitude 0.0 to 0.9 s.
5. **Frame-to-frame consistency**: the frame started a whole number of minutes after the previous accepted frame (± 50 ms for each minute, up to 60 minutes), and its decoded time is exactly that many minutes later. The minutes are counted from the two start times, so the check also runs after a lost or a rejected frame. A frame that fails only this check is remembered as a challenger, and a later frame that is consistent with the challenger is accepted: two frames that agree outvote a previous frame that agrees with neither. Up to 90 s after the previous accepted frame, a start that is not 60 s (± 50 ms) later is a failure. The check is skipped when there is no previous accepted frame, when the start is more than 90 s later and not on a whole minute, when it is more than 60 minutes later, or when a digit or range check failed so the time cannot be built.

Rejected frames are reported with their reasons so the UI can show accepted and rejected frames side by side.

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
- Some errors still pass. Compensating bit flips can yield a legal value, and the consistency check only catches them when the result disagrees with the previous frame. Two frames in a row that carry the same wrong time replace a right one.
- The first frame after a start or a reset is accepted on the single-frame checks alone. So is a frame that starts more than 60 minutes after the previous accepted one, or more than 90 s after it and not on a whole minute.
- A right frame is rejected when the previous accepted frame is the wrong one. The frame after it is then accepted through the challenger.
- The consistency check depends on the edge timestamps to count minutes. It allows them 50 ms a minute, which is 833 parts per million.
- Range checks need calendar knowledge: leap years and day-of-year limits.

### Risks
- Consistency across minute, hour, day, and year rollovers must use real time arithmetic, not field-by-field comparison.
- A leap second (23:59:60 UTC on June 30 or December 31) makes the next frame arrive 61 seconds later. The decoder loses sync and recovers by itself about two minutes later.

*Amended 2026-11-06 (end of Step 6):* the original mitigation allowed the one-second slip when the leap-second-pending flag was set. It is removed because that slip can never reach validation. Frame sync has no leap-second case, so it loses sync on the extra marker, and the next frame starts 121 seconds after the last accepted one, which skips the consistency check. Handling a leap second is also not worth the code: the last one was at the end of 2016, none will be inserted at the end of December 2026, and in 2022 the CGPM resolved to end them by 2035.

*Amended 2026-11-07 (Step 7):* the decision said the stage reports the first failing check. It now reports every failing check, one reason per check, so a noisy frame shows all of its problems at once ([#12](https://github.com/byte2pixel/wwvb-decoder/pull/12)). The amendment also lists the error-symbol check, the 60 s start-time tolerance, and the rules for skipping the consistency check, which `pipeline-contracts.md` already specified.

*Amended 2026-11-19 (after Step 13):* the consistency check was skipped whenever the previous accepted frame started more than 90 s earlier. That is the case after every lost frame and after every rejected frame, so in noise most accepted frames were compared with nothing. A recording made for Step 13 showed the result: after a loss of sync, a 19:33 frame with one flipped bit read 19:32, was accepted, and left the locked clock a minute behind. The check now counts the minutes between the two frames from their start times and compares across the gap. Two things follow from that. The previous accepted frame can itself be wrong, and it would then have every later frame rejected for an hour, so a frame that fails only this check is kept as a challenger. And the leap-second case above is still skipped, now because 121 s is not a whole number of minutes. The edge timestamps are good enough to count minutes with: in the overnight capture of 2026-11-09, 524 frames over 523 minutes, any two frames up to 60 minutes apart start within 7 ms of a whole number of minutes, and the logger's clock drifts 0.4 parts per million against WWVB.
