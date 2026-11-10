# Captures

Raw edge logs from the receiver. Replay uses them as input, and they double as test data. The file format is specified in [`docs/design/capture-format.md`](../docs/design/capture-format.md). This page covers how to record a capture, how to check one, and what each file here contains.

## Files

| File | What it is |
| --- | --- |
| `2026-11-08T2300Z-desk.edges.txt` | Ten-minute smoke test of the Tools `record` command, receiver on the desk. 1,214 edges, no gaps, no malformed lines |
| `2026-11-09T0434Z-overnight.edges.txt` | First overnight capture, 8 h 45 min (04:34Z to 13:19Z on 2026-11-09). 62,991 edges and one glitch; see [The first overnight capture](#the-first-overnight-capture) |
| `pulseview/` | Logic-analyser recordings (PulseView `.sr`) from bring-up, used to establish the receiver's output polarity. Not readable by replay |

## Recording a capture

1. Flash the edge logger and wire the receiver as described in [`firmware/README.md`](../firmware/README.md).
2. Find the port:

   ```powershell
   dotnet run --project host/src/WwvbDecoder.Tools -- ports
   ```

3. Run `record` from the repository root, so the file lands in `captures/`:

   ```powershell
   dotnet run --project host/src/WwvbDecoder.Tools -- record --port COM3 --label overnight
   ```

   `--label` names what and where (lowercase letters, digits, hyphens; default `capture`). `--out <file>` writes to a path of your choice instead.
4. Stop with **Ctrl+C**. The file is closed cleanly and the last line printed is `Wrote <file>`.

The command refuses to overwrite an existing file. If the port never opens, it exits with code 1 and deletes the header-only file it started.

### What the terminal shows

```text
Recording COM3 to captures\2026-11-09T0434Z-overnight.edges.txt
Press Ctrl+C to stop recording.
Port opened.
# wwvb-edge-logger 0.1.0 pin=2
04:35:00  edges 64  resets 0  malformed 0  last 472300594364
```

A status line is printed once a minute on the PC clock (UTC). The file is flushed at the same time, so a crash costs at most a minute of data.

| Field | Meaning | Healthy value |
| --- | --- | --- |
| `edges` | Edge lines recorded so far | Rises by about 120 a minute (two edges a second) |
| `resets` | Resets after the first: a new `# session` from the Pico, or a timestamp that went backwards | 0. Anything else means the Pico reset or the USB link dropped |
| `malformed` | Lines that are neither an edge nor a `#` line. They are still written to the file | 0 |
| `last` | Pico timestamp (µs since boot) of the latest edge | Rises steadily |

A line such as `04:51:12  reset: session` appears when a reset happens.

### Where to put the antenna

- Put the ferrite rod broadside to Fort Collins, Colorado (rod axis at right angles to the direction of the transmitter), near a window, and well away from the PC.
- Keep it away from phone chargers, LED drivers, monitors, and switching power supplies. Noise shows up as bursts of many edges a second, as `edges` climbing much faster than 120 a minute, or as `# dropped` lines.
- Reception is far better at night. A daytime capture can have long stretches of noise; that is still useful replay data, but label it so it can be told apart (`desk-daytime`, for example).

## What is in a file

The format spec has the details. In short:

- **Header**: `# capture v1`, `# recorded <start UTC>`, and `# source serial <port>, label <label>`.
- **Body**: the Pico's serial output, verbatim. That includes `# session` and the firmware banner at the start, edge lines `H <micros>` (reduced-carrier pulse starts) and `L <micros>` (pulse ends), and any `# dropped <n>` lines from the firmware.
- **`# wall <UTC> <micros>`**: written by the recorder at each minute boundary. It pairs the PC's UTC time with the timestamp of the last edge written before it. Replay uses these lines to show the offset between the decoded WWVB time and the PC clock at the time of recording.

File names follow `<YYYY-MM-DD>T<HHMM>Z-<label>.edges.txt`, where the time is UTC when recording started. Files are UTF-8 without BOM, with LF line endings. They take about 110 KB an hour, so a whole night is about 1 MB and can be committed as is.

`CaptureFile.Open` in `WwvbDecoder.Adapters` reads these files for replay. It loads the whole file and splits it on `\n`.

## Checking a capture

Run these from Git Bash, or any shell with `grep` and `awk`, before committing a capture:

```bash
f=captures/2026-11-09T0434Z-overnight.edges.txt
head -c3 "$f" | od -c | head -1                    # starts with "# c", no BOM
grep -c $'\r' "$f"                                 # 0: no carriage returns
tail -c1 "$f" | od -c | head -1                    # \n: ends with LF
grep -cvE '^(#|[HL] [0-9]+$)' "$f"                 # 0: no malformed lines
grep -c '^# session' "$f"                          # 1: no Pico resets
grep -n '^# dropped' "$f"                          # firmware drop reports, if any
awk '/^[HL] /{ if (p && $2 < p) b++; if (p && ($2-p)/1e6 > 1.1) g++; p=$2 }
     END { print "backwards", b+0, "gaps over 1.1 s", g+0 }' "$f"
```

A clean capture has two edges a second. In normal WWVB timing no two edges are more than 0.8 s apart, so a gap over 1.1 s means at least one second of signal was lost.

## Decoding a capture

`decode` runs the whole host pipeline on a capture, or on a live port, and prints one line per stage output:

```powershell
dotnet run --project host/src/WwvbDecoder.Tools -c Release -- decode capture captures/2026-11-09T0434Z-overnight.edges.txt --speed 0 > decode.txt
dotnet run --project host/src/WwvbDecoder.Tools -- decode port COM3
```

`--speed` paces the replay: 1 is real time (the default), 10 is ten times faster, 0 emits everything as fast as possible. A whole night at speed 0 takes about a second and prints about 160,000 lines, so redirect it to a file. At a paced speed every line is flushed as it happens. A paced replay cannot run faster than about 30x, because each wait is at least the Windows timer tick of about 15 ms.

One second of a locked frame looks like this. The pulse that started at the previous `High` is released by the next second's entering edge, so stages 2 to 4 report it one edge late:

```text
Stage 1 (Edge): Low 493484015195
Stage 1 (Edge): High 493484806641
Stage 2 (Pulse): start 493483807741 duration 207454 us
Stage 3 (Symbol): Zero start 493483807741 duration 207454 us
Stage 4 (Frame Sync): SlotFilled second=4 Zero
```

Each minute ends with the frame's four lines:

```text
Stage 4 (Frame Sync): FrameComplete start=472359798920 end=472419597018
Stage 5 (Frame Decoded): year=26 day=313 04:36 ut1Tenths=-1 leapYear=False leapSecondPending=False dst=Standard errorSeconds=[]
Stage 6 (Frame Validated): ACCEPTED
Stage 7 (Clock): Locked frameUtc=2026-11-09T04:36:00Z offsetFromPc=-91649.734 s recordedOffset=+0.796 s accepted=2 rejectedSince=0
```

Stage 5 prints the fields as transmitted (two year digits, UT1 in tenths), stage 6 lists each failed check on a rejected frame, and stage 7 is the status line:

- `offsetFromPc` compares the decoded time with the PC clock now. Under replay that is the age of the capture and means nothing.
- `recordedOffset` compares it with the PC clock at recording time, from the nearest `# wall` line; positive when WWVB was ahead of the PC. It is good to about a second: the recorder writes the wall time in whole seconds and pairs it with the last edge it had already written, so the value reads up to 0.8 s high. It is `unknown` for a capture without `# wall` lines and on a live port.

`Reset:` and `Info:` lines show the source's resets and `#` lines (the firmware banner, `# wall`, `# dropped`) in order with the stages. The run ends with a summary line (resets, frames, accepted, rejected, and stage 2's glitch, gap, and unpaired-edge counters) and the source's malformed line count.

## The first overnight capture

`2026-11-09T0434Z-overnight.edges.txt`: recorded on COM3 from 04:34:28Z to about 13:19:23Z on 2026-11-09 (22:34 to 07:19 local). File size 966,526 bytes, 63,522 lines.

| Check | Result |
| --- | --- |
| Encoding | No BOM, no CR, ends with LF |
| Malformed lines | 0 |
| `# session` lines | 1 (the start of the recording; no Pico resets) |
| Backwards timestamps | 0 |
| Gaps over 1.1 s | 0 |
| `# wall` lines | 525, one a minute from 04:35:00Z to 13:19:00Z |
| Edges per full hour | Exactly 7,200 every hour from 05Z to 12Z (7,201 in 10Z, from the glitch below) |
| Pulse widths | 20,186 at 0.2 s (0 bits), 7,634 at 0.5 s (1 bits), 3,675 at 0.8 s (markers): one pulse for every second of the run, with exactly seven markers a minute |

Reception was clean for the whole night. The only defect is one glitch at about 10:28:03Z, lines 42789 to 42791:

```text
L 493483014913
L 493483807660
H 493483807741
```

The pulse start at the top of that second bounced. The receiver output went high, dropped for 81 µs, and went high again, so three edges arrived microseconds apart. The firmware lost the first one, which leaves two `L` lines in a row and a `# dropped 1` line about five seconds later (line 42801; the firmware reports drops at most every 10 s). The pulse that follows is a normal 0.2 s zero bit. This is real-world input that the decoder must survive: a repeated level, an 81 µs spike, and a drop report that is not next to the edges it describes.

### Decode result

Run through `decode capture ... --speed 0` on 2026-11-10 (Step 11):

| Result | Value |
| --- | --- |
| Run time | 1 s, 160,101 lines |
| Frames completed | 524, one for every minute from 04:35Z to 13:18Z |
| Accepted | 524. Rejected 0, sync losses 0 |
| Clock | `Acquiring` after the first frame, `Locked` from 04:36Z and never dropped |
| Recorded offset | +0.794 s to +0.805 s on every frame. The 0.8 s is the recorder's whole-second rounding described under [Decoding a capture](#decoding-a-capture), so the PC clock was within a second of WWVB all night |
| Stage 2 counters | Glitches dropped 0, gaps bridged 0, unpaired edges 1 |
| Malformed lines | 0 |

The glitch cost nothing. The first `L` ended the previous pulse normally, stage 2 counted the second `L` as the one unpaired edge, the next pulse measured as a normal 207 ms zero, and stage 4 filled second 3 of the 10:28 frame with it. That frame was accepted and the clock stayed locked. The `# dropped 1` line appeared as an `Info:` line five seconds later and changed nothing.
