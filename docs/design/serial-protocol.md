# Serial line protocol

The Pico 2 edge logger reports every edge on the receiver's data pin as one text line over USB serial. It makes no decisions about what the edges mean (ADR-0001, ADR-0002). This document is the contract between the two sides:

| Side | Implemented in | Role |
| --- | --- | --- |
| Firmware | `firmware/` (construction plan Step 9) | Emits the lines |
| Host codec | `WwvbDecoder.Core`, `EdgeLineCodec` (Step 4) | Parses one line into an edge event |
| Host adapter | `WwvbDecoder.Adapters`, `SerialEdgeSource` (Step 10) | Owns the port, applies the reset rules |

The same lines, with a header, form a capture file. See [`capture-format.md`](capture-format.md).

A second Pico, the desk-top transmitter, takes commands from the host. Its contract is the last section, [Transmitter commands](#transmitter-commands). Every section before that one is about the edge logger only.

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
| `# session` | First line of every session: each time the host asserts DTR | A new session has started; discard any state from before it. Timestamps restart from zero only if the Pico itself restarted; on a reconnect they continue from the Pico's boot |
| `# wwvb-edge-logger <semver> pin=<gpio>` | Immediately after `# session` | Banner. `<semver>` is the firmware's Cargo version, `<gpio>` the input pin number, for example `# wwvb-edge-logger 0.1.0 pin=2` |
| `# dropped <n>` | At most every 10 s, only when `n` is greater than 0 | At least `n` edges were lost since the last report, because the firmware's queue was full or because edges came too close together to stamp one by one (see Rate and back-pressure) |

Host rules:

- `# session` is a **reset event**. Other `#` lines are **info events**. Edge lines are **sample events**. The codec makes exactly this three-way split.
- A host must ignore any `#` line it does not recognize, after forwarding it as an info event. This is how the capture format adds its own `#` header lines without a protocol change.
- The banner is the host's confirmation that the port belongs to the logger. The host should show it to the user.

## Reset detection

Two conditions mean the edge stream may not continue from the previous edge (a new session, or the Pico restarted and the timestamp baseline changed):

1. A `# session` line arrived.
2. An edge line's `micros` is strictly smaller than the previous edge line's `micros`.

On either, the serial adapter emits a reset event (reason `session` or `timestamp went backwards`), and the pipeline calls `Reset()` on every stage, discarding partial pulses and any frame in progress. The firmware discards edges queued while no host held DTR, so a session never starts with stale edges. At most one USB packet of lines from the previous session (a few edges) can arrive ahead of `# session`; the reset discards them. The second condition is a safety net for a stream whose `# session` was lost.

Replay follows the same rules: a `# session` line in a capture file resets the pacing baseline.

## Error handling

- A line that does not match an edge line or a `#` line is **malformed**. Examples: `X 12`, `H -1`, `H 1.5`, `H`, an empty line, a line longer than 64 bytes, a line containing a carriage return.
- The host counts malformed lines and skips them. It never disconnects, throws, or resets the pipeline because of a malformed line.
- The host should surface the malformed-line count to the user. A steadily rising count means the wrong port or wrong baud convention, not a noisy signal.
- Firmware never emits a partial line. Every line fits one USB packet, so lines are never split across packets.

## Rate and back-pressure

- Nominal rate: the receiver produces one reduced-carrier pulse per second, so two edges per second, about 30 bytes per second.
- Noise bursts: a laptop charger or LED driver near the antenna can produce hundreds of edges per second for a while.
- The firmware **never blocks** waiting for USB. Edges are timestamped in the interrupt path and queued; a writer task drains the queue. If the queue (256 entries) is full, the edge is dropped and the dropped counter incremented. Edges closer together than the firmware's wake-up time (a few microseconds) share one line; the firmware detects that from the pin's edge latches and counts at least one as dropped. The count is reported with `# dropped <n>` and reset.
- The host should treat `# dropped` as a signal-quality indicator (the pulse-measurement stage will also see unpaired edges) and display it, not as an error.
- There is no flow control and no acknowledgement. CDC over full-speed USB can carry far more than the worst-case burst; the queue exists for scheduling latency, not throughput.

## Example

A fresh connection, then a "zero" second (0.2 s of reduced carrier) and a marker second (0.8 s). Timestamps are microseconds since boot.

```text
# session
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

## Transmitter commands

The desk-top transmitter is a second Pico 2 (construction plan Step 14). It sends a 60 kHz carrier, keyed with WWVB frames, into a small coil beside the receiver's antenna, for a room where the real station cannot be received. The PC gives it the time and a few commands over USB serial. This section is the contract between the two sides:

| Side | Implemented in | Role |
| --- | --- | --- |
| Firmware | `firmware/transmitter/`, with its logic in `firmware/transmitter-core/` (Step 14) | Keeps the time, keys the carrier, obeys the commands |
| Host tool | `WwvbDecoder.Tools`, `tx-sync` (Step 14) | Sends the PC's time and prints the reply |

The decoder never talks to the transmitter. It hears it through the receiver and the edge logger, as it hears the real station.

### Transport and encoding

The transport is the edge logger's (see Transport), with three differences:

| Item | Value | Note |
| --- | --- | --- |
| Product string | `wwvb-transmitter` | |
| USB VID / PID | 0xC0DE / 0xCAFF | The logger's PID plus one, so the two Picos differ when both are plugged in |
| Direction | Both ways | The host sends command lines. The transmitter answers each with one line |

Lines from the transmitter follow Encoding: ASCII, one line feed at the end, at most 64 bytes, each in one USB packet.

Command lines, from the host, have their own rules:

- ASCII. A line is a command word in upper case. `TIME` and `FRAME` are followed by exactly one space and one argument. There is nothing else on the line: no leading space, no trailing space.
- A line ends at a line feed or at a carriage return. An empty line is ignored and gets no reply. So a line feed, a carriage return, or both together end a command, and a serial terminal, which sends a carriage return for the Enter key, works as it is.
- A line is at most 66 bytes, not counting its end. That is the length of a `FRAME` command. A longer line is discarded up to its end and answered with `ERR line too long`.
- There is no echo, no flow control, and no timeout: an unfinished line waits for its end.

### Session

- A session starts when the host asserts DTR, as with the logger. The transmitter writes `# session` and then its banner, `# wwvb-transmitter <semver> pin=<gpio>`, where `<gpio>` is the pin that drives the coil. For example `# wwvb-transmitter 0.1.0 pin=16`.
- The banner tells the host which Pico it has opened. `tx-sync` waits for it before it sends anything, and stops with a message when the banner is the edge logger's.
- A host waits for the banner before it sends its first command. The transmitter discards the bytes it received before it wrote the banner, so a session starts with an empty command line and nothing of an earlier session is left in it.
- A host ignores every line before `# session`. At most one can arrive there: a reply of an earlier session that its host never read, which the USB hardware still held.
- A host must assert DTR. What the transmitter does with bytes sent without DTR is not defined.
- After the banner the transmitter writes only replies: one line for each command, and nothing by itself.
- The session and the signal are independent. The transmitter keeps its time and keeps transmitting after the host closes the port. Only a power loss or a reset clears its state.

### State

The transmitter holds three things, and each command reads or changes them:

| State | At power-up | Changed by |
| --- | --- | --- |
| Clock | Not set | `TIME` sets it. It then runs on the Pico's crystal |
| Carrier switch | On | `ON` and `OFF` |
| Custom frame | None | `FRAME` sets one. The minute it is sent in uses it up |

What the transmitter sends in a minute follows from the three at the start of that minute:

| Carrier switch | Clock | Custom frame | The minute's signal |
| --- | --- | --- | --- |
| Off | Any | Any | None. A custom frame is still used up |
| On | Not set | None | None |
| On | Set | None | The frame that says that minute of the clock |
| On | Set | Waiting | The custom frame |

So after power-up the transmitter is silent until its first `TIME`.

### Commands

| Command | Effect |
| --- | --- |
| `TIME <unix_seconds>` | Sets the clock |
| `STATUS` | Changes nothing |
| `ON` | Switches the carrier on |
| `OFF` | Switches the carrier off |
| `FRAME <symbols>` | Sets the custom frame for the next minute |

`TIME <unix_seconds>`:

- `<unix_seconds>` is decimal digits only: the seconds since 1970-01-01T00:00:00Z, from 946684800 (2000-01-01T00:00:00Z) to 4102444799 (2099-12-31T23:59:59Z). Those are the years that a frame's two digits of year can say.
- The moment the transmitter reads the end of the line is the start of that second. So the host sends the line at the top of a second by its own clock. The error is the PC clock's error plus a few milliseconds of USB.
- It takes effect at once. The second in progress is cut short and the new second begins, with its reduced carrier. The frame in progress is abandoned, custom or not, and the transmitter goes on from that second of the new minute's frame. The decoder sees one second of the wrong length and then part of a frame, which it handles as it handles noise. A custom frame that is waiting keeps waiting.
- Unix time has no leap seconds, and the transmitter never sends one.

`ON` and `OFF`:

- `OFF` stops the carrier at once, and `ON` lets it through again at once, in the middle of a second if that is where it is.
- They switch the output only. The clock runs on and the minutes pass as they would, which is why a custom frame whose minute passes while the carrier is off is used up unsent.

`FRAME <symbols>`:

- `<symbols>` is exactly 60 characters, each `0`, `1`, or `M` for a marker, for seconds 0 to 59 in that order.
- Nothing else about the frame is checked. A marker may be missing or out of place, and a field may hold any bits: the command exists to send a frame that is wrong on purpose.
- It is refused with `ERR clock not set` before the first `TIME`.
- The custom frame takes the place of the next minute's frame, from that minute's second 0, once. The minute after it has its normal frame again. Seconds and minutes fall where the clock puts them, so the stream stays in step.
- A `FRAME` that arrives while another is waiting replaces it. A `FRAME` that arrives while a custom frame is being sent waits for the minute after.

### Replies

The transmitter answers every command line that is not empty with exactly one line, in the order of the commands, after the command has taken effect.

A command that succeeded is answered with the status line, which holds the three things of the State table as they are after the command:

```text
OK time=<unix_seconds> carrier=<on|off> custom=<none|waiting>
```

| Field | Values | Meaning |
| --- | --- | --- |
| `time` | Decimal digits, or `unset` | The clock's current second, as Unix seconds. `unset` before the first `TIME` |
| `carrier` | `on` or `off` | The carrier switch. `on` does not mean there is a signal: with the clock unset there is none |
| `custom` | `none` or `waiting` | Whether a custom frame is waiting for the next minute. It goes back to `none` when that minute starts and the frame is being sent |

A command that failed changes nothing and is answered with `ERR` and a reason:

| Reply | When |
| --- | --- |
| `ERR unknown command` | The command word is not one of the five. Lower case counts as unknown |
| `ERR unexpected argument` | Anything follows `STATUS`, `ON`, or `OFF` |
| `ERR time is not a number` | The argument of `TIME` is missing or holds anything but decimal digits |
| `ERR time is outside 2000 to 2099` | The argument of `TIME` is a number outside the range |
| `ERR frame needs 60 symbols of 0, 1 or M` | The argument of `FRAME` is missing, has another length, or holds another character |
| `ERR clock not set` | `FRAME` before the first `TIME` |
| `ERR line too long` | The line passed 66 bytes |

A program reads the first word, `OK` or `ERR`. The reason is for a person, and a program shows it as it is.

### Signal

- The carrier is a 60 kHz square wave from one GPIO: the 150 MHz system clock divided by 2,500, at 50 percent duty. Reduced carrier is no carrier at all. The station lowers its carrier by 17 dB; the transmitter switches it off.
- Each second starts with reduced carrier: 0.2 s for a 0, 0.5 s for a 1, 0.8 s for a marker. The carrier is full for the rest of the second.
- Each minute is one frame, with second 0 at the clock's minute, and the frame says the minute it is sent in, as the station's does. The layout is the host's `WwvbFrameLayout`, ported to `firmware/wwvb-frame/` and held to it by a test.
- The time does not give the frame's other fields, so they are fixed: a UT1 correction of plus 0.0, no leap second pending, and standard time. In summer the station says daylight time and the transmitter does not. A custom frame can say anything.
- A clock that runs past the end of 2099 has no frame that can say its minute. The transmitter then sends no carrier, apart from a custom frame, until a `TIME` puts the clock back in range.

### Example

A session from power-up. `>` marks a line the host sends and `<` a line from the transmitter. The marks are not on the wire.

```text
< # session
< # wwvb-transmitter 0.1.0 pin=16
> STATUS
< OK time=unset carrier=on custom=none
> TIME 1796931000
< OK time=1796931000 carrier=on custom=none
> STATUS
< OK time=1796931017 carrier=on custom=none
> FRAME M01100000M000101001M001100100M010000101M000000010M011000000M
< OK time=1796931042 carrier=on custom=waiting
> STATUS
< OK time=1796931075 carrier=on custom=none
> time 1796931000
< ERR unknown command
> OFF
< OK time=1796931090 carrier=off custom=none
```

Reading it: the transmitter starts silent, with no time. `TIME` arrives at 2026-12-10T19:30:00Z and the 19:30 frame starts with it. Seventeen seconds later `STATUS` shows the clock running. The `FRAME` at 19:30:42 holds the frame for 19:30 once more, and it waits. At 19:31:00 it is sent in place of the 19:31 frame, so the transmitter sends a frame with the wrong minute, on purpose; by 19:31:15 it is no longer waiting. The command in lower case is refused and changes nothing. `OFF` stops the carrier at 19:31:30, in the middle of that frame, and the clock runs on.
