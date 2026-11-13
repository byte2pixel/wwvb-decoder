# Design notes

Engineering documents for the decoder: protocols, pipeline contracts, and the build plan.

| Document | Contents |
| --- | --- |
| [`wwvb-decoder-construction-plan.md`](wwvb-decoder-construction-plan.md) | Step-by-step build plan: documentation first, then Core pipeline, firmware, sources, visualizer, transmitter, demo hardening |
| [`serial-protocol.md`](serial-protocol.md) | Line protocol from the Pico edge logger to the host over USB serial: edge lines, informational lines, reset detection, error handling |
| [`capture-format.md`](capture-format.md) | Capture file format for recorded edge logs: naming, header, wall-clock lines, replay semantics |
| [`pipeline-contracts.md`](pipeline-contracts.md) | C# contracts for the host decoder: edge sources, the six host stages, the pipeline, frame layout and encoder, line codec, synthetic and replay sources, adapters, worked examples |
| [`webview2-messages.md`](webview2-messages.md) | JSON messages between the WPF host and the React page: envelope, one payload per stage output plus status types, page commands, threading |
| [`message-examples/`](message-examples/) | One example JSON file per message type and per command; both test suites assert against them |
| [`ui-design.md`](ui-design.md) | The visualizer's layout and visual language: overview and focus views, the pipeline rail, each panel, tokens, page structure, the source clock, keyboard, and the two protocol additions it needs |
| [`architecture.md`](architecture.md) | Four diagrams: components from antenna to page, pipeline data flow, hexagonal view of Core and its adapters, a session sequence |
| [`adr/`](adr/README.md) | Architecture decision records |
