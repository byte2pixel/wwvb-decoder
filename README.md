# wwvb-decoder

Receive NIST's WWVB 60 kHz time signal, decode it in software, and show every step of the decode live.

Built for a knowledge-sharing talk: the same layered thinking as HART (physical layer → bits → framing → fields → validation), on a radio signal instead of a 4–20 mA loop.

## Layout

```text
wwvb-decoder/
├── firmware/   Rust + Embassy on a Raspberry Pi Pico 2: timestamps receiver edges, streams them over USB serial
├── host/       C# / .NET 10: WPF + WebView2 host, decoder pipeline (Core) and tests
├── ui/         React + TypeScript (Vite), shown inside the host's WebView2
├── captures/   Recorded edge logs used for replay and tests
└── docs/
    ├── presentation/  Talk outline, notes, slides and images
    └── design/        Protocol, pipeline and wiring notes
```

## Prerequisites

| Tool | For | Install |
| --- | --- | --- |
| .NET 10 SDK | `host/` | https://dotnet.microsoft.com/download |
| Node.js LTS | `ui/` | https://nodejs.org |
| Rust (rustup) | `firmware/` | https://rustup.rs (the target installs from `rust-toolchain.toml`) |
| picotool | flashing the Pico over USB | https://github.com/raspberrypi/picotool/releases |
| WebView2 Runtime | `host/` | Included with Windows 11 |

## Getting started

```powershell
# Firmware: hold BOOTSEL while plugging in the Pico 2, then
cd firmware
cargo run --release

# UI
cd ui
npm install
npm run dev

# Host
cd host
dotnet build
dotnet test
```

## Hardware

- Raspberry Pi Pico 2 (RP2350)
- WWVB 60 kHz receiver module WVB-0860N-03A (CME6005), 1.1–3.3 V
- Breadboard, jumpers, active USB extension

See `firmware/README.md` for wiring.
