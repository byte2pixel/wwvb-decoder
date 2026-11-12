# ui

React + TypeScript, built with Vite. The host loads it inside WebView2.

```powershell
npm install
npm run dev     # dev server with hot reload; the Debug host prefers it when it is running
npm test        # vitest, once
npm run lint    # oxlint
npm run build   # type-check every project, then static files in dist/ for the host to serve
```

All four must pass before a push; CI runs the last three.

## Protocol

The page talks to the host over WebView2 web messages, as `docs/design/webview2-messages.md` specifies. `src/protocol/` is the page's half of that contract.

| File | Holds |
| --- | --- |
| `messages.ts` | zod schemas for every host envelope and page command, and the types inferred from them. The schemas are the source of truth; no type is written by hand. `hostMessageSchema` is a discriminated union on `type`, `commandSchema` on `command`, with the three `selectSource` shapes nested on `source`. |
| `bridge.ts` | `createBridge(webview)` wraps `window.chrome.webview`: `onMessage` validates each event with `safeParse` and drops, with a `console.warn`, anything that fails; `send` posts a command object. `findWebView()` returns the real one, or `undefined` in a plain browser, where the bridge becomes a logged no-op so `npm run dev` works outside the host. |
| `log.ts` | A pure reducer keeping the last 200 messages and a count per type. |
| `*.test.ts` | `messages.test.ts` parses every file in `docs/design/message-examples/` through the schema its file name selects, so the host's serializer tests and these tests assert against the same files. The bridge is tested with a fake webview object, no DOM. |

`App.tsx` is the protocol page from construction plan Step 8: a source selector with only synthetic enabled, a noise slider, Start and Stop, `hello` on load, and the log with its counters. Steps 12 and 13 replace it with the visualizer.

## TypeScript projects

`tsconfig.json` is a solution file that references three projects:

| Project | Covers | Types |
| --- | --- | --- |
| `tsconfig.app.json` | `src/`, excluding `*.test.ts*` | `vite/client` |
| `tsconfig.test.json` | all of `src/`, including the tests | `node`, `vite/client` |
| `tsconfig.node.json` | `vite.config.ts` | |

The test project extends the app project and resets its `exclude` to `[]`. It includes all of `src`, not only the test files, so the editor's auto-import sees modules a test has not imported yet; the cost is that `tsc -b` type-checks the non-test sources twice. Test files import `describe`, `it`, and `expect` from `vitest` explicitly; there is no `vitest/globals`.

TypeScript here is 7.x, the Go-based compiler. VS Code's bundled TypeScript cannot load it as a workspace version and falls back to its own, which is fine for editing; the build uses 7.x from the shell.
