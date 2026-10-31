# Design notes

Engineering documents for the decoder: protocols, pipeline contracts, and the build plan.

| Document | Contents |
| --- | --- |
| [`wwvb-decoder-construction-plan.md`](wwvb-decoder-construction-plan.md) | Step-by-step build plan: documentation first, then Core pipeline, firmware, sources, visualizer, transmitter, demo hardening |
| [`serial-protocol.md`](serial-protocol.md) | Line protocol from the Pico edge logger to the host over USB serial: edge lines, informational lines, reset detection, error handling |
| [`capture-format.md`](capture-format.md) | Capture file format for recorded edge logs: naming, header, wall-clock lines, replay semantics |
| [`adr/`](adr/README.md) | Architecture decision records |
