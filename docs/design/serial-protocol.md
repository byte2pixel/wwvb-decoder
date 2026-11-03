# Serial line protocol

The Pico 2 edge logger reports every edge on the receiver's data pin as one text line over USB serial. It makes no decisions about what the edges mean (ADR-0001, ADR-0002). This document is the contract between the two sides:

| Side | Implemented in | Role |
| --- | --- | --- |
| Firmware | `firmware/` (construction plan Step 9) | Emits the lines |
| Host codec | `WwvbDecoder.Core`, `EdgeLineCodec` (Step 4) | Parses one line into an edge event |
| Host adapter | `WwvbDecoder.Adapters`, `SerialEdgeSource` (Step 10) | Owns the port, applies the reset rules |

The same lines, with a header, form a capture file. See [`capture-format.md`](capture-format.md).

## Transport

| Item | Value | Note |
| --- | --- | --- |
| USB class | CDC ACM (virtual COM port) | Windows binds `usbser.sys` without an INF when the device descriptor uses class 0xEF, subclass 0x02, protocol 0x01 with IADs |
| Line settings | 8 data bits, no parity, 1 stop bit, no flow control | CDC ignores the baud rate; the host opens the port at 115200 by convention |
| DTR | Host asserts DTR after opening | The firmware waits for DTR before writing, so a host that does not assert it sees nothing |
| USB VID / PID | 0xC0DE / 0xCAFE | The Embassy `usb_serial` example values. Not a registered VID; acceptable for a private device that never ships |
| Manufacturer string | `wwvb-decoder` | |
| Product string | `wwvb-edge-logger` | Visible in Device Manager |
| Direction | Device to host only | The host sends nothing. Anything received by the firmware is ignored |

The host does not identify the device by USB IDs. The user picks the port by name, and the host confirms it is talking to the logger when it sees the banner line (see Informational lines).

## Encoding

- ASCII only. No bytes above 0x7F.
- Every line ends with a single line feed (`\n`, 0x0A). No carriage return.
- Maximum line length is 64 bytes including the line feed. A host may drop anything longer as malformed.
- Fields within a line are separated by exactly one space (0x20).
- There is no framing beyond the line feed and no checksum. Serial over USB is reliable enough for this; the host's error handling covers the rest.

## Edge lines

One line per edge on the receiver's data pin:

```text
<level> <micros>\n
```

| Field | Values | Meaning |
| --- | --- | --- |
| `level` | `H` or `L` | The pin level **after** the edge. `H` is a rising edge, `L` is a falling edge |
| `micros` | Unsigned decimal integer, 1 to 20 digits, no sign, no leading `+`, leading zeros allowed | Microseconds since the firmware booted, from a 64-bit counter. It does not wrap in practice (584,000 years) |

Rules:

- The firmware reports the raw pin level. Which level means "reduced carrier" (the start of each WWVB second) is decided on the host by the pulse-measurement stage's `ReducedCarrierLevel` setting. See the polarity note under Example.
- Consecutive timestamps are normally increasing. Two consecutive edges with the **same** timestamp are legal (contact bounce or a glitch inside one microsecond) and must not be treated as an error.
- A timestamp **smaller** than the previous one means the Pico restarted. See Reset detection.
- Two consecutive lines with the same `level` can happen when the firmware dropped an edge (see Rate and back-pressure). The host counts this as an unpaired edge and resynchronizes; it is not a protocol error.

Examples of valid edge lines:

```text
L 1203455
H 1403461
L 0
```

## Informational lines

Any line starting with `#` is informational. The firmware emits three kinds:

| Line | When | Meaning |
| --- | --- | --- |
| `# boot` | First line of every session: each time the host asserts DTR | A new session has started; discard any state from before it. Timestamps restart from zero only if the Pico itself restarted; on a reconnect they continue from the Pico's boot |
| `# wwvb-edge-logger <semver> pin=<gpio>` | Immediately after `# boot` | Banner. `<semver>` is the firmware's Cargo version, `<gpio>` the input pin number, for example `# wwvb-edge-logger 0.1.0 pin=2` |
| `# dropped <n>` | At most every 10 s, only when `n` is greater than 0 | `n` edges were lost since the last report because the firmware's queue was full |

Host rules:

- `# boot` is a **reset event**. Other `#` lines are **info events**. Edge lines are **sample events**. The codec makes exactly this three-way split.
- A host must ignore any `#` line it does not recognize, after forwarding it as an info event. This is how the capture format adds its own `#` header lines without a protocol change.
- The banner is the host's confirmation that the port belongs to the logger. The host should show it to the user.

## Reset detection

Two conditions mean the edge stream may not continue from the previous edge (a new session, or the Pico restarted and the timestamp baseline changed):

1. A `# boot` line arrived.
2. An edge line's `micros` is strictly smaller than the previous edge line's `micros`.

On either, the serial adapter emits a reset event (reason `boot` or `timestamp went backwards`), and the pipeline calls `Reset()` on every stage, discarding partial pulses and any frame in progress. The firmware discards edges queued while no host held DTR, so a session never starts with stale edges. The second condition is a safety net for a stream whose `# boot` was lost.

Replay follows the same rules: a `# boot` line in a capture file resets the pacing baseline.

## Error handling

- A line that does not match an edge line or a `#` line is **malformed**. Examples: `X 12`, `H -1`, `H 1.5`, `H`, an empty line, a line longer than 64 bytes, a line containing a carriage return.
- The host counts malformed lines and skips them. It never disconnects, throws, or resets the pipeline because of a malformed line.
- The host should surface the malformed-line count to the user. A steadily rising count means the wrong port or wrong baud convention, not a noisy signal.
- Firmware never emits a partial line. If it cannot fit a complete line in its buffer, it drops the edge and counts it instead.

## Rate and back-pressure

- Nominal rate: the receiver produces one reduced-carrier pulse per second, so two edges per second, about 30 bytes per second.
- Noise bursts: a laptop charger or LED driver near the antenna can produce hundreds of edges per second for a while.
- The firmware **never blocks** waiting for USB. Edges are timestamped in the interrupt path and queued; a writer task drains the queue. If the queue (256 entries) is full, the edge is dropped and the dropped counter incremented. The count is reported with `# dropped <n>` and reset.
- The host should treat `# dropped` as a signal-quality indicator (the pulse-measurement stage will also see unpaired edges) and display it, not as an error.
- There is no flow control and no acknowledgement. CDC over full-speed USB can carry far more than the worst-case burst; the queue exists for scheduling latency, not throughput.

## Example

A fresh connection, then a "zero" second (0.2 s of reduced carrier) and a marker second (0.8 s). Timestamps are microseconds since boot.

```text
# boot
# wwvb-edge-logger 0.1.0 pin=2
H 12000000
L 12200412
H 13000003
L 13800377
H 14000011
L 14500398
H 15000006
L 15200401
H 16000002
L 16800390
```

Reading it: each `H` starts a reduced-carrier pulse at the top of a second, each `L` ends it. The first pulse lasts 200 ms (a 0), the second 800 ms (a marker), the third 500 ms (a 1), the fourth 200 ms (a 0), the fifth 800 ms (a marker).

**Polarity.** Measured 2026-11-02 with the USB logic analyzer on the T pin of receiver #1 (`captures/pulseview/`): the output idles **low** during full carrier and goes **high** for the reduced-carrier pulse, so `H` starts a pulse and `L` ends it. The host default for `ReducedCarrierLevel` is therefore `High`. The setting stays switchable from the UI in case another module is inverted; the protocol does not change either way.
