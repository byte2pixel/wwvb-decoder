# UI design

The layout, visual language, and page structure of the visualizer (`ui/`). The libraries are decided in [ADR-0007](adr/0007-tailwind-shadcn-hand-drawn-views.md); the messages the page consumes are specified in [`webview2-messages.md`](webview2-messages.md). When this document and that one disagree about a payload, that one is right.

The screen has two jobs. It shows the whole pipeline working at a glance, including a fault moving from one stage to the next. And it lets the presenter stop on one stage and explain it to the back of the room. Those are the two modes: **overview** and **focus**.

## Principles

1. **The pipeline is the structure.** Seven stages, seven message types, seven entries in the rail. Every panel names the stages it shows.
2. **Order by time scale.** Top to bottom: the signal (changes every second), the frame (fills over a minute), the decoded frames (one row a minute), the time.
3. **One time axis for stages 1 to 4.** The carrier, the measured pulse, the classified symbol, and the second it fills are the same instant at four levels of meaning, so they are lanes on one axis.
4. **The page never decodes.** Field values, verdicts, and clock status come from the host. The page may count, group, and label what it receives.
5. **Hue means health.** Green, amber, and red are reserved for status. Data uses one accent color: a 1 is lit, a 0 is dim, a marker is neutral.
6. **Every panel has a state for no data.** Nothing is blank or broken before a source starts or while a frame is filling.
7. **Built for a projector.** The whole screen scales with the window, text meets contrast targets, and nothing depends on hue alone.

## Screen regions

The design baseline is 1280 by 720 CSS pixels. Sizes below are at the baseline.

```text
+--------------------------------------------------------------------------+
| App bar: name, source controls, stop, settings                       46  |
+--------------------------------------------------------------------------+
| Pipeline rail: 1 Edges  2 Pulses  3 Symbols  4 Frame sync  5 Decode      |
|                6 Validate  7 Clock                                   58  |
+----------------------------------------------------+---------------------+
| Signal (stages 1 to 4, lanes on one time axis) 178 | Pulse width (3) 178 |
+----------------------------------------------------+---------------------+
| Frame (stage 4): 3 rows of 20 cells            226 | Time (stage 7)      |
+----------------------------------------------------+                     |
| Decoded frames (stages 5 and 6): table       fills |               fills |
+----------------------------------------------------+---------------------+
| Status bar: run state, source, sequence, key hints                   24  |
+--------------------------------------------------------------------------+
```

The right column is 344 wide. Panels are 12 apart, with 12 of padding around the content area. The two fixed rows leave the decoded-frames panel 140 at the baseline, which is what its heading row and three frames need.

A warning from the host adds a row of 32 between the app bar and the rail for as long as it shows (see [Warning banner](#warning-banner)). The decoded-frames panel gives up the room.

### Scaling

The page scales as one piece, like a slide. The root font size is `min(100vw / 80, 100vh / 45)`, which is 16 px at 1280 by 720 and 24 px at 1920 by 1080. Every size is in `rem`, so Tailwind's spacing and type scales follow. Borders stay 1 px. At another aspect ratio the two flexible panels take the extra room.

Canvas views measure their own box with a `ResizeObserver` and multiply by `devicePixelRatio`, so they stay sharp at any scale.

## Tokens

Starting values. Tune them in the room during the on-site week; the names are what the code depends on.

| Token | Dark | Light | Use |
| --- | --- | --- | --- |
| `bg` | `#0B0E13` | `#EEF1F5` | Window background |
| `bar` | `#0F1319` | `#FFFFFF` | App bar, rail, status bar |
| `panel` | `#12161D` | `#FFFFFF` | Panel surface |
| `control` | `#171C25` | `#F3F5F8` | Buttons, inputs, badges |
| `hover` | `#262E3B` | `#DCE2EA` | A hovered or selected control |
| `line` | `#232A36` | `#DDE2E9` | Panel borders, grid lines |
| `line-strong` | `#3A4556` | `#B4BDCA` | Control borders, empty marker cells |
| `text` | `#E8ECF2` | `#111827` | Primary text |
| `text-2` | `#AEB7C4` | `#3F4A5A` | Secondary text |
| `text-3` | `#8590A0` | `#5B6676` | Labels, axis text |
| `accent` | `#4CC2FF` | `#0B6BCB` | Carrier trace, a 1, selection |
| `on-accent` | `#06202F` | `#FFFFFF` | Text on the accent |
| `zero` | `#9BD8FB` on a 10% accent tint | `#0B5CAD` on a 7% tint | A 0 in a field |
| `marker` | `#E8ECF2` on `#2A313D` | `#111827` on `#E4E8EE` | A marker |
| `good` | `#4ADE80` | `#137A3E` | Locked, accepted, running |
| `warn` | `#FBBF24` | `#8A5A00` | Acquiring, holding, errors seen |
| `bad` | `#FF6B6B` | `#B42318` | Error symbol, rejected, source error |

### Variable names

shadcn/ui components expect a fixed set of CSS variables, so the tokens are declared under those names where one exists and under their own names otherwise. shadcn's `--accent` means the hover surface, which is why the data accent is called `signal` in CSS.

| Token | CSS variables |
| --- | --- |
| `bg`, `text` | `--background`, `--foreground` |
| `panel` | `--card`, with `--card-foreground` set to `text` |
| `control` | `--secondary`, `--muted`, `--popover`, each with its `-foreground` set to `text` |
| `hover` | `--accent`, with `--accent-foreground` set to `text` |
| `line`, `line-strong` | `--border`, `--input` |
| `text-3` | `--muted-foreground` |
| `accent`, `on-accent` | `--signal` and `--on-signal`; also `--primary`, `--primary-foreground`, and `--ring` |
| `bad` | `--bad`; also `--destructive` |
| `text-2` | `--foreground-soft`, so the utility reads `text-foreground-soft` |
| `bar`, `zero`, `marker`, `good`, `warn` | `--bar`, `--zero` and `--zero-surface`, `--marker` and `--marker-surface`, `--good`, `--warn` |

- Light values go in `:root` and dark values under `.dark`, as shadcn generates them, so its components work unchanged. `<html>` carries `class="dark"` by default and the theme button toggles it.
- Each variable is mapped into Tailwind in the `@theme inline` block (`--color-signal: var(--signal)`), which gives utilities such as `bg-signal` and `text-good`.
- Canvas views read the plain variables from the root element with `getComputedStyle`, and read them again when the theme changes. They do not read the `--color-*` names, because Tailwind only emits the ones a utility uses.
- `--radius` is `0.625rem`, shadcn's default, which gives the 10 px panel radius.

Type: Inter for interface text, JetBrains Mono for anything measured or decoded. `font-variant-numeric: tabular-nums` everywhere, so changing digits do not shift.

| Role | Size at baseline | Face |
| --- | --- | --- |
| Clock digits | 60 px | Mono, 600 |
| Focus view title | 22 px | Inter, 600 |
| Panel title | 13 px | Inter, 600 |
| Body and controls | 12.5 to 13 px | Inter |
| Data values | 12 to 13 px | Mono |
| Labels | 10.5 px, uppercase, 0.06 em tracking | Inter, 600 |
| Bit weights in the overview frame | 9.5 px | Mono |

Radius: 10 px for panels, 7 to 9 px for controls, 5 px for cells and chips. No shadows; surfaces are separated by borders.

Text contrast is at least 4.5 to 1 on its surface, in both themes. A state is never shown by hue alone: it also has a word, a glyph, or a difference in lightness.

## Pipeline rail

Seven buttons in a row, joined by a line. Each shows the stage number, its name, the latest output, and a health dot. Clicking one, or pressing its number, opens that stage's focus view.

The focused stage's button is highlighted with an accent border on the hover surface, and it is marked as pressed for a screen reader. Clicking the highlighted button goes back to the overview, because a pointer has no `Esc`.

The dot is a glyph as well as a color, so it does not depend on hue: a ring for no data, a check for good, a triangle for warn, and a cross for bad. Each carries its meaning as a text alternative: `no data`, `good`, `warning`, `fault`.

| Stage | Message | Readout | Health |
| --- | --- | --- | --- |
| 1 Edges | `edges` | The last edge as the serial line writes it, `L 2617209411` | Good once an edge has arrived; bad when `sourceStatus` is `error` |
| 2 Pulses | `pulse` | Last duration, `502 ms` | Good |
| 3 Symbols | `symbol` | Last kind, `one` | Warn if any of the last 60 symbols is `error` |
| 4 Frame sync | `frameSync` | `37 of 60`, or `searching` | Good when synced; warn while searching; bad for 5 s after `syncLost` (see [Sync loss](#sync-loss)) |
| 5 Decode | `decodedFrame` | `19:32 day 344` | Warn if `errorSeconds` is not empty |
| 6 Validate | `validation` | `accepted` or `rejected` | Good or bad |
| 7 Clock | `clock` | The status | Good when `locked`; warn when `acquiring` or `holding`; `text-3` when `noSignal` |

Before a source starts, every readout is a dash and every dot is `text-3`. A source reset returns the rail to that state. `stop` does not: the readouts and dots keep their last values, and the status bar shows that the source stopped.

When a frame completes, the buttons of stages 4 to 7 light one after the other, in the order the frame passes through them (see [Motion](#motion)).

## Panels

Every panel has a header with its title, a chip for each stage it shows, and one line of state on the right.

### Signal

Four lanes over the same time axis, with a line at every second.

| Lane | Stage | Shows |
| --- | --- | --- |
| Carrier | 1 | A square wave built from edges. The reduced-carrier level is drawn low. An edge pair too short to become a pulse still shows here, as a narrow dip |
| Pulse | 2 | A bar as long as each measured pulse, and its duration in milliseconds |
| Symbol | 3 | A chip per pulse: `0`, `1`, `M`, or a red `×` |
| Second | 4 | The second the symbol filled, `:17`. A dash while searching |

- The newest second is drawn up to a cursor at the estimated present (see [Source clock](#source-clock)). Nothing is drawn to the right of the cursor, even when an edge has arrived ahead of the estimate. When there is no present, because the source stopped or was reset, the window ends at the newest edge and the last picture stays on screen.
- Stage 2 reports a pulse only when a later edge confirms it, about a second after the pulse starts. Until then the pulse lane draws every reduced-carrier interval that starts after the last measured pulse as a bar that reads `pending`, growing to the cursor while the carrier is still reduced. A dip that stage 2 drops as a glitch is such a bar for about a second; after that it shows in the carrier lane only. The stage 2 focus view keeps it and names it (see [Stage 2: pulses](#stage-2-pulses)).
- A pending bar is drawn as an outline, not filled. One too narrow to carry the word gets no label, so a glitch does not flash `pending`.
- The symbol lane has one chip for each bar. A chip reads `pending` until the symbol with the same start time arrives. A pending chip is an outline too, and with the labels hidden it shrinks to an ellipsis.
- A `slotFilled` event labels the symbol that arrived just before it. A symbol that filled no second, because frame sync was searching, gets a dash. The newest symbol gets nothing until its `slotFilled` arrives or a newer symbol does: the two messages arrive a few milliseconds apart, and a dash must not flash between them.
- A bar that the window's left edge cuts slides out under the edge. Its duration, its chip, and its second label leave with it, and the duration stays the measured one.
- The lines at every second take their phase from the frame in progress: the frame start of the newest filled slot. Before frame sync has placed anything they take it from the newest measured pulse, since a pulse starts at the top of a second. Before the first pulse there are no lines.
- The window is 12 s at 1× speed. Above 1× it is 60 s and the pulse and second labels are hidden, because they would be unreadable.
- The header's state line names the window, `12 s window` or `60 s window`, because the time axis carries no numbers.
- Drawn on a canvas, once per animation frame, straight from the store's buffers.

### Pulse width

A histogram of the last 600 symbol durations from 0 to 1,000 ms.

- Bins are 25 ms wide, so the class boundaries at 100, 350, 650, and 900 ms fall on bin edges.
- Bars are colored by class: zero dim accent, one accent, marker neutral, error `bad`.
- A bar's class is the host's: the `kind` of the newest symbol in its bin. The page does not classify a duration. Only the bin from 900 ms can hold two classes, because the host still accepts exactly 900 ms as a marker.
- A duration of 1,000 ms or more is counted in the last bin, so a long error still shows.
- A bar's height is its count against the fullest bin, and never less than 0.125 rem, so a single pulse still shows beside a full bin.
- The two decision thresholds are solid lines; the valid range limits are dashed, with the error zones tinted. They are drawn from constants that repeat the defaults of the host's `SymbolClassifier`, because no message carries them. The axis names the four in milliseconds. Its text is 10.5 px in the overview and 16 px where the panel is wide, as in the stage 3 focus view; a container query on the view's width decides.
- A dashed line marks the last pulse, and the header line reads `Last pulse 502 ms classified as 1`. The class is written `0`, `1`, `M`, or `error`. Before the first pulse the line reads `No pulses yet`. The line of a pulse of 1,000 ms or more sits at the right edge.
- Each bar and each error zone has a `title`, such as `200 to 225 ms: 14`, which a pointer shows as a tooltip.
- Drawn in DOM. Every position is a percentage of the plot, so the view measures nothing and scales with its panel.
- The stage 3 focus view asks the panel for its details: the zones named above the plot, and the last pulse's duration on its line (see [Stage 3: symbols](#stage-3-symbols)).

### Frame

The 60 seconds of the frame in progress, in three rows of 20. Every BCD field stays inside one row, and the position markers line up in the 10th and 20th columns.

- A cell shows the symbol placed by `slotFilled`, written as the signal panel's chips write it: `0`, `1`, `M`, and `×` for an error. Cell states: empty, empty marker slot (stronger border), `0` in a field, `1`, `M`, error, and the cell being received now (accent border).
- An unused second has a dashed border, empty or holding its 0, and its 0 has no fill. The time code leaves these seconds at 0, so without the dashes they read as part of the field beside them.
- Above each cell: its bit weight, or the marker's name (`Pr`, `P1` to `P5`, `P0`). Below each row: a bracket under each field with its name: Minute, Hour, Day of year, UT1 sign, UT1, Year, LY, LS, DST. A bracket runs from the field's first second to its last, across a marker or an unused second between them.
- The cell being received is the second after the last one placed. Frame sync places a second at the moment the next one begins, so no clock is needed. After second 59, and while frame sync is searching, no cell is marked.
- The overview shows bits only. Values appear in the decoded-frames table, because the host decodes only a complete frame.
- A `slotFilled` with a new `frameStartMicros` clears the cells first. A loss of sync does not: the cells keep what was placed, so the place where it broke stays on screen until the next frame starts.
- Header line: `Synced, 37 of 60 symbols` with a progress bar; `Searching, 1 of 2 markers`; or, for 5 s after `syncLost`, `Sync lost: ` and the host's reason, in the fault color. With a source running and no event yet it reads `Searching`; with no source it is empty.
- Which seconds belong to which field comes from the host (see [Protocol additions](#protocol-additions)). Before the layout arrives the cells have no weights, names, or brackets.
- Drawn in DOM. A second is a twentieth of its row, so a bracket's place is a percentage and the view measures nothing. The type follows the panel's height, not its width: the overview's panel is wide and short, and the focus view's is tall.

### Decoded frames

One row per `validation` message, newest first. Three rows in the overview, and the newest 16 in the stage 6 focus view; the store keeps 20. Sixteen is what a 16:9 window holds without cutting a row, and the page scales with the window, so that is true at 720p and at 1080p alike. In a window with less room the table scrolls under its headings.

| Column | Content |
| --- | --- |
| Frame | A count since the last source reset, `#41`. It keeps counting past the 20 frames the store holds |
| Symbols 0 to 59 | Sixty ticks: tall accent for a 1, short and dim for a 0, tall neutral for a marker, `bad` for an error. The strip's tooltip writes the symbols out: `M`, `0`, `1`, and `×` |
| Min, Hour, Day, Year | The decoded values as received, with no correction |
| UT1 | Sign and seconds, `+0.3 s` or `-0.7 s`. An invalid sign is written `?` |
| DST | `standard`, `dst`, `begins today`, `ends today` |
| Result | `Accepted`, or `Rejected: ` and the first failure's check in words, such as `error symbols` or `BCD digits`. A rejected row has a `bad` tint |

The result's tooltip lists every failure of a rejected frame, one to a line: the check in words, then the host's `detail`. The stage 6 focus view lists all ten checks of one frame beside the table, and there a row can be selected (see [Stage 6: validate](#stage-6-validate)).

A row slides in when its frame arrives (see [Motion](#motion)).

Before the first frame the panel shows its column headings and `No frames yet`.

### Time

- The clock digits are `frameUtc` plus the source time elapsed since `frameStartMicros`, so replay at 10× runs the clock at 10×. Before the first accepted frame they read `--:--:--`.
- The digits tick on the decoded second, read from the source clock on every animation frame. With no present to read, as when the source is stopped, they show the time at the end of the last accepted frame.
- Under the digits: the date and day of year, `2026-12-10 · day 344`.
- The four statuses in a row, No signal, Acquiring, Locked, Holding, with the current one outlined in its health color. The panel's header line names it too.
- Four rows: last accepted frame, `19:30 UTC`; accepted in a row, or rejected since then as `2 of 3`; offset from the PC clock, `+37 ms`; local time, US Central, `13:30:07 CST`.
- The last accepted frame's time is the frame's own, the time at its first second. A frame is only known when it has ended, so that row and the decoded-frames table read a minute behind the digits. The digits add the source time since the frame began.
- The status changes only when a frame is validated. A loss of frame sync yields no frame, so the clock stays as it was, Locked included, while the frame panel searches; it goes to Holding on the next rejected frame.
- One font size, set from the panel's width and height, scales everything in the view.
- Under replay the offset row reads `Recorded offset` and is computed from the most recent `# wall` line received as `info`. It shows a dash until one arrives.
- Local time uses the `dst` field. On a `beginsToday` or `endsToday` day it uses the offset the day began with and is marked approximate, because the bits do not carry the 02:00 switch.

### App bar

Name; a three-way source control (Live, Replay, Synthetic); the selected source's own controls; Start and Stop; and three icon buttons for signal settings, theme, and keyboard shortcuts.

| Source | Controls | Commands |
| --- | --- | --- |
| Live | Port list, refreshed on open | `selectSource` serial, `listPorts` |
| Replay | Capture list with date and length, refreshed on open; speed 1×, 10×, 60× | `selectSource` replay, `setReplaySpeed`, `listCaptures` |
| Synthetic | Noise slider, 0 to 1 | `selectSource` synthetic, `setNoise` |

- **Start.** Choosing a source, a port, a capture, a speed, or a noise level changes nothing in the host until Start is pressed. Start sends `selectSource` for the source on show. It is disabled while that source has nothing to start: no port is listed, or no capture.
- **The default pick.** With nothing picked, the first port and the newest capture count as picked. A picked port or capture that leaves its list gives way to that default and is picked again when it comes back, so a logger that is unplugged and plugged in again is still the one selected.
- **While its source runs.** A speed button sends `setReplaySpeed` at once while a replay is running. With another source running, or none, it only sets what the next Start sends.
- **The noise slider** sends `setNoise` once, when the thumb is let go, while the synthetic source is running. The readout beside it follows the thumb all the way, so the level can be read before it is sent. A step made with an arrow key is let go at once, so each key press sends. With another source running, or none, the slider only sets what the next Start sends.
- **A capture row** reads `overnight · 9 Nov 2026, 04:34 UTC · 8 h 45 min`. First is the label the capture was recorded under, the part of its file name after the timestamp, because that is what tells two rows apart at a glance. Then when it was recorded, in UTC, with the month as a word, and how long it runs. A file that is not named as [`capture-format.md`](capture-format.md) names one shows its name without the extensions.
- **An empty list** reads `No ports found` or `No captures found`, on its button and as its only row.
- **Signal settings** is a popover with the reduced-carrier level, High or Low. A pick is sent as `setReducedCarrierLevel` at once, whether or not a source runs, because the host keeps the level from one run to the next. The signal panel reads the carrier with the same level.
- **The pressed button** of the source, the speed, and the level is drawn in the accent color, so the choice can be read from the back of the room.

The app bar starts from the host's [`selection`](webview2-messages.md#selection), which arrives with the reply to `hello`: the source, the port, the capture, the speed, the noise, and the level. So a reloaded page shows what the host is running, and a restarted host shows what it ran last, and starts it again. Until `selection` arrives the values are the host's defaults: Synthetic, nothing picked (so the first port and the newest capture count as picked), 1×, noise 0, and High.

- **No echo.** `selection` comes once for each page load. After that a pick changes the app bar at once, and the host learns of it from the command that carries it: `selectSource` on Start, and `setReplaySpeed`, `setNoise`, and `setReducedCarrierLevel` as above.
- **A remembered port that is unplugged** is not in the list. It stays the pick, by the rule of the default pick, and is shown again when the port comes back. A capture that is gone arrives as `null`.
- **What is on show and what is running can differ**, as before: Replay picked while Synthetic runs changes nothing until Start. The status bar says what is running.

### Warning banner

A row between the app bar and the rail, 32 high, that is there only while the latest `sourceStatus` carries a `warning`. It is the one place where the host tells the presenter something to act on: that the logger was not found and a capture is playing in its place, or that the reduced-carrier level looks inverted (Step 15, Task 3).

- The text is the host's, shown as it comes. The page does not parse it.
- It is drawn in the warning color with a warning glyph before the text, so it does not depend on hue, and it is announced to a screen reader as a status.
- A close button at its right end hides it. The banner stays hidden while the host keeps sending the same text. It shows again when the text changes, and when the same text comes back after a status without a warning.
- It takes no key, and the keyboard map is the same while it shows.

### Status bar and protocol log

The status bar shows the run state from `sourceStatus`, the source's name and detail, the last `seq` with a count of gaps, and the key hints: `1 to 7 stage · Esc overview · ? shortcuts`. The protocol log from Step 8 becomes a drawer opened with `L`. It is a development tool and is closed on stage.

## Focus views

Pressing `1` to `7` replaces the panels with one stage, sized to be read from the back of the room. The app bar, rail, and status bar stay, and the rail highlights the stage. Each focus view has the same header: the stage number, its name, its type (`IStage<Frame, DecodedFrame>`), and its rule in a sentence or two.

Under the header, a view shows its stage at work: what goes in, the rule applied to it, and what comes out. Step 16 built these views. Until then a focus view was the overview's panel enlarged.

| Stage | Shows |
| --- | --- |
| 1 Edges | The carrier lane over the source's informational lines, beside the last 50 edges as the serial protocol writes them |
| 2 Pulses | The carrier and pulse lanes, with each glitch that stage 2 dropped or bridged marked and named |
| 3 Symbols | The pulse-width panel with its five zones named and the last pulse's duration on its line, beside that pulse as input, rule, and output, over the symbol lane |
| 4 Frame sync | The frame panel, over frame sync's two states as a diagram and the last five sync-lost reasons |
| 5 Decode | The last complete frame with each field's value and the weights of its set bits, beside the record handed to stage 6 |
| 6 Validate | The table at 16 rows, beside the ten checks of one frame |
| 7 Clock | The time panel beside the four statuses as a state diagram, with the two counters |

A view is built from panels, each with its header and its error boundary. The signal, pulse-width, frame, decoded-frames, and time panels are the overview's own. The others appear only in a focus view. A signal panel that shows fewer lanes names those lanes and carries only their stage chips.

### Stage 1: edges

The edges are listed newest first, as the protocol log lists its messages, each as the rail writes the last edge. The 50 lines run down three columns, filled one after the other. Before the first edge the list reads `No edges yet`.

Under the carrier lane a panel, Info lines, lists the newest four informational lines the source sent, newest first and as the source wrote them. The store keeps the newest 50.

- A live source sends the logger's banner and its `# dropped` reports.
- A replay sends the capture's header lines and a `# wall` line for each minute.
- The synthetic source sends none, and the panel reads `No info lines yet`.

### Stage 2: pulses

The pulse lane also shows what the 50 ms rule did, on the glitches of the signal itself.

- **Dropped.** A stretch of reduced carrier that no measured pulse covers was too short to be a pulse. It is drawn as the outline of the bar it did not become, with `dropped` under the line of durations.
- **Bridged.** A stretch of full carrier inside a measured pulse was too short to end it. It is drawn as a notch cut into the top of that pulse's bar, with `bridged` under the line of durations. The bar still runs on under the notch, as the pulse did.
- The page finds both by comparing the edges with the pulses the host measured. It applies no threshold of its own, so a mark is the host's decision and not a second opinion.
- A dip reads `pending` until a later pulse is measured, as in the overview, and `dropped` from then on. Nothing older than the oldest pulse the store holds is marked, because the page cannot tell a dropped glitch from a pulse it no longer has.
- A glitch of a few milliseconds is a hairline at 12 s to the window, so a mark is never drawn less than 2 px wide.
- The names need a line of their own, and so a pulse lane at least 8 rem tall. This view's is 9.6 rem. The overview's is under 2 rem and shows no marks: there a dropped glitch still shows in the carrier lane only.
- Above 1× the labels are hidden, and the marks stay without their names.
- When the reduced-carrier level is changed while a source runs, the pulses on screen were measured with the other level, and the stretches between them read as dropped until they have scrolled out of the window.

The host also counts what it dropped and bridged. Those counts are not shown (see [Protocol additions](#protocol-additions)).

### Stage 3: symbols

The pulse-width panel is drawn with its details:

- Above the plot each of the five zones is named over its stretch of the axis: `error`, `0`, `1`, `M`, `error`. The zone the last pulse landed in is marked.
- The last pulse's line carries its duration, `502 ms`. The label is right of the line, and left of it past the middle of the axis, so it stays inside the plot.
- A new pulse is a new line, and it drops into place (see [Motion](#motion)).

Beside it a panel, Last pulse, shows the stage at work on that pulse, from top to bottom:

- **Pulse.** The duration, to the millisecond.
- **Rule.** The classifier's five rows, shortest pulse first: `under 100 ms` is `×`, `from 100 ms` is `0`, `from 350 ms` is `1`, `from 650 ms` is `M`, and `over 900 ms` is `×`. The row the pulse landed in is marked.
- **Symbol.** The symbol as a chip in the colors of the frame's cells, with its kind in words beside it.

The class is the host's. The page marks the zone and the row of the kind the host sent and compares no duration with a threshold. An error is the one exception: two zones are errors, and the duration says on which side of the valid range the pulse fell. Before the first pulse the panel reads `No pulses yet`.

### Stage 4: frame sync

Under the frame panel are two panels side by side.

Frame sync states draws the two states, Searching and Synced, with an arrow each way: `2 markers a second apart` and `sync lost`.

- The state frame sync is in is tinted and carries its count: `1 of 2 markers` while searching, `37 of 60 symbols` while synced. Synced is in the good color and Searching in the warning color.
- For the 5 s that the store holds a loss, the arrow back reads `sync lost: ` and the host's reason, in the fault color, and Searching is drawn as a fault, as its rail button is.
- With a source running and no event yet it is Searching, the state frame sync starts in. While no source has run, neither state is marked.

Sync losses lists the newest five losses of frame sync, newest first, each with its count since the source reset and the host's reason: `#2 gap 2000 ms before second 15`. The frame panel's header shows a loss for 5 s; this list keeps it until the source resets. Before the first loss it reads `No sync loss yet`.

### Stage 5: decode

One panel, Last complete frame, shows the newest `decodedFrame`:

- Its 60 symbols are drawn in the frame panel's grid, with the same weights, marker names, and brackets. No cell is marked as being received, because the frame is over.
- Under each field's name is the value the host decoded: the minute, hour, day, and two-digit year as numbers; the UT1 sign as `+`, `-`, or `?`; the correction as `0.3 s`; `yes` or `no` for LY and LS; and the DST status in the table's words.
- Under each value are the weights of the field's bits that are 1, in the order they are sent: `20 + 10 + 4 + 1` under a minute of 35. A field with no bit set reads `none`. The UT1 sign and the three flags have no such line, because their seconds carry no weight. UT1's weights count tenths of a second.
- Only a 1 counts. The host reads an error symbol as 0, so the weights listed add up to the value above them. The page still does not add: it lists the weights from `frameLayout` and the frame's symbols, and the value is the host's.
- The header line names the seconds that hold an error symbol, `Error symbol in second 44`, or reads `No error symbols`. It counts the frame's own symbols, so it names every cell drawn as an error. The host's `errorSeconds` lists field seconds only and leaves out an unused second.
- Before the first frame the cells are empty, the brackets hold the names alone, and the header line reads `No frame yet`.

Beside it a panel, Decoded frame, lists the record that stage 5 hands to stage 6. The frame panel shows the values in words; this one shows them as the validator gets them.

- One row for each property of the host's `DecodedFrame`, under its C# name and in the order C# declares them: `Frame`, `Minute`, `Hour`, `DayOfYear`, `Year`, `Ut1Sign`, `Ut1Tenths`, `LeapYear`, `LeapSecondPending`, `Dst`, and `ErrorSeconds`.
- `Frame` reads `60 symbols`. An enum reads as C# names it, `EndsToday`. `ErrorSeconds` reads `6, 31`, or `none`.
- Before the first frame it reads `No frame yet`.

### Stage 6: validate

The table lists the newest 16 frames as the overview lists three. Beside it a panel, Checks, lists the ten checks of one frame.

- **Which frame.** The newest one, and the panel's header line says so: `Frame #41, the newest`. The list follows each new frame.
- **Selecting.** A click on a row selects its frame. The list stays on it while newer frames arrive, and the header line reads `Frame #39, selected`. A second click lets go of it, and the list follows the newest frame again.
- The frame's number in the row is the button, so the keyboard reaches it, and it covers its whole row for the pointer. A row's tooltips do not show in this view for that reason; the list holds what the result's tooltip would say.
- The row of the frame on show is outlined in the accent color, selected or not. A selected frame's number is in the accent color too.
- **The verdict.** Over the list, one line: the frame's number, its time as it was decoded, `19:32 day 344`, and `Accepted` or `Rejected, 1 of 10 checks failed`.
- **The checks**, in the host's order. Each has a check mark or a cross, the word `passed` or `failed` for a screen reader, its name, and a second line. For a check that passed the line says what the check asks. For one that failed it is the host's `detail`, in the fault color.
- What a check asks repeats the host's contract, because no message carries it.
- The message lists failures only. A check with none reads as passed, and that includes a `consecutive` check the host skipped.
- A selected frame is shown while its row is in the table. When 16 newer frames have pushed it out, the list goes back to the newest. A source reset forgets the selection, because the frames are numbered from 1 again.

Sixteen rows fit a 16:9 window; past that the table scrolls under its headings.

### Stage 7: clock

Beside the time panel a panel, Clock states, draws the four statuses as a state diagram.

- An arrow for each move, with what causes it: No signal to Acquiring on the `first frame`; Acquiring to Locked on `2 accepted in a row`; Locked to Holding on `rejected`; Holding back to Locked on `accepted`; and Holding to Acquiring on `3 rejected in a row`. A line under the diagram says that a source reset returns the clock to No signal.
- The status the clock is in is tinted in its health color, as its chip in the time panel is.
- Beside the diagram are the two counters the host keeps, each with the number at which it moves the clock: `Accepted in a row`, of which 2 lock the clock, and `Rejected in a row`, of which 3 end the hold. They are `consecutiveAccepted` and `rejectedSinceLastAccepted` as the host last sent them.
- The 2 and the 3 repeat the host's defaults, because no message carries them.

### State diagrams

The two diagrams are SVG with a fixed `viewBox`, so each scales with its panel as one piece, text included. The current state is marked for a screen reader as well as tinted, and each diagram has a title.

### Focus header

The type is the host's, as C# writes it. The numbers in a rule repeat the host's defaults, because no message carries them.

| Stage | Type | Rule |
| --- | --- | --- |
| 1 Edges | `IEdgeSource` | Every change of the receiver's output pin arrives as its new level and a time in microseconds. |
| 2 Pulses | `IStage<Edge, Pulse>` | A pulse is the time the carrier stays reduced. A pulse under 50 ms is dropped and a gap under 50 ms is bridged. |
| 3 Symbols | `IStage<Pulse, Symbol>` | A pulse from 100 ms is a 0, from 350 ms a 1, from 650 ms a marker, and outside 100 to 900 ms an error. |
| 4 Frame sync | `IStage<Symbol, FrameSyncEvent>` | Two markers a second apart start a frame. A marker or a bit in the wrong second, or a gap outside 0.5 to 1.5 s, loses sync. |
| 5 Decode | `IStage<Frame, DecodedFrame>` | Each field is the sum of the weights of its bits that are 1. |
| 6 Validate | `IStage<DecodedFrame, ValidationResult>` | A frame is accepted when it passes all ten checks. The last is that its time is the last accepted frame's plus the minutes passed since. |
| 7 Clock | `IStage<ValidationResult, ClockState>` | Two accepted frames in a row lock the clock. Once locked it holds through two rejected frames, and the third sends it back to acquiring. |

## Panel states

| Panel | No source | Running, no data yet | Fault |
| --- | --- | --- | --- |
| Signal | Empty lanes with labels and a hint to pick a source | Lanes scroll; flat carrier | None of its own |
| Pulse width | Axis, zones, and thresholds; no bars | Same | None of its own |
| Frame | Empty cells with weights and brackets | `Searching` in the header | The sync-lost reason for 5 s |
| Decoded frames | Column headings and `No frames yet` | Same | Rejected rows |
| Time | `--:--:--`, status `No signal` | Same | `Holding`, then `Acquiring` |

Each panel sits in its own error boundary. If a view throws, that panel shows a short message and the rest of the screen keeps running.

## Page structure

```text
bridge (or simulator)  ->  store  ->  React components   (rail, tables, clock, controls)
                              \---->  canvas views       (read buffers every animation frame)
```

- **Store.** One Zustand store. Each message type is handled by a pure reducer that is tested on its own. The store keeps ring buffers (2,000 edges, 300 pulses, 600 symbols, 120 filled slots, 20 validation results, 50 info lines, 5 sync losses), the latest message of each type for the rail, and the counters. A filled slot is a `slotFilled` event together with the start time of the symbol that arrived just before it, which is the symbol it placed. The signal panel's second lane and the frame panel both read that buffer.
- **The page's own state.** The store also holds what the page decides for itself: the focused stage, the open drawer, the app bar's picks with the reduced-carrier level, the warning the presenter closed, and the frame selected in the stage 6 table. They are outside the reducer, so a source reset leaves them alone, with one exception: it forgets the selected frame, because the frames are numbered from 1 again. Two more messages reach them: `selection` sets the picks and the level, and a `sourceStatus` with an empty `warning` forgets the warning that was closed.
- **Rendering.** Components subscribe to small slices, so a new edge batch does not re-render the tables. The canvas views do not go through React at all.
- **View models.** Each custom view has a pure function from buffers, size, and tokens to a list of things to draw, and a thin component that paints the list. Tests cover the function. The same split holds for a focus view's own panels: what a check list, a state diagram, or the last pulse's card shows is decided by a function of the store's state.
- **Simulator.** When `window.chrome.webview` is absent, a simulator bridge plays a recorded message stream at a chosen speed and answers commands with canned status. Its reply to `hello` ends with a `selection` of the defaults. The recording is taken from the real host, so the simulator cannot drift from the protocol.

### Source clock

Edges arrive in batches every 100 ms and only when the carrier changes, so the present has to be estimated between them. One module does this for every view:

- It maps `performance.now()` to source microseconds, anchored on the first edge and advancing at the source's speed (1 for live and synthetic, the replay speed for replay). The speed is the `speed` of the latest `sourceStatus`, so it changes when the host says it has, not when the page asks.
- Every later batch corrects it with the timestamp of its newest edge:
  - An edge later than the estimate proves the clock is behind. The whole difference is made up.
  - An edge earlier than the estimate is the normal case, because edges arrive late. A tenth of the difference is taken back. This keeps the clock with a stream that runs slower than its nominal speed, which a paced source can (see Step 13, Task 1 in the construction plan), and with a Pico whose crystal runs slower than the PC's.
- It never jumps and never runs backwards. A correction changes how fast the clock runs over the next 250 ms. An edge that arrives during a correction is compared with where the clock is heading, so the rest of the correction is kept.
- It keeps running when edges stop, so a flat carrier still scrolls.
- The page discards the anchor when the source resets or stops.
- At replay speed 0, as fast as possible, there is no usable present: the signal panel stops scrolling and shows a note.

The signal cursor and the clock digits read this module. The frame panel's cell being received does not: it follows the last second placed (see [Frame](#frame)).

### Sync loss

The host sends `searching` in the same push as `syncLost`, so the latest `frameSync` event shows a loss only for an instant. The store keeps the loss on its own:

- The reducer records the reason and the message's `seq` when `syncLost` arrives. A source reset clears it.
- The store clears it 5 s later by the wall clock. The 5 s is reading time, so it is the same at every replay speed.
- The timer clears only the loss it was started for, so a second loss inside the 5 s is shown for its own 5 s.

The rail's stage 4 health and the frame panel's header both read this one field.

The reducer also appends each loss to a list of the newest five, with a number that counts from 1 after every source reset. No timer touches the list. The stage 4 focus view reads it.

## Protocol additions

Each lands with its example file and both test suites, in the step that needs it.

**`frameLayout` (the frame panel, Step 13).** The frame layout is written down in exactly two places in code, `WwvbFrameLayout` and the Rust crate. A TypeScript copy would be a third, so the host sends it once, in its reply to `hello`. The payload is specified in [`webview2-messages.md`](webview2-messages.md#framelayout): a role and a bit weight for each of the 60 seconds.

- The page derives a field's bracket from the first and last second with its role.
- The message carries no marker names. The page names the marker seconds in order: `Pr`, `P1` to `P5`, `P0`.
- The store keeps the layout across a source reset, because the host does not send it again.

Adding a message type does not change an existing one, so the protocol number stays 1.

**`selection` and the `warning` of `sourceStatus` (Step 15).** The host remembers the page's choices in a settings file and starts the remembered source at launch. `selection` tells a page that has just loaded what they are, and `warning` carries what the banner shows. Both are specified in [`webview2-messages.md`](webview2-messages.md#selection), with the file and the launch rule under [What the host remembers](webview2-messages.md#what-the-host-remembers). `warning` is a new field of an existing message, which either side may add: a page without it shows no banner, and a host without it sends none.

**Pulse statistics (not added).** `PulseMeasurement` counts `GlitchesDropped` and `GapsBridged`, but no message carries them, and Step 16 left it that way. The stage 2 focus view marks each dropped glitch and each bridged gap where it happened instead, which the page works out from the edges and the pulses it already receives (see [Stage 2: pulses](#stage-2-pulses)). A running count would need a message of its own, with its example file and both test suites.

## Keyboard

Keys are left to the control while a text field or slider has focus, and while a list or the signal settings are open.

| Key | Action |
| --- | --- |
| `1` to `7` | Focus that stage |
| `Esc` or `0` | Back to the overview |
| Left and right arrows, Page Up, Page Down | Previous and next stage, so a presenter remote works |
| `T` | Switch theme |
| `L` | Protocol log |
| `?` | Shortcut list |
| `D` | The noise demo preset, on and off |
| `F11` | Full screen, on and off. The host's window takes this key, not the page |

- The arrow and page keys walk a row of eight places: the overview, then stages 1 to 7. They stop at both ends.
- Pressing the number of the stage in focus changes nothing. Only the rail's button toggles.
- A key pressed with Ctrl, Alt, or the Windows key is left to the browser, and a key that is held down acts once.
- The protocol log and the shortcut list are drawers, and one is open at a time: `L` and `?` each open theirs and close it again, and opening one closes the other. The app bar's keyboard button opens the shortcut list too.
- While a drawer is open no other key acts on the page. `Esc` closes the drawer and leaves the view behind it as it was.
- An open list and the open signal settings have the keyboard too, from the moment they open. No key acts on the page: letters and digits search the list, the arrows move in it or between High and Low, and `Esc` closes the control and leaves the view as it was. This holds for a list with nothing to pick as well.
- A closed list and the signal settings button are buttons like any other. A stage key pressed on one acts on the page, and the control stays closed.
- A key the page acts on goes no further. An arrow key pressed on a source or speed button changes the stage and does not also move the focus along the buttons.
- When a key acts, the control that has focus gives it up. A button clicked with the mouse keeps focus, and the browser draws its focus ring on the next key press, so a stage key would light up the Start button. Tab carries on from the control that had focus.

Three things were added in Step 15, for the presenter:

- **`D`, the noise demo.** On is the synthetic source started at noise 0.10, where frames still complete and stage 6 rejects some of them. `D` picks Synthetic and that noise in the app bar and sends `selectSource`, also when the synthetic source is the one running. The reason is a measurement of 2026-11-22: the source draws the same noise on every start, so a new start rejects its first frame after one minute, every time, while noise turned up on a source that already ran gave a rejected row within five minutes in 8 of 60 tries. Pressed while the synthetic source runs at 0.10, to within half a step of the slider, `D` sends `setNoise` with 0, and the source runs on without its noise. So one press shows a rejection and the next shows the clock recovering. Like `T`, it does nothing while a drawer is open.
- **`F11`, full screen.** The window loses its title bar and its border and covers the monitor it is on, the taskbar too. `F11` again gives the window back as it was, maximized or not. The page does not see the key. In a browser, where the page runs on the simulator, `F11` is the browser's own full screen.
- **A desktop app, not a browser.** The host gives the page the keyboard when the page has loaded, so the keys work with no click on the page first. In a Release build of the host, `F5` and `Ctrl+R` do not load the page again, `F12` opens no tools, and the right mouse button opens no menu. A Debug build keeps all of them, for work on the page.

## Motion

Motion explains a change; it is never decoration.

| What | How |
| --- | --- |
| Hover and press | 120 ms |
| Overview to focus and back, and one stage to the next | 200 ms, with the View Transitions API. A panel that both views show moves and grows to its new place, and the rest cross-fades |
| A frame completing | The rail's buttons for stages 4 to 7 light in order, 80 ms apart, each for 500 ms, as the frame's row slides into the decoded-frames table in 200 ms |
| The last pulse, in the stage 3 focus view | Its line drops into place in 200 ms |
| A state diagram | The state it is in takes its color over 200 ms |
| Signal scrolling | Continuous, driven by the source clock |

- **The view transition** is started by the key or the click that changes the view. The browser takes a picture, React renders the new view inside the API's callback, and the browser animates from the one to the other. Asking for the view that is on show starts none.
- Each panel's `view-transition-name` is made from its title, `panel-frame`. No two panels on one screen share a title, which the API requires.
- A second change inside the 200 ms skips the first transition and starts its own, so a remote pressed twice in a row lands on the second view.
- **A row slides in** only when its frame arrives while the table is on show. The rows that are there when the table appears are drawn in place.
- **Reduced motion.** With `prefers-reduced-motion`, no view transition is started, the rail's lights are not drawn, and one rule in `index.css` cuts every animation and transition on the page to nothing, the hover and press included. The signal still scrolls, because that is the data and not an effect.
- Where a browser has no View Transitions API the view changes at once. WebView2 has it.
