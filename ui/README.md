# ui

React + TypeScript, built with Vite. The host loads it inside WebView2. Opened in a plain browser, a development build runs on the [simulator](#simulator) instead, so the page can be worked on without the host.

The layout and visual language are specified in [`docs/design/ui-design.md`](../docs/design/ui-design.md), and the libraries are decided in [ADR-0007](../docs/design/adr/0007-tailwind-shadcn-hand-drawn-views.md).

As of construction plan Step 12a the page is the shell: app bar, pipeline rail with live readouts, five panels in their no-data states, status bar, and the protocol log in a drawer. Three things wait for Steps 12b and 13: the rail's buttons do nothing, nothing calls the source clock, and Live and Replay are disabled.

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
| `time/` | The source clock |
| `sim/` | The simulator, the recorder, and the recorded fixture. Development builds only |
| `shell/` | The frame of the screen: app bar, pipeline rail, panels, status bar, protocol log |
| `components/ui/` | Controls written by the shadcn CLI. They are linted and formatted like the rest, and left out of coverage |
| `lib/` | `cn`, which the generated controls import |

`main.tsx` picks the bridge and renders `App`. `App.tsx` subscribes the store to the bridge, sends `hello`, and lays out the shell.

## Protocol

The page talks to the host over WebView2 web messages, as [`docs/design/webview2-messages.md`](../docs/design/webview2-messages.md) specifies. `src/protocol/` is the page's half of that contract.

| File | Holds |
| --- | --- |
| `messages.ts` | zod schemas for every host envelope and page command, and the types inferred from them. The schemas are the source of truth; no type is written by hand. `hostMessageSchema` is a discriminated union on `type`, `commandSchema` on `command`, with the three `selectSource` shapes nested on `source`. |
| `bridge.ts` | `createBridge(webview)` wraps `window.chrome.webview`: `onMessage` validates each event with `safeParse` and drops, with a `console.warn`, anything that fails; `send` posts a command object. `findWebView()` returns the real one, or `undefined` in a plain browser. `Bridge` is the interface the page depends on; the simulator implements it too. |
| `*.test.ts` | `messages.test.ts` parses every file in `docs/design/message-examples/` through the schema its file name selects, so the host's serializer tests and these tests assert against the same files. The bridge is tested with a fake webview object, no DOM. |

## State

`state/appState.ts` holds `AppState` and `reduce(state, message)`, a pure function with one case per message type.

| Fields | Keep |
| --- | --- |
| `edges`, `pulses`, `symbols`, `validations`, `infoLines` | The newest 2,000, 300, 600, 20, and 50 |
| `clock`, `decodedFrame`, `frameSync`, `sourceStatus`, `ports`, `captures` | The latest message of that type |
| `frameCount`, `lastSeq`, `lostMessages` | Counters. `frameCount` counts `validation` messages; `lostMessages` adds up the gaps in `seq` |
| `syncLoss` | The latest loss of frame sync, held for 5 s (see "Sync loss" in `ui-design.md`) |
| `log` | The protocol log: the last 200 messages and a count per type |

A `sourceStatus` whose `detail` starts with `reset:` is a source reset. The reducer goes back to `initialState` and keeps `ports`, `captures`, the sequence tracking, and the log.

`state/store.ts` wraps the reducer in one Zustand store, `useAppStore`. `dispatch(message)` runs the reducer, and starts the 5 s timer when the message is a `syncLost`; that timer is the only thing in the store that is not a reducer.

- A component subscribes to the slice it shows: `useAppStore((state) => state.lastSeq)`.
- Zustand compares a selector's result by identity. A selector that builds a new object on every call goes through `useShallow`, as the rail's buttons do.
- A test puts the store back with `useAppStore.setState(initialState)` in `beforeEach`.

## Shell

| File | Holds |
| --- | --- |
| `stages.ts` | One pure function per stage, from `AppState` to what the rail shows (`readout` and `health`), and the `stages` table the rail maps over. The rules are the rail table in `ui-design.md` |
| `PipelineRail.tsx` | The seven stage buttons. Each subscribes to its own stage's function, so a new edge re-renders one button. Health is a glyph with a hidden word, not a color alone |
| `Panel.tsx` | A titled region with a chip per stage, an optional line of state, and an error boundary around its children: a view that throws shows a message in its own panel and the rest of the screen keeps running |
| `AppBar.tsx` | The name, the source toggle, the noise slider, Start, Stop, and the theme button |
| `StatusBar.tsx`, `ProtocolLog.tsx` | The run state, the source's name and detail, the last `seq`, the lost count, and the Log button, which opens the protocol log in a sheet |

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
| `hello` | `sourceStatus` stopped, then empty `ports` and `captures` |
| `selectSource` | Plays the recording from the top, whichever source and noise were asked for |
| `stop` | Stops playing and sends `sourceStatus` stopped |
| Anything else | Nothing |

- Each message is sent at the time it arrived when it was recorded, so the stream has the host's real timing, late edges included.
- One second after the last message the recording starts again. It opens with the host's `reset: start` status, so the page clears at the top of every pass.
- `seq` is renumbered from 1 as messages go out, so the count never shows a gap.
- `?speed=10` on the address plays ten times faster: http://localhost:5173/?speed=10. Any positive number works, fractions too; anything else means 1. Only the arrival times shrink. The timestamps inside the messages are untouched, so at 10 the source runs at ten times wall time.

The simulator and the recorder sit behind `import.meta.env.DEV`, so `npm run build` leaves both out, and the fixture with them. A production build opened without the host gets a bridge that only logs a warning, and the status bar reads `no host`.

A development build runs under React's StrictMode, which mounts `App` twice. `hello` goes out twice and the log opens with two sets of answers. That is expected, in the host as well.

## Recording a fixture

A fixture is recorded from the real host, so it cannot drift from the protocol. In a development build `main.tsx` subscribes a recorder to the bridge and puts it on `window.wwvbRecorder`.

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

## Imports

- A local import carries no file extension: `import { reduce } from "./appState"`. A stylesheet and the raw fixture keep theirs (`./index.css`, `./fixtures/synthetic-noise-0.jsonl?raw`).
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

The editor underlines the `import/extensions` rule in that file with "Array has too many items". Leave it. The schema accepts either a plain map of extensions or a settings object with that map under `pattern`, and the file mixes the two: `css`, `jsonl`, and `checkTypeImports` side by side. In oxlint 1.90.0 the schema's own form, with the map under `pattern`, stops checking `import type` lines. The mixed form checks them.

After an oxlint upgrade it is worth one try: move the map under `pattern`, add `.ts` to an `import type` line, and run `npm run lint`. If the line is reported, keep the schema's form.

## Tests

`vite.config.ts` defines two vitest projects, and the file extension picks one:

| Project | Files | Environment |
| --- | --- | --- |
| `unit` | `*.test.ts` | Node |
| `dom` | `*.test.tsx` | jsdom, with `src/test-setup.ts` |

One jsdom environment for everything does not work: jsdom replaces the global `URL`, which `messages.test.ts` uses to find the example files. So a test that needs a DOM is named `.test.tsx`, whether or not it contains JSX.

`test-setup.ts` adds the `jest-dom` matchers and unmounts after each test. Testing Library cannot register that cleanup itself, because there are no vitest globals: test files import `describe`, `it`, and `expect` from `vitest`.

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
