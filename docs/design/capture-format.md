# Capture file format

A capture is a recording of the edge logger's serial output (see [`serial-protocol.md`](serial-protocol.md)) with a small header. Replay feeds a capture through the same pipeline as a live receiver (ADR-0004), so the demo never depends on live reception.

| Role | Implemented in |
| --- | --- |
| Writer | `WwvbDecoder.Adapters`, `CaptureWriter`, via the Tools `record` command (construction plan Step 10) |
| Writer (interim) | Any serial terminal with session logging, plus a hand-added header (Step 9, Task 7) |
| Reader | `WwvbDecoder.Core`, `ReplayEdgeSource` (Step 11), with `CaptureFile.Open` in Adapters loading the lines |

## File name

```text
captures/<YYYY-MM-DD>T<HHMM>Z-<label>.edges.txt
```

| Part | Rule | Example |
| --- | --- | --- |
| Date and time | UTC, when recording started, minute precision, `Z` suffix | `2026-11-02T0310Z` |
| Label | Lowercase letters, digits, and hyphens; what and where | `home-west-window`, `desk-rx2`, `office-room-204` |
| Extension | Always `.edges.txt` | Plain text, UTF-8 (ASCII in practice), LF line endings |

Example: `captures/2026-11-02T0310Z-home-west-window.edges.txt`. Files live under `captures/` in the repository; a night of recording is a few megabytes of text.

## Header

The file starts with header lines, each beginning with `# ` (hash, space), in this order:

| Line | Required | Meaning |
| --- | --- | --- |
| `# capture v1` | Yes, first line | Format version. A reader rejects a file whose first line is not this |
| `# recorded <ISO-8601 UTC>` | Yes | Wall-clock time the recording started, for example `# recorded 2026-11-02T03:10:27Z` |
| `# source <text>` | Yes | Free text: port, receiver, location, and anything else useful, for example `# source serial COM5, WVB-0860N-03A receiver #1, home west window on 5 m extension` or `# source terminal log (PuTTY), header added by hand` |
| `# reduced-carrier-level <H\|L>` | No | Which pin level means reduced carrier for this recording, if known. Lets replay pre-set the host's `ReducedCarrierLevel` |

A reader treats any other `#` line in the header position as an info line, so the header can grow without a version bump; `v1` changes only if the meaning of an existing line changes.

## Body

After the header, the body is the serial output **verbatim**, one line per line, including the firmware's own `#` lines (`# boot`, the banner, `# dropped <n>`). The recorder does not filter, reformat, or renumber anything. Malformed lines, if the recorder saw any, are kept as-is so replay exercises the same error handling as live reception.

Edge lines therefore have exactly the `serial-protocol.md` form:

```text
L 12000000
H 12200412
```

## Wall-clock lines

The recorder inserts one line per minute pairing the PC clock with the most recent Pico timestamp:

```text
# wall <ISO-8601 UTC> <micros>
```

| Field | Meaning |
| --- | --- |
| `<ISO-8601 UTC>` | The PC's UTC time when the line was written, with at least second precision, for example `2026-11-02T03:11:27Z` |
| `<micros>` | The `micros` value of the last edge line written before this one |

Purpose: when a capture is replayed, the decoded WWVB time can be compared with what the PC thought the time was at recording, so the "offset from PC clock" display stays meaningful in replay. The first `# wall` line is written at the first minute boundary after the first edge; a file shorter than a minute may have none.

Terminal-logged captures (Step 9 interim recordings) have no `# wall` lines. They replay normally; the offset display shows "unknown".

## Replay semantics

- **Timestamps are relative.** Only differences between consecutive `micros` values matter. The absolute values are microseconds since the Pico booted, not time of day.
- **Pacing.** At speed 1, replay waits the difference between consecutive edge timestamps before emitting the next edge. At speed N it waits that difference divided by N. At speed 0 it emits everything as fast as possible (tests, and the Tools `decode` command).
- **Resets.** A `# boot` line in the body resets the pacing baseline and emits a reset event, exactly as live. A timestamp smaller than its predecessor does the same.
- **Header and wall lines.** Header lines populate the capture's metadata. `# wall` lines are forwarded as info events so the UI can show the recorded PC offset. Other `#` lines are forwarded as info events and otherwise ignored.
- **Memory.** The reader loads the whole file into memory before starting. A night is a few megabytes; this keeps the replay source free of file I/O and lets it loop.
- **Looping.** When looping is enabled, reaching the end of the file emits a reset event (reason `loop`) and starts again from the first body line.

## Example

A complete, valid capture with a header, the firmware's boot lines, ten edge lines, and one wall-clock line:

```text
# capture v1
# recorded 2026-11-02T03:10:27Z
# source serial COM5, WVB-0860N-03A receiver #1, home west window on 5 m extension
# reduced-carrier-level L
# boot
# wwvb-edge-logger 0.1.0 pin=2
L 12000000
H 12200412
L 13000003
H 13800377
L 14000011
H 14500398
# wall 2026-11-02T03:11:00Z 14500398
L 15000006
H 15200401
L 16000002
H 16800390
```

To turn a terminal log into a capture by hand: save the terminal session to a file, add the first three header lines above it (and the fourth if the polarity is known), rename it per the File name rules, and remove any carriage returns the terminal added (`(Get-Content raw.log -Raw) -replace "`r", "" | Set-Content -NoNewline out.edges.txt` in PowerShell).
