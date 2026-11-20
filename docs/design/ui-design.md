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
| Signal (stages 1 to 4, lanes on one time axis) 186 | Pulse width (3) 186 |
+----------------------------------------------------+---------------------+
| Frame (stage 4): 3 rows of 20 cells            234 | Time (stage 7)      |
+----------------------------------------------------+                     |
| Decoded frames (stages 5 and 6): table       fills |               fills |
+----------------------------------------------------+---------------------+
| Status bar: run state, source, sequence, key hints                   24  |
+--------------------------------------------------------------------------+
```

The right column is 344 wide. Panels are 12 apart, with 12 of padding around the content area.

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
- Stage 2 reports a pulse only when a later edge confirms it, about a second after the pulse starts. Until then the pulse lane draws every reduced-carrier interval that starts after the last measured pulse as a bar that reads `pending`, growing to the cursor while the carrier is still reduced. A dip that stage 2 drops as a glitch is such a bar for about a second; after that it shows in the carrier lane only.
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

One row per `validation` message, newest first. Three rows in the overview, and the newest 16 in the focus views of stages 5 and 6; the store keeps 20. Sixteen is what a 16:9 window holds without cutting a row, and the page scales with the window, so that is true at 720p and at 1080p alike. In a window with less room the table scrolls under its headings.

| Column | Content |
| --- | --- |
| Frame | A count since the last source reset, `#41`. It keeps counting past the 20 frames the store holds |
| Symbols 0 to 59 | Sixty ticks: tall accent for a 1, short and dim for a 0, tall neutral for a marker, `bad` for an error. The strip's tooltip writes the symbols out: `M`, `0`, `1`, and `×` |
| Min, Hour, Day, Year | The decoded values as received, with no correction |
| UT1 | Sign and seconds, `+0.3 s` or `-0.7 s`. An invalid sign is written `?` |
| DST | `standard`, `dst`, `begins today`, `ends today` |
| Result | `Accepted`, or `Rejected: ` and the first failure's check in words, such as `error symbols` or `BCD digits`. A rejected row has a `bad` tint |

The result's tooltip lists every failure of a rejected frame, one to a line: the check in words, then the host's `detail`. The stage 6 focus view will show the same lines in the table.

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
- **While its source runs.** A speed button sends `setReplaySpeed` at once while a replay is running, and the noise slider sends `setNoise` on each step while the synthetic source is running. With another source running, or none, they only set what the next Start sends.
- **A capture row** reads `2026-11-09 04:34 UTC · 8 h 45 min`: when the capture was recorded, in UTC, and how long it runs.
- **An empty list** reads `No ports found` or `No captures found`, on its button and as its only row.
- **Signal settings** is a popover with the reduced-carrier level, High or Low. A pick is sent as `setReducedCarrierLevel` at once, whether or not a source runs, because the host keeps the level from one run to the next. The signal panel reads the carrier with the same level.
- **The pressed button** of the source, the speed, and the level is drawn in the accent color, so the choice can be read from the back of the room.

The host does not report the port, the capture, the noise, or the level, so a reloaded page shows its defaults: Synthetic, the first port, the newest capture, 1×, noise 0, and High. The clock still runs at the host's speed, because every `sourceStatus` carries it; the speed buttons do not follow it. Step 16 Task 9 holds what is left to polish here.

### Status bar and protocol log

The status bar shows the run state from `sourceStatus`, the source's name and detail, the last `seq` with a count of gaps, and the key hints: `1 to 7 stage · Esc overview · ? shortcuts`. The protocol log from Step 8 becomes a drawer opened with `L`. It is a development tool and is closed on stage.

## Focus views

Pressing `1` to `7` replaces the panels with one stage, sized to be read from the back of the room. The app bar, rail, and status bar stay, and the rail highlights the stage. Each focus view has the same header: the stage number, its name, its type (`IStage<Frame, DecodedFrame>`), and one sentence stating its rule.

| Stage | Baseline (Steps 12b and 13) | Full (Step 16) |
| --- | --- | --- |
| 1 Edges | The carrier lane enlarged, beside the last 50 edges as the serial protocol writes them | Informational lines too: the banner and `# dropped` |
| 2 Pulses | The carrier and pulse lanes enlarged | The drop and bridge rules drawn on a real glitch |
| 3 Symbols | The pulse-width panel enlarged, with the symbol lane | The last pulse landing in its class, as input, rule, output |
| 4 Frame sync | The frame panel enlarged, with the last sync-lost reasons | The searching and synced states as a diagram |
| 5 Decode | The last complete frame, with each field's value from `decodedFrame` | The weights of the set bits listed under each field, and the record handed to stage 6 |
| 6 Validate | The table at 20 rows with every failure's detail | The ten checks as a list for the selected frame |
| 7 Clock | The time panel enlarged | The four statuses as a state diagram with the counters |

In stage 1 the edges are listed newest first, as the protocol log lists its messages, each as the rail writes the last edge. The 50 lines run down three columns, filled one after the other. Before the first edge the list reads `No edges yet`.

In stage 5 the page lists which weights are set; the sum it shows is the host's value.

A baseline view is built from the overview's panels, each with its header and its error boundary. A signal panel that shows fewer lanes names those lanes and carries only their stage chips. Until Step 13 fills them, stages 4 to 7 show their overview panel in its no-data state, and stages 5 and 6 both show the decoded-frames panel.

### Focus header

The type is the host's, as C# writes it. The numbers in a rule repeat the host's defaults, because no message carries them.

| Stage | Type | Rule |
| --- | --- | --- |
| 1 Edges | `IEdgeSource` | Every change of the receiver's output pin arrives as its new level and a time in microseconds. |
| 2 Pulses | `IStage<Edge, Pulse>` | A pulse is the time the carrier stays reduced; a pulse under 50 ms is dropped and a gap under 50 ms is bridged. |
| 3 Symbols | `IStage<Pulse, Symbol>` | A pulse from 100 ms is a 0, from 350 ms a 1, from 650 ms a marker, and outside 100 to 900 ms an error. |
| 4 Frame sync | `IStage<Symbol, FrameSyncEvent>` | Two markers a second apart start a frame; a marker or a bit in the wrong second, or a gap outside 0.5 to 1.5 s, loses sync. |
| 5 Decode | `IStage<Frame, DecodedFrame>` | Each field is the sum of the weights of its bits that are 1. |
| 6 Validate | `IStage<DecodedFrame, ValidationResult>` | A frame is accepted when it passes all ten checks; the last is that its time is one minute after the frame before. |
| 7 Clock | `IStage<ValidationResult, ClockState>` | Two accepted frames in a row lock the clock; once locked it holds through two rejected frames, and the third sends it back to acquiring. |

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

- **Store.** One Zustand store. Each message type is handled by a pure reducer that is tested on its own. The store keeps ring buffers (2,000 edges, 300 pulses, 600 symbols, 120 filled slots, 20 validation results, 50 info lines), the latest message of each type for the rail, and the counters. A filled slot is a `slotFilled` event together with the start time of the symbol that arrived just before it, which is the symbol it placed. The signal panel's second lane and the frame panel both read that buffer.
- **The page's own state.** The store also holds what the page decides for itself: the focused stage and the open drawer. No message changes them, so they are outside the reducer, and a source reset leaves them alone.
- **Rendering.** Components subscribe to small slices, so a new edge batch does not re-render the tables. The canvas views do not go through React at all.
- **View models.** Each custom view has a pure function from buffers, size, and tokens to a list of things to draw, and a thin component that paints the list. Tests cover the function.
- **Simulator.** When `window.chrome.webview` is absent, a simulator bridge plays a recorded message stream at a chosen speed and answers commands with canned status. The recording is taken from the real host, so the simulator cannot drift from the protocol.

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

## Protocol additions

Each lands with its example file and both test suites, in the step that needs it.

**`frameLayout` (the frame panel, Step 13).** The frame layout is written down in exactly two places in code, `WwvbFrameLayout` and the Rust crate. A TypeScript copy would be a third, so the host sends it once, at the end of its reply to `hello`. The payload is specified in [`webview2-messages.md`](webview2-messages.md#framelayout): a role and a bit weight for each of the 60 seconds.

- The page derives a field's bracket from the first and last second with its role.
- The message carries no marker names. The page names the marker seconds in order: `Pr`, `P1` to `P5`, `P0`.
- The store keeps the layout across a source reset, because the host does not send it again.

Adding a message type does not change an existing one, so the protocol number stays 1.

**Pulse statistics (optional, Step 16).** `PulseMeasurement` counts `GlitchesDropped` and `GapsBridged`, but no message carries them. The stage 2 focus view can show them only if a message does. Until then the page shows the dips in the carrier lane and no count.

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
| `F11`, `D` | Full screen and the noise demo preset, both added in Step 15 |

- The arrow and page keys walk a row of eight places: the overview, then stages 1 to 7. They stop at both ends.
- Pressing the number of the stage in focus changes nothing. Only the rail's button toggles.
- A key pressed with Ctrl, Alt, or the Windows key is left to the browser, and a key that is held down acts once.
- The protocol log and the shortcut list are drawers, and one is open at a time: `L` and `?` each open theirs and close it again, and opening one closes the other. The app bar's keyboard button opens the shortcut list too.
- While a drawer is open no other key acts on the page. `Esc` closes the drawer and leaves the view behind it as it was.
- An open list and the open signal settings have the keyboard too, from the moment they open. No key acts on the page: letters and digits search the list, the arrows move in it or between High and Low, and `Esc` closes the control and leaves the view as it was. This holds for a list with nothing to pick as well.
- A closed list and the signal settings button are buttons like any other. A stage key pressed on one acts on the page, and the control stays closed.
- A key the page acts on goes no further. An arrow key pressed on a source or speed button changes the stage and does not also move the focus along the buttons.
- When a key acts, the control that has focus gives it up. A button clicked with the mouse keeps focus, and the browser draws its focus ring on the next key press, so a stage key would light up the Start button. Tab carries on from the control that had focus.

## Motion

Motion explains a change; it is never decoration. With `prefers-reduced-motion`, everything below is instant.

| What | How |
| --- | --- |
| Hover and press | 120 ms |
| Overview to focus and back | 200 ms, with the View Transitions API |
| A frame completing | The rail entries for stages 4 to 7 light in order, 80 ms apart, as the new table row slides in (Step 16) |
| Signal scrolling | Continuous, driven by the source clock |
