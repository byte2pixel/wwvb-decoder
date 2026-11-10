# host

C# / .NET 10 solution (`WwvbDecoder.slnx`).

| Project | Purpose |
| --- | --- |
| `src/WwvbDecoder.Core` | Decoder pipeline. No UI or platform dependencies. |
| `src/WwvbDecoder.Adapters` | The serial port (`SerialEdgeSource`, `SerialPorts`) and capture files on disk (`CaptureFile`, `CaptureWriter`). |
| `src/WwvbDecoder.Host` | WPF app that hosts the React UI in WebView2 and wires Core to the adapters. |
| `src/WwvbDecoder.Tools` | Console app on Spectre.Console.Cli, one class per command: `ports` lists serial ports, `record` writes a capture file from the edge logger, `decode` runs the pipeline on a capture or a live port and prints every stage's output. See [`captures/README.md`](../captures/README.md) for `record` and `decode`. |
| `tests/WwvbDecoder.Core.Tests` | xUnit tests for Core. |
| `tests/WwvbDecoder.Adapters.Tests` | xUnit tests for Adapters: the serial source over a `StringReader`, the capture writer on a `FakeTimeProvider`, capture files in a temp directory. |

## Core layout

Every type in Core is in the namespace `WwvbDecoder.Core`, whichever folder it lives in.

| Folder | Holds |
| --- | --- |
| `Domain/` | Records and enums shared by the pipeline, plus `WwvbFrameLayout` and `WwvbFrameEncoder`. |
| `Ports/` | The interfaces Core works through: `IEdgeSource` and `IStage<TIn,TOut>`. |
| `Sources/` | Edge sources that are pure computation: `SyntheticEdgeSource`. |
| `Protocol/` | The serial line grammar: `EdgeLineCodec`. |
| `Stages/` | The decoder stages. Added in Steps 5 to 7. |
| `Pipeline/` | `DecoderPipeline`. Added in Steps 5 to 7. |

## Hexagonal rule

Core does no I/O and references no I/O packages: no `System.IO.Ports`, no WebView2, no WPF. Code that touches the serial port or the file system belongs outside Core, behind a port such as `IEdgeSource`. PC time enters Core only through an injected `TimeProvider`.

`WwvbDecoder.Adapters` is where that code lives: `System.IO.Ports` and the file system are referenced there and nowhere else. Every Adapters type is in the namespace `WwvbDecoder.Adapters`, whichever folder (`Serial/`, `Capture/`) it sits in.

`SyntheticEdgeSource` is in Core because it only computes edges. It paces them on the injected `TimeProvider`.

To check the rule, run these from the repo root. Both should print nothing:

```powershell
Select-String -Path host/src/WwvbDecoder.Core/WwvbDecoder.Core.csproj -Pattern 'System.IO.Ports|WebView2|UseWPF'
Select-String -Path host/Directory.Build.props, host/src/Directory.Build.props -Pattern '<PackageReference'
```

## React UI

The Host serves the React app from `../ui`. Its built `dist/` is copied to `wwwroot/` next to the exe.

| Configuration | npm build | UI source |
| --- | --- | --- |
| Debug | No, copies an existing `ui/dist` if present | Vite dev server at `http://localhost:5173` when running (hot reload), else `wwwroot/` |
| Release / publish | Yes (`npm ci` + `npm run build`, incremental) | `wwwroot/` |

Debug workflow: run `npm run dev` in `ui/`, then F5 the Host. Force the npm build in any configuration with `-p:BuildUi=true`, or skip it with `-p:BuildUi=false`.
