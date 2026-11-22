# Demo runbook

The checklist for the live demo on Thursday, 2026-12-10, and for the dry runs before it. It says what to do in what order, what the screen should show, and what to do when it does not.

Written on 2026-11-22 against commit `bab88bc`. Each time and count below was measured, unless the line says it was not: the app's on that build, the transmitter's on the bench on 2026-11-20 and 21, and the overnight capture's when it was first decoded. The dry runs fill in the tables under [Dry-run record](#dry-run-record), and what they find goes back into the steps above them.

## The plan on one page

**Three things to show:**

1. A clean decode: the signal becomes pulses, symbols, a frame, a time, and a locked clock.
2. Noise: the decoder loses the signal or rejects a frame, says why, and recovers.
3. The same pipeline on another source: a recording at 60 times its speed, then a synthetic signal.

They are played in four parts, because the dependable half of item 2 is itself a change of source: the clean decode, the charger, the replay, and last the `D` key.

**The source for item 1**, in order of preference:

| Order | Source | Needs | Use it when |
| --- | --- | --- | --- |
| 1 | The live station | The logger at the west window | The clock is Locked ten minutes before the talk |
| 2 | The desk-top transmitter | Both Picos on the table | The room shows pulses but no lock |
| 3 | Replay of a capture | The laptop only | The logger gives no edges, or there is no time left |
| 4 | Synthetic | The laptop only | The captures folder is missing too |

The replay and the `D` key need the laptop only, so they work whichever source item 1 used. The audience sees the same seven stages on all of them.

**The one rule for the keyboard:** `D` is the only key that changes what runs. Do not press it while the live decode is what you are showing. Every other key only changes the view.

## What to bring

- The laptop, charged, with its charger.
- The logger: the Pico 2 with the receiver module on its breadboard, and its USB cable.
- The 5 m active USB extension.
- The transmitter: the second Pico with its coil and resistor, and its USB cable.
- The phone charger used in the dry run, for the noise.
- Tape, to hold the antenna at the window.
- A USB stick with a second copy of the `publish` folder.

## At home, before the on-site week

1. **Publish.** In the repository: `./publish.ps1`. Copy the whole `publish` folder to the laptop and to the USB stick. Write the line of `publish\version.txt` into the record. When a newer build goes to the laptop later, keep the laptop's own `captures` folder: it holds what was recorded there. A capture worth keeping also belongs in the repository's `captures/`, and the next publish then carries it.
2. **The PC without .NET.** Run the steps under [The PC without .NET](#the-pc-without-net) on a Windows 11 PC that has no .NET installed.
3. **Find the two ports.** Plug in the logger only and run `tools\WwvbDecoder.Tools.exe ports` in the `publish` folder: the port it lists is the logger's. Plug in the transmitter: the new one is the transmitter's. The numbers have moved between sessions before, so use the same USB sockets, and the extension, on the day, and look again with `ports` anyway. `tx-sync` is a second check: on the logger's port it says `The device is an edge logger, not a transmitter` and sends nothing.
4. **Run the demo once from the `publish` folder**, start to end, as [The demo](#the-demo) describes it, at night when the station is strong. Fill in the record.
5. **Try the charger** (part 2 of the demo) and write down what it does and from how far. This is the one part nobody has tried yet.
6. **Record the charger**, so that real rejections can be replayed at will. Stop the app's live source first: one program can have the port at a time.

   ```powershell
   tools\WwvbDecoder.Tools.exe record --port COM6 --label charger
   # Hold the charger at the antenna for a minute, take it away for two. Do that three times. Ctrl+C.
   tools\WwvbDecoder.Tools.exe decode capture captures\<the new file> --speed 0 | Select-Object -Last 3
   ```

   The last lines count the frames, the accepted, and the rejected. A file with rejected frames in it is worth keeping. One with none goes out of `captures\` again, because the newest capture is the one the app replays by itself when the logger is missing.

## In the on-site week, in the room

Do these at the time of day of the talk, in the room if you can get it, at the office window if not.

1. **The window test.** Antenna at the west window on the extension, as under [30 minutes before](#30-minutes-before). Start Live and give it ten minutes. Write down whether it locks, and how many frames of ten were accepted.
2. **Record the room**: `tools\WwvbDecoder.Tools.exe record --port COM6 --label room`, ten minutes or more, with the app's live source stopped. Decode it as above. A good one is a replay that looks like this room. A bad one goes out of `captures\`.
3. **The transmitter in the room**, if the window test did not lock: the steps under [The desk-top transmitter](#the-desk-top-transmitter).
4. **The projector.** Drag the window onto the projector's screen, then `F11`. Check that the taskbar is covered, that the text can be read from the back row, and which theme reads better: `T` switches. The page starts dark every time, so a light theme means pressing `T` after each start.
5. **Other programs.** Keys go to the window in front. Close what pops up or takes the foreground, and switch Windows notifications off for the hour.

## The day

### In the morning

1. Open the [WWVB outage page](https://www.nist.gov/pml/time-and-frequency-division/time-distribution/radio-station-wwvb/wwvb-station-outages). If the station is off the air today, the source is the transmitter, and nothing else in this runbook changes.
2. Laptop on its charger. Sleep and the screen saver off for the day.
3. Check that `publish\version.txt` on the laptop is the build of the last dry run.

### 30 minutes before

1. Logger to the laptop through the 5 m extension. Use the USB socket of the dry run.
2. Antenna at the west window, taped down, with the ferrite rod pointing north and south, so that its side faces Fort Collins in the west.
3. Keep it one to two metres from the laptop, the projector, monitors, and every charger and power supply.
4. Transmitter on the table, plugged in, its coil well away from the antenna for now: a metre or more.

### 10 minutes before

1. Start `WwvbDecoder.Host.exe`. It starts what ran when it was last closed. If it shows `COM6 was not found. Replaying the newest capture.`, the logger has another port number today: pick Live, open the port list, pick the port, Start.
2. If Live is not running yet, pick **Live**, the logger's port, and **Start**. The status bar reads `running` and then the logger's own line, `# wwvb-edge-logger 0.1.0 pin=2`.
3. Watch it for five minutes:

   | Time from Start | What a good signal shows |
   | --- | --- |
   | At once | The Signal panel draws one pulse a second, and the Pulse width panel gathers them near 200, 500, and 800 ms |
   | Within 1 minute | The Frame panel says `Synced` and fills its cells one a second |
   | 1 to 2 minutes | The first row in Decoded frames, `Accepted`. The clock says Acquiring |
   | 2 to 3 minutes | The second row. The clock says **Locked** |

4. Decide the source:

   | After five minutes | Do |
   | --- | --- |
   | Locked, and each minute adds an `Accepted` row | Stay on Live |
   | Pulses, but sync is lost again and again, or rows are rejected | [The desk-top transmitter](#the-desk-top-transmitter) |
   | No pulses at all | The first rows of [When something goes wrong](#when-something-goes-wrong). If two minutes do not fix it: [Replay](#replay) |

5. `F11` for full screen, on the projector's screen. `Esc` for the overview.

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

On the live signal, or on the transmitter's. Hold the charger at the antenna.

What to expect, until the dry run says otherwise: the pulse widths smear, the Frame panel loses sync and searches, and no new row arrives. The clock keeps saying Locked and keeps counting, because its status changes only when a frame is validated: a frame that never completes is not rejected. Take the charger away, and sync returns at the next minute mark and the rows a minute after that.

A frame that completes with a damaged bit shows as a `Rejected` row with the check it failed, and the clock goes to Holding until the next accepted frame. Whether the charger ever does that is for the dry run to say. Part 4 does it every time.

### 3. Another source: a recording

Nothing changes but the source. Each Start clears all seven stages. This ends the live decode, so it comes after everything you want to show live.

1. Pick **Replay**. The list shows each capture with its date and length. Pick `2026-11-09 04:34 UTC · 8 h 45 min`, press `60×`, **Start**.
2. A frame arrives every second and the clock locks within a few seconds. The time on screen is the night of the recording, and the Time panel's offset row reads `Recorded offset`. All 524 frames of that night are accepted. After 8 minutes 45 seconds it starts again from the top.
3. `10×` and `1×` change the speed while it runs.

If the dry run left a charger capture with rejected frames in it, this is where it plays: real rejections, as often as you like.

### 4. A third source, with noise: `D`

Press `D` while the replay runs. It starts the synthetic source, a signal the PC makes up, at noise 0.10. What follows is the same every time, because the synthetic source draws the same noise on each start:

| Time from `D` | What happens |
| --- | --- |
| At once | The app bar shows Synthetic at noise `0.10`, and every stage starts again. The clock says No signal |
| 61 s | The first row: `Rejected: BCD digits`. Its year column reads 106, because the tens digit of 26 decoded as 10. The clock says Acquiring |
| 121 s | An `Accepted` row, year 26 |
| 144 s | Sync is lost: a gap of two seconds. That minute gives no row |
| 241 s | An `Accepted` row, and the clock is Locked |

Measured on the pipeline, and seen in the published app on 2026-11-22.

Press `D` again and the noise is 0 on the same source: a clean frame every minute, from the first whole minute after the press.

`D` gives the rejected row only when it starts the synthetic source, which it does when Live or Replay is running, or nothing is. Pressed while Synthetic already runs, it only turns the noise up on the running source: sync is lost within about three minutes every time, but a rejected row came within five minutes in 8 of 60 tries. So from Synthetic, press **Stop** first and then `D`.

Back to the station afterwards: pick **Live**, **Start**, and allow the two to three minutes of a new lock.

## The desk-top transmitter

The second Pico sends the time code on 60 kHz through its coil. The receiver and the logger do not change, and the app stays on Live on the logger's port.

1. Plug the transmitter in. It sends nothing until its clock is set, and it forgets the time when it loses power.
2. Set its clock from the laptop, in the `publish` folder, with the transmitter's port:

   ```powershell
   tools\WwvbDecoder.Tools.exe tx-sync COM5
   ```

   The lines `Time set: OK …` and `Status: OK …` mean it is sending, and its LED is steadily on.
3. Lay the coil **20 to 30 cm** from the ferrite rod, off to one side of it, with the coil's axis parallel to the rod. It worked from against the rod out to 50 cm and not at 1 m.
4. Press **Start** again in the app. That clears every stage, so the room sees the clock go from No signal through Acquiring to Locked, in two to three minutes. A clock that the station had already locked would otherwise just stay Locked, since the transmitter says the same minute.
5. The time on screen is the laptop's own, sent by radio over 25 cm and decoded again. Say so.

A rejection on demand, while the transmitter is the source and the clock is Locked: run `tx-sync COM5 --offset 86400`, which sends tomorrow's date. The validator does not take a jump of a day from one frame: expect a `Rejected: consecutive` row and the clock on Holding, then an accepted frame with the new date. A plain `tx-sync COM5` puts today back the same way. That way round is what was seen on the bench on 2026-11-21: one frame rejected, the clock held, and the next frame accepted.

## Replay

Needs nothing but the laptop. Pick **Replay**, the capture, a speed, **Start**. At `1×` the overnight capture is a live decode in everything but the date: Acquiring after about a minute and a half, Locked a minute later.

The app does this by itself at launch when the logger it remembers is not there. It then replays the newest capture and says so in a banner. The newest is the last one recorded, which may be the charger's or the room's. If that is not the one you want, pick the overnight capture from the list and Start.

## When something goes wrong

| What you see | Likely cause | Do |
| --- | --- | --- |
| The port list says `No ports found` | The logger is not plugged in, or the extension has no power | Reseat both ends of the extension. Open the list again: it refreshes each time it opens |
| `running`, but no pulses and no logger line in the status bar | The port is the transmitter's, or the receiver has come loose | Pick the other port and Start. Then check the receiver's four wires on the breadboard |
| The status bar says `error` and a reason | The port could not be opened: another program has it, or it was unplugged | Close the `record` or `decode` that still runs. Replug. Start |
| Pulses of every width, no sync | Noise near the antenna, or too little signal | Move the antenna away from the laptop and the projector, and turn the rod. If it does not lock in five minutes, use the transmitter |
| Banner: `The reduced-carrier level looks inverted…` | The level setting is the wrong way round | The sliders button in the app bar, then the other level. The banner goes at once |
| Banner: `COM… was not found. Replaying the newest capture.` | The logger was not there at launch, or has another port number today | Plug it in, pick Live and the port, Start. The cross on the banner closes it |
| The app starts on its own and plays something you did not ask for | It starts what ran when it was last closed | Stop. Or close it after a Stop, and it starts nothing next time |
| Pressed `D` during the live decode | `D` started the synthetic source | Pick Live, Start. The lock takes two to three minutes: talk through the stages meanwhile |
| A key does nothing | Another window is in front | Click the page once |
| Full screen is on the laptop's screen and not the projector's | `F11` fills the screen the window is on | `F11`, drag the window to the projector's screen, `F11` |
| The transmitter is plugged in and nothing locks | It lost power and has no time, or the coil is too far | `tx-sync` again. Coil to 20 to 30 cm, beside the rod |
| The app shows a message box at start and closes | It names the cause: the WebView2 runtime is missing, or the `wwwroot` folder is not beside the exe | Copy the whole `publish` folder from the USB stick. The runtime is part of Windows 11 |
| The app does not start at all | Not seen so far | `tools\WwvbDecoder.Tools.exe decode port COM6` prints the same seven stages as text in a terminal |

## Keys

| Key | Does |
| --- | --- |
| `1` to `7` | That stage, full size |
| `Esc` or `0` | The overview |
| Arrows, Page Up, Page Down | The stage before and after |
| `F11` | Full screen, and back |
| `T` | The other theme |
| `D` | The noise demo: Synthetic at 0.10, and back to 0. **It changes what runs** |
| `?` | The list of keys |
| `L` | The protocol log. Not for the audience |

`F5` does nothing in the published app, so a remote that sends it cannot reload the page.

## Dry-run record

### The PC without .NET

The step's exit criterion: the published app runs on a Windows 11 PC with no .NET SDK and no .NET runtime.

1. Copy the whole `publish` folder, by USB stick if you can. A zip that came through a browser or a cloud drive can make Windows show "Windows protected your PC" for an exe that is not signed: More info, then Run anyway.
2. Start `WwvbDecoder.Host.exe`. The window shows the rail of seven stages and the panels.
3. Synthetic, Start. Pulses scroll, and the clock locks in about two minutes.
4. Replay. The list shows the captures. Start at `60×`: rows are accepted and the clock locks.
5. Stop, and close the window.
6. In a terminal in that folder: `tools\WwvbDecoder.Tools.exe decode capture captures\2026-11-08T2300Z-desk.edges.txt --speed 0`. The last lines say `Frames: 9, Accepted: 9`.

| | |
| --- | --- |
| Date | |
| Windows version (Settings, System, About) | |
| The line in `version.txt` | |
| Steps 2 to 6 | |
| What went wrong, if anything | |

### At home

| | |
| --- | --- |
| Date and time of day | |
| The line in `version.txt` | |
| Logger's port, transmitter's port | |
| Live: minutes from Start to Locked | |
| Live: frames accepted of the next ten | |
| Charger: which one, and from how far it has an effect | |
| Charger: what the screen showed (lost sync, rejected rows, the clock's status) | |
| Charger capture: file, frames, rejected | |
| `D` from Live: the rejected row at about one minute | |
| Replay at `60×` | |
| Transmitter: minutes from the new Start to Locked, and the coil's distance | |
| What went wrong, and what was changed | |

### At the office window

| | |
| --- | --- |
| Date and time of day | |
| Room, and where the antenna was | |
| The line in `version.txt` | |
| Live: locked or not, after how long | |
| Live: frames accepted of ten | |
| Room capture: file, frames, accepted | |
| Transmitter in the room: minutes to Locked | |
| Projector: theme chosen, readable from the back row | |
| `F11` on the projector's screen | |
| The source chosen for the day | |
| What went wrong, and what was changed | |
