# host

C# / .NET 10 solution (`WwvbDecoder.slnx`).

| Project | Purpose |
| --- | --- |
| `src/WwvbDecoder.Core` | Decoder pipeline. No UI or platform dependencies. |
| `src/WwvbDecoder.Adapters` | The serial port (`SerialEdgeSource`, `SerialPorts`) and capture files on disk (`CaptureFile`, `CaptureWriter`). |
| `src/WwvbDecoder.Host` | WPF app that hosts the React UI in WebView2 and wires Core to the adapters. `Bridge/` serializes pipeline events into web messages and parses page commands; `Services/` starts and stops sources. See [Host layout](#host-layout). |
| `src/WwvbDecoder.Tools` | Console app on Spectre.Console.Cli, one class per command: `ports` lists serial ports, `record` writes a capture file from the edge logger, `decode` runs the pipeline on a capture or a live port and prints every stage's output. See [`captures/README.md`](../captures/README.md) for `record` and `decode`. |
| `tests/WwvbDecoder.Core.Tests` | xUnit tests for Core. |
| `tests/WwvbDecoder.Adapters.Tests` | xUnit tests for Adapters: the serial source over a `StringReader`, the capture writer on a `FakeTimeProvider`, capture files in a temp directory. |
| `tests/WwvbDecoder.Host.Tests` | xUnit tests for Host (`net10.0-windows`, `UseWPF`): message serialization and command parsing against `docs/design/message-examples/*.json`, `WebMessageBridge` on a `FakeTimeProvider`, `SourceController` on a fake environment. No WebView2 is created. |

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

## Host layout

Every type is in the namespace `WwvbDecoder.Host.Bridge` or `WwvbDecoder.Host.Services`. The contract between the host and the page is `docs/design/webview2-messages.md`; the example files next to it are what the tests assert against.

| File | Holds |
| --- | --- |
| `Bridge/Messages.cs`, `Bridge/MessageJson.cs` | The envelope and the host-side payload records (`SourceStatus`, `CaptureEntry`, `InfoPayload`, `ItemsPayload<T>`), and the `System.Text.Json` options that produce the document's conventions: camelCase names and enums, `Z` on every `DateTimeOffset`, `TimeSpan` as `...Ms`, and `kind` as the discriminator on `FrameSyncEvent`. Polymorphism and the rename are configured with type-info modifiers here, so Core carries no JSON attributes. |
| `Bridge/Commands.cs`, `Bridge/CommandJson.cs` | The closed `Command` hierarchy and a `JsonDocument` parser for it. An unknown command parses to null and is logged, never thrown. |
| `Bridge/WebMessageBridge.cs`, `Bridge/IMessagePoster.cs` | The host-to-page half: subscribes to every `DecoderPipeline` event, batches edges every 100 ms or 200 items, numbers every message with one shared `seq`, and posts on the dispatcher. `IMessagePoster` is the slice of it the controller uses to answer commands. |
| `Services/SourceController.cs`, `Services/ISourceEnvironment.cs`, `Services/HostEnvironment.cs` | The page-to-host half: `hello` answers `sourceStatus`, `ports`, and `captures`; `selectSource` stops the running source, creates the requested one, and starts `DecoderPipeline.RunAsync` on a thread-pool thread; `stop` cancels it. `ISourceEnvironment` is the machine (ports, captures directory, source factories) and `HostEnvironment` is the real one. Only the synthetic source is wired; `serial` and `replay` answer `sourceStatus` with `state: "error"` and `detail: "not available yet"` until Step 13. |
| `App.xaml.cs` | Composition root: `TimeProvider.System`, the pipeline, the bridge, the controller, and the window. |
| `MainWindow.xaml.cs` | Owns the WebView2 control, chooses the dev server or `wwwroot`, forwards `WebMessageReceived` to the controller, and posts outgoing JSON. |

Threading: commands arrive on the dispatcher and are handled there; the pipeline pumps on a thread-pool thread and the bridge marshals every outgoing message back to the dispatcher. A command that fails is answered with `sourceStatus` in `state: "error"`; the host never throws into the page.

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

To run the host against a production bundle instead, run `npm run build` in `ui/` with the dev server stopped and launch the Host. The Host project sets `DisableFastUpToDateCheck` so that Visual Studio runs MSBuild on every launch: the `dist/` files are globbed inside a target, and Visual Studio's own up-to-date check lagged the file system by many seconds and launched a stale `wwwroot/` when started right after `npm run build`.

Manual check of the bridge: launch, and the page's log should show `sourceStatus`, `ports`, and `captures` in reply to its `hello`. Start the synthetic source and every stage type counts up; `clock` reaches `locked` after the third accepted frame; Stop answers `sourceStatus` `stopped`.
