# host

C# / .NET 10 solution (`WwvbDecoder.slnx`).

| Project | Purpose |
| --- | --- |
| `src/WwvbDecoder.Core` | Decoder pipeline. No UI or platform dependencies. |
| `src/WwvbDecoder.Host` | WPF app that hosts the React UI in WebView2 and owns the serial port. |
| `tests/WwvbDecoder.Core.Tests` | xUnit tests for Core. |

## React UI

The Host serves the React app from `../ui`. Its built `dist/` is copied to `wwwroot/` next to the exe.

| Configuration | npm build | UI source |
| --- | --- | --- |
| Debug | No, copies an existing `ui/dist` if present | Vite dev server at `http://localhost:5173` when running (hot reload), else `wwwroot/` |
| Release / publish | Yes (`npm ci` + `npm run build`, incremental) | `wwwroot/` |

Debug workflow: run `npm run dev` in `ui/`, then F5 the Host. Force the npm build in any configuration with `-p:BuildUi=true`, or skip it with `-p:BuildUi=false`.
