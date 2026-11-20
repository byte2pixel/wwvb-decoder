# ui

React + TypeScript, built with Vite. The host loads it inside WebView2. Opened in a plain browser, a development build runs on the [simulator](#simulator) instead, so the page can be worked on without the host.

The layout and visual language are specified in [`docs/design/ui-design.md`](../docs/design/ui-design.md), and the libraries are decided in [ADR-0007](../docs/design/adr/0007-tailwind-shadcn-hand-drawn-views.md).

As of construction plan Step 12b the page shows the signal. The Signal panel draws four lanes on one time axis, scrolling on the source clock, and the Pulse width panel draws the histogram. A key from `1` to `7`, or a click on the rail, opens that stage's focus view. Step 13 fills the Frame, Decoded frames, and Time panels, which show their no-data states until then, and enables Live and Replay.

```powershell
npm install
npm run dev             # dev server with hot reload; the Debug host prefers it when it is running
npm test                # vitest, once, with coverage into coverage/; thresholds are in vite.config.ts
npm run lint            # oxlint, warnings are errors; the pre-commit hook runs it on staged files
npm run format          # prettier --write; the pre-commit hook does this for staged files
npm run format:check    # prettier --check
npm run build           # type-check every project, then static files in dist/ for the host to serve
```

CI runs `format:check`, `lint`, `test`, and `build`; `../verify.ps1 -Only ui` runs the same steps locally.

## Stack

| Layer | Choice | Where |
| --- | --- | --- |
| Styling | Tailwind CSS v4 through `@tailwindcss/vite` | Tokens and the theme in `src/index.css`; there is no Tailwind config file |
| Controls | shadcn/ui, style `base-nova`, on Base UI primitives | Copied into `src/components/ui/`; the CLI's settings are in `components.json` |
| State | Zustand over pure reducers | `src/state/` |
| Messages | zod schemas | `src/protocol/` |
| Fonts | Inter and JetBrains Mono, variable, from Fontsource | Imported in `src/index.css`. Vite copies the font files into `dist/`, so nothing loads from the network |
| Icons | `lucide-react` | |
| Tests | Vitest, Testing Library, jsdom | Beside the code, as `*.test.ts` and `*.test.tsx` |
| Lint and format | oxlint, Prettier | `.oxlintrc.json`, `.prettierrc.json` |

## Source layout

| Folder | Holds |
| --- | --- |
| `protocol/` | The page's half of the message contract: schemas, types, and the bridge to the host |
| `state/` | `AppState`, its reducer, and the store |
| `time/` | The source clock, and the feed that keeps it on the stream's time |
| `sim/` | The simulator, the recorder, and their fixtures: the recorded stream and a copy of the `frameLayout` example. Development builds only |
| `shell/` | The frame of the screen: app bar, pipeline rail, panels, focus layout, keyboard map, status bar, protocol log |
| `views/` | What the panels show, one folder per view: the signal lanes, the pulse-width histogram, the edge log |
| `components/ui/` | Controls written by the shadcn CLI. They are linted and formatted like the rest, and left out of coverage |
| `lib/` | `cn`, which the generated controls import |

`main.tsx` picks the bridge, creates the source clock, subscribes the clock's feed to the bridge, and renders `App`. `App.tsx` subscribes the store to the bridge, sends `hello`, runs the keyboard map, and lays out the overview or one stage's focus view.

## Protocol

The page talks to the host over WebView2 web messages, as [`docs/design/webview2-messages.md`](../docs/design/webview2-messages.md) specifies. `src/protocol/` is the page's half of that contract.

| File | Holds |
| --- | --- |
| `messages.ts` | zod schemas for every host envelope and page command, and the types inferred from them. The schemas are the source of truth; no type is written by hand. `hostMessageSchema` is a discriminated union on `type`, `commandSchema` on `command`, with the three `selectSource` shapes nested on `source`. |
| `bridge.ts` | `createBridge(webview)` wraps `window.chrome.webview`: `onMessage` validates each event with `safeParse` and drops, with a `console.warn`, anything that fails; `send` posts a command object. `findWebView()` returns the real one, or `undefined` in a plain browser. `Bridge` is the interface the page depends on; the simulator implements it too. |
| `edgeLine.ts` | `edgeLine(edge)` writes an edge as the serial protocol does, `H 12345678`. The rail's stage 1 readout and the edge log both call it. |
| `*.test.ts` | `messages.test.ts` parses every file in `docs/design/message-examples/` through the schema its file name selects, so the host's serializer tests and these tests assert against the same files. The bridge is tested with a fake webview object, no DOM. |

## State

`state/appState.ts` holds `AppState` and `reduce(state, message)`, a pure function with one case per message type.

| Fields | Keep |
| --- | --- |
| `edges`, `pulses`, `symbols`, `validations`, `infoLines` | The newest 2,000, 300, 600, 20, and 50 |
| `clock`, `decodedFrame`, `frameSync`, `sourceStatus`, `ports`, `captures`, `frameLayout` | The latest message of that type |
| `frameCount`, `lastSeq`, `lostMessages` | Counters. `frameCount` counts `validation` messages; `lostMessages` adds up the gaps in `seq` |
| `slots` | The newest 120 `slotFilled` events, each with the start of the symbol it placed: the symbol that arrived just before it |
| `syncLoss` | The latest loss of frame sync, held for 5 s (see "Sync loss" in `ui-design.md`) |
| `log` | The protocol log: the last 200 messages and a count per type |

A `sourceStatus` whose `detail` starts with `reset:` is a source reset. The reducer goes back to `initialState` and keeps `ports`, `captures`, `frameLayout`, the sequence tracking, and the log. The host sends the layout only in its reply to `hello`, so a reset that dropped it would leave the frame panel without one.

`state/store.ts` wraps the reducer in one Zustand store, `useAppStore`. `dispatch(message)` runs the reducer, and starts the 5 s timer when the message is a `syncLost`.

The store also holds what the page decides for itself, `UiState`, beside the reducer's state and not in it:

| Field | Holds | Set by |
| --- | --- | --- |
| `focus` | The stage whose focus view is open, 1 to 7, or `null` for the overview | `setFocus` |
| `drawer` | The open drawer, `"log"` or `"shortcuts"`, or `null` | `setDrawer` |

No host message changes these, and a source reset leaves them alone: the reducer never returns them, and `set` merges what it does return.

One more value is the page's own and is kept the same way: `reducedLevel`, the pin level that means reduced carrier, `"high"` until `setReducedLevel` says otherwise. The signal settings set it and the signal view reads it. It sits beside `UiState` and not in it, because `UiState` is what the keyboard map takes.

- A component subscribes to the slice it shows: `useAppStore((state) => state.lastSeq)`.
- Zustand compares a selector's result by identity. A selector that builds a new object on every call goes through `useShallow`, as the rail's buttons do.
- A test puts the store back in `beforeEach`. `useAppStore.setState(initialState)` resets the reducer's state, and merges, so `focus`, `drawer`, and `reducedLevel` keep their values. A test file that touches any of those replaces the whole store: `useAppStore.setState(useAppStore.getInitialState(), true)`.

## Time

Nothing in a message says what time it is now, only when each edge happened. `time/sourceClock.ts` estimates the present in the source's microseconds, so the signal panel can scroll between messages. The rules are under "Source clock" in `ui-design.md`.

| File | Holds |
| --- | --- |
| `sourceClock.ts` | `createSourceClock(wallMs)` returns a `SourceClock`: `now()`, `sync(micros)`, `setSpeed(speed)`, `speed()`, and `reset()`. `slewMs` and `pullBack` at the top are the two tuning values |
| `clockFeed.ts` | `feedClock(clock)` returns a function of one message. An `edges` message syncs the clock to its newest edge. A `sourceStatus` sets the clock's speed from its `speed`, 1 when it has none; a source reset, or a status that is not `running`, also resets the clock |

`main.tsx` creates the one clock and hands it to `App`. Nothing on the page chooses the clock's speed. The host says it in every `sourceStatus`: the replay speed while a replay runs, 1 otherwise. The Signal panel's `12 s window` or `60 s window` line reads the same field from the store.

## Shell

| File | Holds |
| --- | --- |
| `stages.ts` | One pure function per stage, from `AppState` to what the rail shows (`readout` and `health`), and the `stages` table the rail maps over. The rules are the rail table in `ui-design.md`. Each stage also carries the host type and the one-sentence rule that its focus view's header shows |
| `PipelineRail.tsx` | The seven stage buttons. Each subscribes to its own stage's function, so a new edge re-renders one button. Health is a glyph with a hidden word, not a color alone. A click focuses the stage, the focused stage's button is pressed (`aria-pressed`), and a click on it goes back to the overview |
| `Panel.tsx` | A titled region with a chip per stage, an optional line of state, and an error boundary around its children: a view that throws shows a message in its own panel and the rest of the screen keeps running |
| `panels.tsx` | The screen's panels, each a view inside its `Panel`: `SignalPanel`, `PulseWidthPanel`, `EdgeLogPanel`, `FramePanel`, `DecodedFramesPanel` (three rows unless `rows` says otherwise; a focus view asks for 16, which is what fits a 16:9 window), and `TimePanel`, which takes the source clock. The overview and the focus views lay out the same components |
| `FocusLayout.tsx` | One stage's focus view: a header with the stage's number, name, host type, and rule, over that stage's panels |
| `keyboard.ts` | `keyAction(key, { focus, drawer })`: the keyboard map as a pure function. It returns what the key does, or `null`. The table is under "Keyboard" in `ui-design.md` |
| `useKeyboard.ts` | Runs the map for the whole page from one `keydown` listener on `window`. It stands back for a key pressed in an `input`, `textarea`, or `select`, in an open list or the open signal settings, on the button of one that is open, for Ctrl, Alt, and the Windows key, and for a key that is held down. A key it acts on goes no further, so the control with focus does not act on it too |
| `theme.ts` | `toggleTheme()`, for the app bar's button and the `T` key |
| `AppBar.tsx` | The name, the source control with each source's own controls (the port list, the capture list and speed buttons, the noise slider), Start, Stop, and the buttons for the signal settings, the theme, and the shortcut list. `PickList`, `SpeedButtons`, and `SignalSettings` are components local to the file. The rules are under "App bar" in `ui-design.md` |
| `captures.ts` | `captureLabel(entry)`: what the capture list shows for one capture, built from `formatRecorded` and `formatLength` |
| `StatusBar.tsx`, `ProtocolLog.tsx` | The run state, the source's name and detail, the last `seq`, the lost count, the key hints, and the Log button. The protocol log is a sheet that opens when the store's `drawer` is `"log"` |
| `ShortcutList.tsx` | The shortcut list, a sheet that opens when `drawer` is `"shortcuts"`. Its rows are written out by hand, so a key added to the map is added here too |

## Views

A view is split as ADR-0007 asks: a pure function decides what to show and is tested with plain data, and the component that puts it on the screen is thin. Each view reads its own slice of the store. What each one shows is specified under "Panels" and "Focus views" in `ui-design.md`.

**`views/signal/`**, the four lanes on one time axis, on a canvas:

| File | Holds |
| --- | --- |
| `lanes.ts` | `signalLanes(input)`: what is on each lane, in source microseconds, from the store's buffers, the present, and the window. No pixels |
| `model.ts` | `drawSignal(lanes, view)`: the same lanes as a `SignalDrawing` of lines, rectangles, and text in CSS pixels, for a canvas of a given size. The drawing's proportions are the constants at the top, each a multiple of the root font size |
| `paint.ts` | `paintSignal(ctx, drawing, fontFamily)`: paints a drawing on a 2D context, and decides nothing |
| `style.ts` | `readSignalStyle(element)`: the colors from the CSS variables, the root font size, and the font family, read from the page. A canvas cannot use a Tailwind class for a color |
| `timeWindow.ts` | `timeWindowFor(speed)`: 12 s with labels at 1× and below, 60 s without above |
| `SignalView.tsx` | The canvas and the lane names beside it. One effect holds the animation loop, a `ResizeObserver` that sizes the bitmap in device pixels, reads the style again, and paints at once, because sizing a bitmap clears it, and a `MutationObserver` that reads the style again when the theme changes. `show` picks the lanes; pass it a constant, because a new array restarts the effect |
| `recordingContext.ts` | A stand-in for a canvas context, for tests. It records each stroke, fill, text, clear, and transform with the state it was painted in |

**`views/pulse-width/`**, the histogram, in DOM:

| File | Holds |
| --- | --- |
| `model.ts` | `pulseWidthHistogram(symbols)`: 40 bins of 25 ms, each with a count, a height against the fullest bin, and the host's class of its newest pulse. The page does not classify a duration. `lastPulseText(last)` writes the panel's state line |
| `PulseWidthView.tsx` | The bars, the four boundaries, the error zones, and the last-pulse line. Every position is a percentage of the plot, so the view measures nothing. The four boundaries repeat the defaults of the host's `SymbolClassifier`, because no message carries them |

**`views/frame/`**, the frame in progress as three rows of twenty seconds, in DOM:

| File | Holds |
| --- | --- |
| `model.ts` | Pure functions, each from one slice of the store. `frameCells(layout)`: the 60 seconds with their role and the weight or marker name above each. `fieldBrackets(layout)`: each field's first and last second and its name. `placedSymbols(slots)`: the symbol in each second of the newest frame, which is how a new frame clears the cells. `receivingSecond(frameSync)`: the second after the one just placed. `frameHeader(state)`: the header line's text and the share of the frame placed. `cellState(role, symbol)` names how a cell is drawn |
| `FrameView.tsx` | `FrameView` puts them together: a list of cells per row over a list of brackets, each bracket placed by a percentage. `CELL_CLASSES` maps a cell state to its classes. `FrameHeaderLine` is the panel's state line, with a `<progress>` bar while symbols are being placed |

The frame view sizes its type with `clamp(small, n cqh, large)` inside a container of type `size` (`@container-size`), so it follows the panel's height. A width query such as `@2xl:` is wrong here: the overview's frame panel is wide and short, and it would get the focus view's sizes with no height to hold them.

**`views/frames-table/`**, one row per validated frame, in DOM:

| File | Holds |
| --- | --- |
| `model.ts` | `frameRows(validations, frameCount)`: the rows, newest first, each with its number since the source reset, its 60 symbols, the decoded values, and the result in words. `failureLine(failure)` writes one failure as its check in words and the host's detail. Three `Record` tables hold the words for a UT1 sign, a DST status, and a check, so a value the protocol gains later does not compile until it has its text |
| `FramesTable.tsx` | A `<table>` with fixed column widths in percent, the strip of 60 ticks, a tint on a rejected row, and every failure in the result's tooltip. `maxRows` is how many rows it shows; when they do not fit, the table scrolls under its headings |

**`views/time/`**, the clock the decoder has built, in DOM:

| File | Holds |
| --- | --- |
| `model.ts` | `decodedTimeMs(clock, atMicros)`: the last accepted frame's time plus the source time since that frame began. `clockFace` cuts it to the second as digits, date, and day of year. `statusChips` lists the four statuses with the current one marked. `lockRows`, `offsetRow`, and `localTimeRow` are the four rows; under replay the offset comes from the capture's newest `# wall` line |
| `TimeView.tsx` | `TimeView` reads the source clock on every animation frame and renders when the decoded second changes, so it ticks once a second at any replay speed. One font size from the panel's width and height (`min(19cqw, 22cqh)`) scales the rest, which is sized in `em`. `ClockStatusLine` is the panel's state line |

**`views/edge-log/`**: `EdgeLog.tsx` lists the newest 50 edges, newest first, as `edgeLine` writes them.

## Styling

- **Tokens.** `src/index.css` declares them as CSS variables, light values in `:root` and dark values under `.dark`. The names follow the "Variable names" table in `ui-design.md`. The `@theme inline` block maps each one into Tailwind (`--color-signal: var(--signal)`), which is what makes `bg-signal` and `text-good` exist. A new token needs a value per theme and a line in that block.
- **Theme.** `index.html` puts `class="dark"` on `<html>`, and the theme button toggles the class.
- **Imported styles.** `index.css` also imports `tw-animate-css` and `shadcn/tailwind.css`, which the generated controls rely on. That import is why `shadcn` is a dependency of the build and not only a command.
- **Scale.** The root font size is `min(100vw / 80, 100vh / 45)`: 16 px at 1280 by 720 and 24 px at 1920 by 1080, so the page scales as one piece. Every size is in `rem`. An arbitrary value is the size at the baseline divided by 16, so 13 px is `text-[0.8125rem]`. Borders stay 1 px.

## Simulator

When a development build finds no `window.chrome.webview`, `main.tsx` loads `sim/simulatorBridge.ts` by dynamic import and uses it as the bridge. `npm run dev` and http://localhost:5173 in a browser is all it takes.

It plays `sim/fixtures/synthetic-noise-0.jsonl`, a recording of the host's synthetic source at noise 0: 1,141 messages over 225 s of signal, three accepted frames, and a clock that is `acquiring` after the first frame and `locked` after the second.

| Command | The simulator's answer |
| --- | --- |
| `hello` | `sourceStatus` stopped, empty `ports` and `captures`, then `frameLayout` from `sim/fixtures/frameLayout.json` |
| `selectSource` | Plays the recording from the top, whichever source and noise were asked for |
| `stop` | Stops playing and sends `sourceStatus` stopped |
| Anything else | Nothing |

- Each message is sent at the time it arrived when it was recorded, so the stream has the host's real timing, late edges included.
- One second after the last message the recording starts again. It opens with the host's `reset: start` status, so the page clears at the top of every pass.
- `seq` is renumbered from 1 as messages go out, so the count never shows a gap.
- The recording was made at noise 0 with High as the reduced-carrier level, and no command changes it. The noise slider, the speed buttons, and the signal settings still move on the page, but the stream does not follow them, and the port and capture lists are empty. With the level set to Low the page reads the carrier the other way while the recorded pulses stay as they were, so the signal panel draws a pending bar over every stretch the recording did not measure. That is the simulator, not a fault. Check these controls in the host.
- `?speed=10` on the address plays ten times faster: http://localhost:5173/?speed=10. Any positive number works, fractions too; anything else means 1. Only the arrival times shrink. The timestamps inside the messages are untouched, so at 10 the source runs at ten times wall time. The simulator writes that speed into every `sourceStatus` it sends, as the host does for a replay, and that is how the page's clock learns it.

`sim/fixtures/frameLayout.json` is a copy of `docs/design/message-examples/frameLayout.json`, because the simulator has no host to build the layout for it. Two tests keep the copy true. In the host, `Serialize_FrameLayout_MatchesExample` holds the example equal to the layout table in C#. Here, the `hello` test in `sim/simulator.test.ts` holds the fixture equal to the example. When the layout changes in C#, change the example, copy it over the fixture, and run `npx prettier --write src/sim/fixtures/frameLayout.json`.

The simulator and the recorder sit behind `import.meta.env.DEV`, so `npm run build` leaves both out, and both fixtures with them. In the host, the only layout the page has is the one the host sends. A production build opened without the host gets a bridge that only logs a warning, and the status bar reads `no host`.

A development build runs under React's StrictMode, which mounts `App` twice. `hello` goes out twice and the log opens with two sets of answers. That is expected, in the host as well.

## Recording a fixture

The stream fixture is recorded from the real host, so it cannot drift from the protocol. In a development build `main.tsx` subscribes a recorder to the bridge and puts it on `window.wwvbRecorder`.

1. Run `npm run dev`, then start the host in Debug. It loads the page from the dev server, which is the build that has the recorder.
2. Open the dev tools in the host window (F12, or right-click and Inspect) and run `wwvbRecorder.start()` in the console.
3. Press Start on the page. Starting the recorder first puts the `reset: start` status at the top of the recording, which is what clears the page each time the simulator loops.
4. Let it run for at least three and a quarter minutes at noise 0. The clock locks on the second frame, and the third is accepted just after the three-minute mark.
5. Run `copy(wwvbRecorder.dump())` in the console, and paste the clipboard into a new `.jsonl` file under `src/sim/fixtures/`. Save it with LF line endings.
6. Point the import in `sim/simulatorBridge.ts` at the new file, and the fixture test in `sim/recording.test.ts` with it.

Each line is the envelope as the host sent it plus `t`, the milliseconds since `start()`. `parseRecording` checks every line against `hostMessageSchema` and names the first line that fails.

## Adding a shadcn component

```powershell
npx shadcn add <name>    # from ui/; runs the CLI that npm install put in node_modules
```

1. The CLI writes into `src/components/ui/`. If it asks to overwrite a file that is already there, answer no. It asks about `button.tsx` when the new component uses the button. Replacing a component that is already in use is its own change, with its own diff to read.
2. Check `package.json`: the CLI adds any package the component needs.
3. Run `npm run lint`. If a generated file breaks a rule, turn that one rule off in the override for `src/components/ui/**` in `.oxlintrc.json`. Do not edit the generated file to satisfy the linter, and do not turn the rule off for the rest of the code.
4. Coverage needs nothing: the folder is already excluded in `vite.config.ts` and in `sonar.coverage.exclusions` in `.github/workflows/ci.yml`.

The CLI's closing notes are written for Next.js (`app/layout.tsx`). The place for a provider here is `App.tsx`, where `TooltipProvider` already wraps the shell.

The controls are built on Base UI, and most examples online are for the Radix versions. Where they differ:

- `render={<Button />}` replaces `asChild`.
- `Slider` and `ToggleGroup` take `value` as an array, even for one value: `value={[noise]}`. A slider given a bare number draws two thumbs.
- A class passed from outside does not beat one the component sets under a variant: `w-32` loses to the slider's `data-horizontal:w-full`. Put the control in a sized wrapper.
- jsdom hides the slider's thumb. A test finds the real input with `screen.getByLabelText("Noise", { selector: "input" })` and changes it with `fireEvent.change`.
- A `Select` shows the raw value on its button unless its root gets `items` of `{ value, label }`. An empty list draws as a hairline, so `PickList` gives it one disabled row.
- A `Select` also reports changes it makes itself, with the reason `"none"`: it resets the value when the picked item leaves the list, and it picks by typeahead when a key is typed on the closed list. `PickList` takes only a pick the user made and leaves the rest to the page's own rule.
- A list opens a moment after the click, and the focus arrives inside it a moment after that. A test finds what is inside with `findByRole`, and before it types it waits for the focus: `await waitFor(() => expect(option).toHaveFocus())`. A list with nothing to pick puts the focus on its own box, not on a row.
- While a list is open the rest of the page is hidden from role queries, so the next control is found with `findByRole` too.

One generated file is changed by hand. In `toggle.tsx` a pressed toggle is `aria-pressed:bg-primary aria-pressed:text-primary-foreground` where the CLI wrote `aria-pressed:bg-muted`. `--muted` is also the popover's background, so a pressed button inside a popover could not be seen, and it was faint on the app bar. If the CLI ever writes the file again, put the edit back.

## Imports

- A local import carries no file extension: `import { reduce } from "./appState"`. A stylesheet, a JSON file, and the raw fixture keep theirs (`./index.css`, `./fixtures/frameLayout.json`, `./fixtures/synthetic-noise-0.jsonl?raw`).
- The same folder or below is relative (`./stages`). Another folder goes through the alias (`@/state/store`). Never `../`.

The linter enforces the first rule and not the second.

`@/` means `src/`. It is declared in three places, one per tool that resolves it:

| File | For |
| --- | --- |
| `tsconfig.app.json`, `paths` | The compiler. The test project inherits it |
| `vite.config.ts`, `resolve.alias` | The bundler and vitest |
| `tsconfig.json`, `paths` | The shadcn CLI only. It reads the root file and does not follow references; without the block its `init` fails with "Could not load the workspace config". The compiler ignores options in a solution file |

There is no `baseUrl`: TypeScript 7 rejects it (TS5102), and `paths` works without it.

## Lint

`.oxlintrc.json` turns on the `react`, `typescript`, `oxc`, `jsx-a11y`, and `import` plugins, with the correctness, suspicious, and perf categories as errors.

The editor underlines the `import/extensions` rule in that file with "Array has too many items". Leave it. The schema accepts either a plain map of extensions or a settings object with that map under `pattern`, and the file mixes the two: `css`, `json`, `jsonl`, and `checkTypeImports` side by side. In oxlint 1.90.0 the schema's own form, with the map under `pattern`, stops checking `import type` lines. The mixed form checks them.

After an oxlint upgrade it is worth one try: move the map under `pattern`, add `.ts` to an `import type` line, and run `npm run lint`. If the line is reported, keep the schema's form.

One rule is silenced on one line of the app's own code. A tick in `FramesTable.tsx` takes its array index as its React key, because a tick has no identity but its second and the seconds of a frame never move. The line carries `oxlint-disable-next-line react/no-array-index-key` with that reason, and `NOSONAR` for the same rule in Sonar. A new directive needs a reason as plain as that one, written beside it.

## Tests

`vite.config.ts` defines two vitest projects, and the file extension picks one:

| Project | Files | Environment |
| --- | --- | --- |
| `unit` | `*.test.ts` | Node |
| `dom` | `*.test.tsx` | jsdom, with `src/test-setup.ts` |

One jsdom environment for everything does not work: jsdom replaces the global `URL`, which `messages.test.ts` and `sim/simulator.test.ts` use to find the example files. So a test that needs a DOM is named `.test.tsx`, whether or not it contains JSX.

`test-setup.ts` adds the `jest-dom` matchers and unmounts after each test. Testing Library cannot register that cleanup itself, because there are no vitest globals: test files import `describe`, `it`, and `expect` from `vitest`.

jsdom lays nothing out and has no canvas, so `test-setup.ts` also gives every dom test a `ResizeObserver` that never reports and a `getContext` that returns `null`. A view that sizes itself or paints then renders and does nothing more. A test that needs sizes or painting stubs its own, as `SignalView.test.tsx` does: a fake observer the test drives with `reportSize(width, height)`, and the recording context from `views/signal/recordingContext.ts`.

Because jsdom lays nothing out, a test cannot see cells that collapse or text that overlaps. A view drawn in DOM is looked at in the app at both of its sizes, in the overview and in its focus view, before it is called done.

jsdom also shows focus on every focused element: it matches `:focus-visible` wherever `:focus` matches. What a mouse click leaves behind, focus without a focus ring, can only be seen in a browser.

Coverage leaves out the tests, `components/ui/`, `main.tsx`, and `test-setup.ts`.

## TypeScript projects

`tsconfig.json` is a solution file that references three projects:

| Project | Covers | Types |
| --- | --- | --- |
| `tsconfig.app.json` | `src/`, excluding `*.test.ts*` and `test-setup.ts` | `vite/client` |
| `tsconfig.test.json` | all of `src/`, including the tests | `node`, `vite/client` |
| `tsconfig.node.json` | `vite.config.ts` | |

The test project extends the app project and resets its `exclude` to `[]`. It includes all of `src`, not only the test files, so the editor's auto-import sees modules a test has not imported yet; the cost is that `tsc -b` type-checks the non-test sources twice.

TypeScript here is 7.x, the Go-based compiler. VS Code's bundled TypeScript cannot load it as a workspace version and falls back to its own, which is fine for editing; the build uses 7.x from the shell.
