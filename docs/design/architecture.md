# Architecture

Four views of the decoder, from the antenna to the screen. The decisions behind them are ADR-0001 to ADR-0006; the types on the arrows are defined in [`pipeline-contracts.md`](pipeline-contracts.md) and their JSON forms in [`webview2-messages.md`](webview2-messages.md).

## Components

WWVB's 60 kHz carrier, or the desk-top transmitter from construction plan Step 14, reaches a receiver module whose data pin follows the carrier envelope. A Pico 2 timestamps every edge on that pin and writes one text line per edge over USB serial (ADR-0001, ADR-0002). On the laptop, one WPF process holds everything else (ADR-0003): the Adapters library owns the serial port and the capture files, the Core library runs the seven-stage pipeline, and the Host's bridge turns stage events into web messages for the React page in WebView2.

```mermaid
flowchart LR
  subgraph Air
    TX[WWVB Fort Collins<br/>or desk-top transmitter]
  end
  subgraph Bench
    RX[Receiver module<br/>60 kHz, envelope on data pin]
    PICO[Pico 2 edge logger<br/>Rust + Embassy]
  end
  subgraph Laptop[WPF host process]
    AD[WwvbDecoder.Adapters<br/>SerialEdgeSource, CaptureWriter, CaptureFile]
    CORE[WwvbDecoder.Core<br/>DecoderPipeline, seven stages]
    BR[WwvbDecoder.Host<br/>WebMessageBridge, CommandHandler]
    PAGE[React page in WebView2]
  end
  TX -- 60 kHz carrier --> RX
  RX -- edges on GPIO --> PICO
  PICO -- "USB CDC: H 12000000" --> AD
  AD -- EdgeEvent --> CORE
  CORE -- stage events --> BR
  BR -- "PostWebMessageAsJson" --> PAGE
  PAGE -- "postMessage commands" --> BR
  AD -. edge lines .-> CAP[(captures/*.edges.txt)]
  CAP -. replay .-> CORE
```

## Pipeline data flow

Each stage is a class with one typed input and one typed output (ADR-0005). The pipeline pushes every edge depth-first through stages 2 to 7 and raises one event per stage output, so the visualizer can draw every stage as it happens: the raw edges, the measured pulses, the classified symbols, the frame filling up, the decoded fields, the verdict, and the clock. Only `FrameComplete` events carry on past stage 4; `Searching`, `SlotFilled`, and `SyncLost` reach the page but not stage 5.

```mermaid
flowchart LR
  S1[Stage 1<br/>Edge capture<br/>firmware] -- Edge --> S2[Stage 2<br/>PulseMeasurement]
  S2 -- Pulse --> S3[Stage 3<br/>SymbolClassifier]
  S3 -- Symbol --> S4[Stage 4<br/>FrameSync]
  S4 -- "Frame (from FrameComplete)" --> S5[Stage 5<br/>FieldDecoder]
  S5 -- DecodedFrame --> S6[Stage 6<br/>FrameValidator]
  S6 -- ValidationResult --> S7[Stage 7<br/>WwvbClock]
  S7 -- ClockState --> V
  S1 -. edges .-> V[Visualizer<br/>one view per stage]
  S2 -. pulse .-> V
  S3 -. symbol .-> V
  S4 -. frameSync .-> V
  S5 -. decodedFrame .-> V
  S6 -. validation .-> V
```

## Hexagonal view

Core is pure computation: domain records, the six host stages, the pipeline, the line codec, and the two sources that need no I/O, synthetic and replay over in-memory lines. It reaches the outside world only through two ports, `IEdgeSource` for edges and .NET's `TimeProvider` for PC time (ADR-0004). Everything that touches a device, a file, a window, or a console is an adapter around it, so the whole pipeline is tested with a fake clock and scripted edges, and the demo can switch between live RF, a recording, and a generator without Core noticing.

```mermaid
flowchart TB
  subgraph Adapters
    SER[SerialEdgeSource<br/>System.IO.Ports]
    FILE[CaptureFile, CaptureWriter<br/>file system]
    BRIDGE[WebMessageBridge<br/>WebView2]
    CONSOLE[Tools console<br/>record, decode]
    SYSCLOCK[TimeProvider.System]
  end
  subgraph Core[WwvbDecoder.Core]
    PORT_SRC{{IEdgeSource}}
    PORT_TIME{{TimeProvider}}
    PIPE[DecoderPipeline<br/>stages 2 to 7]
    SYN[SyntheticEdgeSource]
    REP[ReplayEdgeSource]
  end
  SER --> PORT_SRC
  SYN --> PORT_SRC
  REP --> PORT_SRC
  FILE -- lines --> REP
  SYSCLOCK --> PORT_TIME
  PORT_SRC --> PIPE
  PORT_TIME --> PIPE
  PORT_TIME --> SYN
  PORT_TIME --> REP
  PIPE -- events --> BRIDGE
  PIPE -- events --> CONSOLE
```

## A session

The page loads, subscribes to `window.chrome.webview`, and says `hello`. The host answers with its current state so a reloaded page recovers. The user picks a source; the host stops any run in progress, builds the source, and starts the pipeline on a thread-pool thread. Stage events arrive on that pump thread, are serialized there, and are posted from the WPF dispatcher; edges are batched, everything else is one message each. `stop` cancels the run and disposes the source.

```mermaid
sequenceDiagram
  participant P as React page
  participant B as Host bridge (dispatcher)
  participant D as DecoderPipeline (pump thread)
  participant S as IEdgeSource
  P->>B: hello {protocol: 1}
  B-->>P: sourceStatus {source: none, state: stopped}
  B-->>P: ports, captures
  P->>B: selectSource {source: serial, port: COM5}
  B->>S: new SerialEdgeSource("COM5")
  B->>D: RunAsync(source, ct)
  S-->>D: Reset("open")
  D-->>B: SourceReset
  B-->>P: sourceStatus {state: running, detail: "reset: open"}
  loop every edge
    S-->>D: Sample(Edge)
    D-->>B: EdgeReceived, PulseMeasured, SymbolClassified, FrameSyncChanged ...
    B-->>P: edges (batched), pulse, symbol, frameSync, decodedFrame, validation, clock
  end
  P->>B: stop
  B->>D: cancel
  B->>S: DisposeAsync
  B-->>P: sourceStatus {source: none, state: stopped}
```
