# wwvb-decoder

[![CI](https://github.com/byte2pixel/wwvb-decoder/actions/workflows/ci.yml/badge.svg)](https://github.com/byte2pixel/wwvb-decoder/actions/workflows/ci.yml)

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
| .NET SDK, version in `global.json` or a later patch | `host/` | https://dotnet.microsoft.com/download |
| Node.js, version in `ui/.nvmrc` | `ui/` | https://nodejs.org |
| Rust (rustup) | `firmware/` | https://rustup.rs (the pinned toolchain and target install from `rust-toolchain.toml`) |
| picotool | flashing the Pico over USB | https://github.com/raspberrypi/picotool/releases |
| WebView2 Runtime | `host/` | Included with Windows 11 |

## Getting started

Once per clone:

```powershell
cd ui; npm install; cd ..        # also installs the git hooks (Lefthook)
dotnet tool restore              # CSharpier, pinned in dotnet-tools.json
git config blame.ignoreRevsFile .git-blame-ignore-revs
```

Then:

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

## Checks

`./verify.ps1` runs every CI step locally and stops at the first failure; `-Only host`, `-Only ui`, or `-Only firmware` runs one part. Warnings are errors in every configuration, locally and in CI.

Tests run with coverage. Host tests emit one Cobertura file per project into `host/TestResults/`, and ReportGenerator (pinned in `dotnet-tools.json`) merges them into `coverage/host/` and fails below the line and branch thresholds set in `verify.ps1` and `ci.yml`. UI tests emit `ui/coverage/` and fail below the thresholds in `ui/vite.config.ts`. Both thresholds are ratchets: set a few points under current coverage, raised as tests land, never lowered. CI publishes the test results as checks on the PR and uploads the reports as artifacts. Firmware has no host-runnable tests, so no coverage.

| Language | Formatter | On commit | In CI |
| --- | --- | --- | --- |
| C# | CSharpier, pinned in `dotnet-tools.json` | Formats staged files | `dotnet csharpier check .` |
| TypeScript, CSS, JSON | Prettier, pinned in `ui/package.json` | Formats staged files | `npm run format:check` |
| TypeScript | oxlint, pinned in `ui/package.json` | Lints staged files | `npm run lint` |
| Rust | rustfmt, from the pinned toolchain | Formats staged files | `cargo fmt --check` |

The pre-commit hook (`lefthook.yml`) formats only staged files and re-stages them; unstaged edits in the same file are left alone. `LEFTHOOK=0 git commit` skips it, and CI still checks. `.csharpierignore` and `ui/.prettierignore` skip files that tools rewrite (`*.csproj`, `Directory.Packages.props`, `*.slnx`, `package-lock.json`) and Markdown. C# analyzers run at `latest-recommended`; each host project commits a `packages.lock.json`, and CI restores in locked mode. `.git-blame-ignore-revs` lists bulk-format commits for `git blame` to skip.

## Hardware

- Raspberry Pi Pico 2 (RP2350)
- WWVB 60 kHz receiver module WVB-0860N-03A (CME6005), 1.1–3.3 V
- Breadboard, jumpers, active USB extension

See `firmware/README.md` for wiring.
