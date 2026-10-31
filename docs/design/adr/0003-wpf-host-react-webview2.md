# ADR-0003: Single .NET WPF host with a React UI in WebView2 over web messages

**Date**: 2026-10-31 (recorded from `notes/plan.md`; decision made 2026-10-29 when the repo was scaffolded)
**Status**: accepted
**Deciders**: Mel Dommer

## Context

The laptop side has three jobs: own the serial port, run the decoder pipeline, and draw live visualizations (strip chart, pulse-width histogram, 60-cell frame grid, clock). It runs on stage, so it must be one thing to launch with nothing else to start first. The audience works in .NET and industrial protocols, so a C# pipeline is familiar to them. The visualizations are the part most likely to be reworked up to the last day.

## Decision

One .NET 10 WPF application owns the serial port, runs the C# pipeline, and hosts a WebView2 control. The UI is React with TypeScript, built by Vite. In production the host serves the built files through `SetVirtualHostNameToFolderMapping`, with no web server. In development WebView2 points at the Vite dev server for hot reload.

The bridge is WebView2 web messages. C# pushes stage output with `PostWebMessageAsJson`. The page listens for `message` events on `window.chrome.webview` and sends commands (replay speed, noise knob, source switch) back with `postMessage`, which the host handles in `WebMessageReceived`. There is one message type per stage output.

## Alternatives Considered

### Alternative 1: Pure WPF UI
- **Pros**: One language, one build, no bridge or serialization.
- **Cons**: Free WPF charting is thin, and iterating on visual polish is slow compared with the web ecosystem.
- **Why not**: The visualizer is where most late changes land, and the web toolchain makes those changes cheaper.

### Alternative 2: Electron or Tauri desktop app
- **Pros**: Web UI is native to the framework.
- **Cons**: Electron puts the serial port and pipeline in Node; Tauri puts them in Rust. Either way the C# pipeline disappears, and with it the parallel to the audience stack.
- **Why not**: Loses the C# pipeline, which is part of the talk.

### Alternative 3: C# service plus a browser tab over WebSocket or SignalR
- **Pros**: Clean separation; the UI can open in any browser; easy to run on a second machine.
- **Cons**: Two things to launch in the right order on stage. A local port on a locked-down work laptop may hit firewall prompts. The browser chrome is on screen.
- **Why not**: Two processes is one too many for a live demo.

## Consequences

### Positive
- One executable to launch on stage.
- Full web charting ecosystem for the visualizer, with hot reload during development.
- The C# pipeline is testable with ordinary .NET test tooling and recognizable to the audience.
- The bridge is simple enough to explain on one slide.

### Negative
- Two build systems (dotnet and npm) and two languages to keep in step.
- Every stage output needs a JSON shape, and the TypeScript types must track the C# types.
- Depends on the WebView2 Evergreen runtime being present on the demo laptop.

### Risks
- Message volume. Edges arrive a few times per second, which is fine for per-message posting. If a fast-forward replay floods the bridge, batch messages per frame.
- Runtime missing or outdated on the presentation machine. Mitigation: verify during the on-site week, or ship the fixed-version runtime with the app.
