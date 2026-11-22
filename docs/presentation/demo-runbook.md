# Demo runbook

The checklist for the live demo on Thursday, 2026-12-10: what to bring, what to do before the talk, the demo, and what to do when something fails. The times below were measured on the build of 2026-11-22, unless the line says they were not.

## The plan

**Three things to show:** a clean decode, noise, and the same pipeline on another source. They are played in four parts: the clean decode, the charger, a replay, and last the `D` key.

**The source for the clean decode:**

| Order | Source | Use it when |
| --- | --- | --- |
| 1 | The live station | The clock is Locked ten minutes before the talk |
| 2 | The desk-top transmitter | It is not |
| 3 | Replay of the overnight capture | The logger gives no pulses, or the transmitter does not lock either |

**The one rule for the keyboard:** `D` is the only key that changes what runs. Do not press it while the live decode is what you are showing.

## What to bring

- The laptop with the `publish` folder on it, and its charger.
- The logger: the Pico 2 with the receiver on its breadboard, its USB cable, and the 5 m active USB extension.
- The transmitter: the second Pico with its coil, and its USB cable.
- A phone charger, for the noise.
- Tape, to hold the antenna at the window.
- A USB stick with a second copy of the `publish` folder.

## Before the talk

### In the morning

Open the [WWVB outage page](https://www.nist.gov/pml/time-and-frequency-division/time-distribution/radio-station-wwvb/wwvb-station-outages). If the station is off the air today, the source is the transmitter.

### 30 minutes before

1. Logger to the laptop through the extension.
2. Antenna at the west window, taped down, with the ferrite rod pointing north and south, so that its side faces Fort Collins in the west.
3. Keep it one to two metres from the laptop, the projector, and every charger and power supply.
4. Transmitter plugged in, its coil a metre or more from the antenna for now.

### 10 minutes before

1. Start `WwvbDecoder.Host.exe`. It starts what ran when it was last closed. If Live is not running, pick **Live**, the logger's port, and **Start**. The status bar reads `running` and the logger's own line, `# wwvb-edge-logger 0.1.0 pin=2`.
2. A good signal shows this:

   | Time from Start | On screen |
   | --- | --- |
   | At once | One pulse a second in the Signal panel, near 200, 500, and 800 ms in the Pulse width panel |
   | Within 1 minute | The Frame panel says `Synced` and fills a cell a second |
   | 1 to 2 minutes | The first `Accepted` row. The clock says Acquiring |
   | 2 to 3 minutes | The second row. The clock says **Locked** |

3. Not Locked after five minutes: go to [The desk-top transmitter](#the-desk-top-transmitter).
4. Drag the window onto the projector's screen and press `F11`. `T` switches the theme. The page starts dark every time.

## The demo

### 1. A clean decode

The overview is on screen and the clock is Locked. Walk the rail from left to right. A number key opens a stage full size and `Esc` comes back. The arrow keys and Page Up and Page Down walk the stages too, so a presenter remote works.

| Key | Stage | What to point at |
| --- | --- | --- |
| `1` | Edges | The receiver's output, as the Pico timestamps it: two edges a second |
| `2` | Pulses | The width of each drop in the carrier |
| `3` | Symbols | Three widths, three symbols: 0, 1, and marker |
| `4` | Frame sync | Two markers in a row start a minute. Sixty cells fill |
| `5` | Decode | The bits of the last frame as minute, hour, day, and year |
| `6` | Validate | One row a frame, and what each rejected one failed |
| `7` | Clock | The time, its status, and the offset from the PC's clock |

### 2. Noise, with the charger

Hold the charger at the antenna. This has not been tried yet. What the decoder's rules predict: the pulse widths smear, the Frame panel loses sync and searches, and no new row arrives. The clock keeps saying Locked and keeps counting, because its status changes only when a frame is validated, and a frame that never completes is not rejected. Take the charger away, and sync returns at the next minute mark and the rows a minute after that.

A frame that completes with a damaged bit shows as a `Rejected` row with the check it failed, and the clock goes to Holding until the next accepted frame. Part 4 shows that every time.

### 3. Another source: a recording

Nothing changes but the source. This ends the live decode, so it comes after everything you want to show live.

1. Pick **Replay** and the capture `2026-11-09 04:34 UTC · 8 h 45 min`, press `60×`, **Start**.
2. A frame arrives every second and the clock locks within a few seconds. The time on screen is the night of the recording. All 524 frames of that night are accepted.

### 4. A third source, with noise: `D`

Press `D`. It starts the synthetic source, a signal the PC makes up, at noise 0.10, whatever ran before. What follows is the same every time, because the synthetic source draws the same noise on each start:

| Time from `D` | What happens |
| --- | --- |
| At once | The app bar shows Synthetic at noise `0.10`, and every stage starts again. The clock says No signal |
| 61 s | The first row: `Rejected: BCD digits`. Its year column reads 106, because the tens digit of 26 decoded as 10. The clock says Acquiring |
| 121 s | An `Accepted` row, year 26 |
| 144 s | Sync is lost: a gap of two seconds. That minute gives no row |
| 241 s | An `Accepted` row, and the clock is Locked |

Press `D` again and the noise is 0 on the same source: a clean frame every minute, from the first whole minute after the press.

## The desk-top transmitter

The second Pico sends the time code on 60 kHz through its coil. The receiver and the logger do not change, and the app stays on Live on the logger's port.

1. Set the transmitter's clock from the laptop, in the `publish` folder, with the transmitter's port:

   ```powershell
   tools\WwvbDecoder.Tools.exe tx-sync COM5
   ```

   The lines `Time set: OK …` and `Status: OK …` mean it is sending, and its LED is steadily on. It sends nothing before that, and it forgets the time when it loses power.
2. Lay the coil **20 to 30 cm** from the ferrite rod, off to one side of it, with the coil's axis parallel to the rod. It worked from against the rod out to 50 cm and not at 1 m.
3. Press **Start** again in the app. That clears every stage, so the room sees the clock go from No signal through Acquiring to Locked, in two to three minutes.

`tools\WwvbDecoder.Tools.exe ports` lists the ports. To tell the two apart, plug in one Pico at a time. On the logger's port `tx-sync` says `The device is an edge logger, not a transmitter` and sends nothing.

A rejection on demand, while the clock is Locked on the transmitter: `tx-sync COM5 --offset 86400` sends tomorrow's date. The validator does not take a jump of a day from one frame, so expect a `Rejected: consecutive` row and the clock on Holding, then an accepted frame with the new date. A plain `tx-sync COM5` puts today back the same way, which is the way round that was seen on the bench.

## Replay

Needs nothing but the laptop. Pick **Replay**, the overnight capture, `1×`, **Start**: a live decode in everything but the date. Acquiring after about a minute and a half, Locked a minute later.

The app does this by itself at launch when the logger it remembers is not there, and says so in a banner.

## When something goes wrong

| What you see | Do |
| --- | --- |
| The port list says `No ports found` | Reseat both ends of the extension. Open the list again: it refreshes each time it opens |
| `running`, but no pulses and no logger line in the status bar | That port is the transmitter's: pick the other one and Start. Then check the receiver's wires |
| The status bar says `error` and a reason | Another program has the port, or it was unplugged. Replug, Start |
| Pulses of every width, no sync | Move the antenna away from the laptop and the projector, and turn the rod. Not Locked in five minutes: the transmitter |
| The transmitter is on and nothing locks | `tx-sync` again: it has no time after a loss of power. Coil to 20 to 30 cm, beside the rod |
| Banner: `The reduced-carrier level looks inverted…` | The sliders button in the app bar, then the other level |
| Banner: `COM… was not found. Replaying the newest capture.` | The logger was not there at launch. Plug it in, pick Live and the port, Start |
| Pressed `D` during the live decode | Pick Live, Start. The lock takes two to three minutes: talk through the stages meanwhile |
| A key does nothing | Another window is in front. Click the page once |
| The app shows a message box at start and closes | It names the cause. Copy the whole `publish` folder from the USB stick |

## Keys

| Key | Does |
| --- | --- |
| `1` to `7`, `Esc` | That stage full size, and back to the overview |
| Arrows, Page Up, Page Down | The stage before and after |
| `F11` | Full screen, and back |
| `T` | The other theme |
| `D` | The noise demo, on and off. **It changes what runs** |
| `?` | The list of keys |

`F5` does nothing in the published app, so a remote that sends it cannot reload the page.

## Checked

Both on 2026-11-22:

- The published app runs on a Windows 11 PC with no .NET installed.
- The demo was run from the `publish` folder.
