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

| Stage | Message | Readout | Health |
| --- | --- | --- | --- |
| 1 Edges | `edges` | The last edge as the serial line writes it, `L 2617209411` | Good while edges arrive; bad when `sourceStatus` is `error` |
| 2 Pulses | `pulse` | Last duration, `502 ms` | Good |
| 3 Symbols | `symbol` | Last kind, `one` | Warn if any of the last 60 symbols is `error` |
| 4 Frame sync | `frameSync` | `37 of 60`, or `searching` | Good when synced; warn while searching; bad for 5 s after `syncLost` |
| 5 Decode | `decodedFrame` | `19:32 day 344` | Warn if `errorSeconds` is not empty |
| 6 Validate | `validation` | `accepted` or `rejected` | Good or bad |
| 7 Clock | `clock` | The status | Good when `locked`; warn when `acquiring` or `holding` |

Before a source starts, every readout is a dash and every dot is `text-3`.

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

- The newest second is drawn up to a cursor at the estimated present (see [Source clock](#source-clock)). Stage 2 holds a pulse until the next edge confirms it, so the newest pulse and symbol read `pending`.
- A `slotFilled` event labels the symbol that arrived just before it.
- The window is 12 s at 1× speed. Above 1× it is 60 s and the pulse and second labels are hidden, because they would be unreadable.
- Drawn on a canvas, once per animation frame, straight from the store's buffers.

### Pulse width

A histogram of the last 600 symbol durations from 0 to 1,000 ms.

- Bins are 25 ms wide, so the class boundaries at 100, 350, 650, and 900 ms fall on bin edges.
- Bars are colored by class: zero dim accent, one accent, marker neutral, error `bad`.
- The two decision thresholds are solid lines; the valid range limits are dashed, with the error zones tinted.
- A dashed line marks the last pulse, and the header line reads `Last pulse 502 ms classified as 1`.

### Frame

The 60 seconds of the frame in progress, in three rows of 20. Every BCD field stays inside one row, and the position markers line up in the 10th and 20th columns.

- A cell shows the symbol placed by `slotFilled`. Cell states: empty, empty marker slot (stronger border), `0` in a field, `0` in an unused second (no fill), `1`, `M`, error, and the cell being received now (accent border).
- Above each cell: its bit weight, or the marker's name (`Pr`, `P1` to `P5`, `P0`). Below each row: a bracket under each field with its name.
- The overview shows bits only. Values appear in the decoded-frames table, because the host decodes only a complete frame.
- A `slotFilled` with a new `frameStartMicros` clears the cells first.
- Header line: `Synced, 37 of 60 symbols` with a progress bar; `Searching, 1 of 2 markers`; or, for 5 s after `syncLost`, the host's reason.
- Which seconds belong to which field comes from the host (see [Protocol additions](#protocol-additions)).

### Decoded frames

One row per `validation` message, newest first. Three rows in the overview; the store keeps 20.

| Column | Content |
| --- | --- |
| Frame | A count since the last source reset, `#41` |
| Symbols 0 to 59 | Sixty ticks: tall accent for a 1, short and dim for a 0, tall neutral for a marker, `bad` for an error |
| Min, Hour, Day, Year | The decoded values as received, with no correction |
| UT1 | Sign and tenths, `+0.0 s` |
| DST | `standard`, `dst`, `begins today`, `ends today` |
| Result | `Accepted`, or `Rejected: ` and the first failure's check. A rejected row has a `bad` tint |

The full `detail` of every failure shows in a tooltip and in the stage 6 focus view.

### Time

- The clock digits are `frameUtc` plus the source time elapsed since `frameStartMicros`, so replay at 10× runs the clock at 10×. Before the first accepted frame they read `--:--:--`.
- Under the digits: the date and day of year.
- The four statuses in a row, with the current one highlighted in its health color.
- Four rows: last accepted frame; accepted in a row, or rejected since then out of 3; offset from the PC clock; local time, US Central.
- Under replay the offset row reads `Recorded offset` and is computed from the most recent `# wall` line received as `info`. It shows a dash until one arrives.
- Local time uses the `dst` field. On a `beginsToday` or `endsToday` day it is marked approximate, because the bits do not carry the 02:00 switch.

### App bar

Name; a three-way source control (Live, Replay, Synthetic); the selected source's own controls; Stop; and three icon buttons for signal settings, theme, and keyboard shortcuts.

| Source | Controls | Commands |
| --- | --- | --- |
| Live | Port list, refreshed on open | `selectSource` serial, `listPorts` |
| Replay | Capture list with date and length; speed 1×, 10×, 60× | `selectSource` replay, `setReplaySpeed`, `listCaptures` |
| Synthetic | Noise slider, 0 to 1 | `selectSource` synthetic, `setNoise` |

Signal settings holds the reduced-carrier level (`setReducedCarrierLevel`).

### Status bar and protocol log

The status bar shows the run state from `sourceStatus`, the source's name and detail, the last `seq` with a count of gaps, and the key hints. The protocol log from Step 8 becomes a drawer opened with `L`. It is a development tool and is closed on stage.

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

In stage 5 the page lists which weights are set; the sum it shows is the host's value.

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

- **Store.** One Zustand store. Each message type is handled by a pure reducer that is tested on its own. The store keeps ring buffers (2,000 edges, 300 pulses, 600 symbols, 20 validation results, 50 info lines), the latest message of each type for the rail, and the counters.
- **Rendering.** Components subscribe to small slices, so a new edge batch does not re-render the tables. The canvas views do not go through React at all.
- **View models.** Each custom view has a pure function from buffers, size, and tokens to a list of things to draw, and a thin component that paints the list. Tests cover the function.
- **Simulator.** When `window.chrome.webview` is absent, a simulator bridge plays a recorded message stream at a chosen speed and answers commands with canned status. The recording is taken from the real host, so the simulator cannot drift from the protocol.

### Source clock

Edges arrive in batches every 100 ms and only when the carrier changes, so the present has to be estimated between them. One module does this for every view:

- It maps `performance.now()` to source microseconds, anchored on the first edge and advancing at the source's speed (1 for live and synthetic, the replay speed for replay).
- Every later batch corrects it with the timestamp of its newest edge:
  - An edge later than the estimate proves the clock is behind. The whole difference is made up.
  - An edge earlier than the estimate is the normal case, because edges arrive late. A tenth of the difference is taken back. This keeps the clock with a stream that runs slower than its nominal speed, which a paced source can (see Step 13, Task 1 in the construction plan), and with a Pico whose crystal runs slower than the PC's.
- It never jumps and never runs backwards. A correction changes how fast the clock runs over the next 250 ms. An edge that arrives during a correction is compared with where the clock is heading, so the rest of the correction is kept.
- It keeps running when edges stop, so a flat carrier still scrolls.
- The page discards the anchor when the source resets or stops.
- At replay speed 0, as fast as possible, there is no usable present: the signal panel stops scrolling and shows a note.

The signal cursor, the cell being received, and the clock digits all read this module.

## Protocol additions

Neither exists yet. Each lands with its example file and both test suites, in the step that needs it.

**`frameLayout` (needed by the frame panel, Step 13).** The frame layout is written down in exactly two places in code, `WwvbFrameLayout` and the Rust crate. A TypeScript copy would be a third, so the host sends it once after `hello`. Proposed shape:

```ts
type SecondRole =
  | "marker" | "unused" | "minute" | "hour" | "dayOfYear" | "ut1Sign"
  | "ut1Correction" | "year" | "leapYear" | "leapSecondPending" | "dst";

interface FrameLayout {
  roles: SecondRole[];          // 60, indexed by second
  weights: (number | null)[];   // 60; the bit weight as the layout table writes it, or null
}
```

The page derives a field's bracket from the first and last second with its role. Adding a message type does not change an existing one, so the protocol number stays 1.

**Pulse statistics (optional, Step 16).** `PulseMeasurement` counts `GlitchesDropped` and `GapsBridged`, but no message carries them. The stage 2 focus view can show them only if a message does. Until then the page shows the dips in the carrier lane and no count.

## Keyboard

Keys are ignored while a text field or slider has focus.

| Key | Action |
| --- | --- |
| `1` to `7` | Focus that stage |
| `Esc` or `0` | Back to the overview |
| Left and right arrows, Page Up, Page Down | Previous and next stage, so a presenter remote works |
| `T` | Switch theme |
| `L` | Protocol log |
| `?` | Shortcut list |
| `F11`, `D` | Full screen and the noise demo preset, both added in Step 15 |

## Motion

Motion explains a change; it is never decoration. With `prefers-reduced-motion`, everything below is instant.

| What | How |
| --- | --- |
| Hover and press | 120 ms |
| Overview to focus and back | 200 ms, with the View Transitions API |
| A frame completing | The rail entries for stages 4 to 7 light in order, 80 ms apart, as the new table row slides in (Step 16) |
| Signal scrolling | Continuous, driven by the source clock |
