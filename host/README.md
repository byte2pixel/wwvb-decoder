# host

C# / .NET 10 solution (`WwvbDecoder.slnx`).

| Project | Purpose |
| --- | --- |
| `src/WwvbDecoder.Core` | Decoder pipeline. No UI or platform dependencies. |
| `src/WwvbDecoder.Adapters` | The serial port (`SerialEdgeSource`, `SerialPorts`), the desk-top transmitter's commands and replies (`TransmitterSession`, `TransmitterReply`), and capture files on disk (`CaptureFile`, `CaptureWriter`). |
| `src/WwvbDecoder.Host` | WPF app that hosts the React UI in WebView2 and wires Core to the adapters. `Bridge/` serializes pipeline events into web messages and parses page commands; `Services/` starts and stops sources. See [Host layout](#host-layout). |
| `src/WwvbDecoder.Tools` | Console app on Spectre.Console.Cli, one class per command: `ports` lists serial ports, `record` writes a capture file from the edge logger, `decode` runs the pipeline on a capture or a live port and prints every stage's output, `tx-sync` sets the desk-top transmitter's clock from the PC's clock. See [`captures/README.md`](../captures/README.md) for `record` and `decode`, and [`firmware/README.md`](../firmware/README.md) for `tx-sync`. The commands are thin: what can be tested is in Adapters, and this project has no tests. |
| `tests/WwvbDecoder.Core.Tests` | xUnit tests for Core. |
| `tests/WwvbDecoder.Adapters.Tests` | xUnit tests for Adapters: the serial source over a `StringReader`, the transmitter session over a `StringReader` and a `StringWriter` on a `FakeTimeProvider`, the capture writer on a `FakeTimeProvider`, capture files in a temp directory. |
| `tests/WwvbDecoder.Host.Tests` | xUnit tests for Host (`net10.0-windows`, `UseWPF`): message serialization and command parsing against `docs/design/message-examples/*.json`, `WebMessageBridge` on a `FakeTimeProvider`, `SourceController` on a fake environment and a fake settings store, `SettingsStore` on files in a temp folder. No WebView2 is created. |

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
| `Bridge/Messages.cs`, `Bridge/MessageJson.cs` | The envelope and the host-side payload records (`SourceStatus`, `CaptureEntry`, `InfoPayload`, `ItemsPayload<T>`, and `FrameLayoutPayload`, which `Create()` builds from `WwvbFrameLayout`), and the `System.Text.Json` options that produce the document's conventions: camelCase names and enums, `Z` on every `DateTimeOffset`, `TimeSpan` as `...Ms`, and `kind` as the discriminator on `FrameSyncEvent`. Polymorphism and the rename are configured with type-info modifiers here, so Core carries no JSON attributes. |
| `Bridge/Commands.cs`, `Bridge/CommandJson.cs` | The closed `Command` hierarchy and a `JsonDocument` parser for it. An unknown command parses to null and is logged, never thrown. |
| `Bridge/WebMessageBridge.cs`, `Bridge/IMessagePoster.cs` | The host-to-page half: subscribes to every `DecoderPipeline` event, batches edges every 100 ms or 200 items, sends the waiting edges ahead of every `sourceStatus`, numbers every message with one shared `seq`, and posts on the dispatcher. `IMessagePoster` is the slice of it the controller uses to answer commands. |
| `Services/SourceController.cs`, `Services/ISourceEnvironment.cs`, `Services/HostEnvironment.cs` | The page-to-host half: `hello` answers `sourceStatus`, `ports`, `captures`, `frameLayout`, and `selection`; `selectSource` stops the running source, creates the requested one, and starts `DecoderPipeline.RunAsync` on a thread-pool thread; `stop` cancels it. `ISourceEnvironment` is the machine (ports, captures directory, source factories) and `HostEnvironment` is the real one. All three sources are wired: `serial` opens the port when the run starts, `replay` reads a capture from the captures directory and loops, and `synthetic` starts at the current UTC minute. A source that cannot be created is answered with `state: "error"` and the reason, and nothing runs. `setReplaySpeed` and `setNoise` change the running source, and a serial source's banner line becomes the status `detail`. Every `sourceStatus` carries the running source's speed, which is how the page's clock learns it, and `setReplaySpeed` is answered with one. `ChangeStatus` makes the next status from the current one, reads the speed from the source, and posts, all under one lock, because the pump thread posts a reset or a warning while the dispatcher answers a command. |
| `Services/HostSettings.cs`, `Services/ISettingsStore.cs`, `Services/SettingsStore.cs` | What the host remembers between runs, as "What the host remembers" in the messages document specifies it: the source, the port, the capture's file name, the speed, the noise, the reduced-carrier level, and whether a source was running. `SettingsStore` keeps it as JSON in `%LocalAppData%\WwvbDecoder\settings.json`, or in the file named after `--settings`. Reading never throws: a file that is missing, broken, or not valid in one value gives the defaults. Writing never throws either, and replaces the file whole. The controller reads the settings once, when it is constructed, gives the remembered level to stage 2, and saves in each command that changes a value: a `selectSource` that is answered `running`, `setReplaySpeed` and `setNoise` while their source runs, `setReducedCarrierLevel`, and `stop`. After its reply to the first `hello` it starts the remembered source if one was running. A port or a capture that is not found is replaced by the newest capture, with a `warning` in the status that lasts for the run, and that replacement is not saved, so the next launch looks for the remembered source again. |
| `Services/PolarityWatch.cs` | The sign that the reduced-carrier level is inverted: more than 20 markers within 60 s of the source's time and no frame among them. The controller gives each run a watch, feeds it the symbols of stage 3, and puts its text in the status as `warning`. A completed frame, `setReducedCarrierLevel`, and a source reset each clear it. The numbers behind the rule are in the messages document, under `sourceStatus`. |
| `App.xaml.cs` | Composition root: `TimeProvider.System`, the pipeline, the bridge, the settings store, the controller, and the window. |
| `MainWindow.xaml.cs` | Owns the WebView2 control, chooses the dev server or `wwwroot`, forwards `WebMessageReceived` to the controller, and posts outgoing JSON. A start that fails ends with a message and exit code 1, never with an empty window: the WebView2 runtime is missing, `wwwroot/index.html` is missing, or the start failed some other way. For the presenter: `F11` switches full screen, taken in `OnPreviewKeyDown`, where WebView2 raises its accelerator keys, the function keys among them, before the page sees them. The change is made after the handler has returned, because the browser process waits on it. The page gets the keyboard when it has loaded, so its keys work with no click first. In Release the browser's own keys and its right-click menu are off (`AreBrowserAcceleratorKeysEnabled`, `AreDefaultContextMenusEnabled`), so `F5` cannot load the page again in front of the room. Debug keeps them, for the dev tools. |

Threading: commands arrive on the dispatcher and are handled there; the pipeline pumps on a thread-pool thread and the bridge marshals every outgoing message back to the dispatcher. A command that fails is answered with `sourceStatus` in `state: "error"`; the host never throws into the page.

## Hexagonal rule

Core does no I/O and references no I/O packages: no `System.IO.Ports`, no WebView2, no WPF. Code that touches the serial port or the file system belongs outside Core, behind a port such as `IEdgeSource`. PC time enters Core only through an injected `TimeProvider`.

`WwvbDecoder.Adapters` is where that code lives: the `System.IO.Ports` package and the file system are referenced there and nowhere else. One command in Tools, `tx-sync`, opens a serial port itself and hands its two streams to `TransmitterSession`, so that everything in Adapters about the transmitter can be tested without a port. Every Adapters type is in the namespace `WwvbDecoder.Adapters`, whichever folder (`Serial/`, `Capture/`) it sits in.

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

The captures directory, which `captures` lists and `selectSource` with `replay` reads from, is the nearest `captures` folder at or above the executable: the repo's own under F5, and one beside the executable when published. With none, the list is empty. `--captures <dir>` on the command line names another directory; a relative one is taken from the current directory. The host refuses to replay a file outside it.

Manual check of the bridge: launch, and the page's log should show `sourceStatus`, `ports`, `captures`, and `frameLayout` in reply to its `hello`. Start the synthetic source and every stage type counts up; `clock` reaches `locked` after the third accepted frame; Stop answers `sourceStatus` `stopped`.

## Publishing

`./publish.ps1` at the repo root builds the folder that goes to the presentation laptop. `-Output <dir>` names another folder than `publish/`.

```text
publish/
├── WwvbDecoder.Host.exe    the app, self-contained: no .NET on the laptop
├── wwwroot/                the built page; the exe does not run without it
├── captures/               every *.edges.txt of the repo; Replay lists this folder
├── tools/
│   └── WwvbDecoder.Tools.exe   ports, record, decode, tx-sync
└── version.txt             the commit and the time of the publish
```

Copy the whole folder. The laptop needs 64-bit Windows 11, which includes the WebView2 runtime, and nothing else.

The tools are there for two jobs on the laptop. Run them from the folder that holds the app's exe:

```powershell
tools\WwvbDecoder.Tools.exe ports
tools\WwvbDecoder.Tools.exe record --port COM3 --label room    # writes captures\<time>-room.edges.txt
tools\WwvbDecoder.Tools.exe tx-sync COM5                       # sets the desk-top transmitter's clock
```

`record` writes into `captures\` under the current folder, which is the folder the app's Replay list reads, so a capture made in the room is in the list the next time the list is opened.

- Both exes are single files for `win-x64`. `IncludeNativeLibrariesForSelfExtract` puts `WebView2Loader.dll` inside the app's exe, and the first start unpacks it under `%TEMP%\.net`.
- WebView2 keeps its user data in `%LocalAppData%\WwvbDecoder\WebView2` and writes nothing beside the exe, so the app also runs from a folder that cannot be written to.
- The folder is cleared before each publish, all but `captures/`, so a capture recorded there is kept. The script stops before it deletes anything when the folder is not empty and holds no earlier publish, and when the app or the tools are running from it.
- A publish restores from the same `packages.lock.json` files as a build, in locked mode, so it fails when they do not fit and never rewrites them. Two things make that so. `host/src/Directory.Build.props` names `win-x64` in `RuntimeIdentifiers`, which puts that runtime's packages in each lock file; a build is still for any runtime. And the script passes `EnableSingleFileAnalyzer=false`: `PublishSingleFile` would otherwise switch on an analyzer that comes as one more package, `Microsoft.NET.ILLink.Tasks`, which a build does not restore and which has a new version with every SDK patch.
- The `.pdb` files stay, so an error on the laptop has line numbers.
