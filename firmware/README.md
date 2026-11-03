# firmware

Rust + [Embassy](https://embassy.dev) firmware for the Raspberry Pi Pico 2 (RP2350).

The WWVB edge logger: it timestamps every edge on the receiver's data pin with a microsecond counter and writes one text line per edge over USB serial. It makes no decisions about what the edges mean; the host does all decoding (ADR-0001). The full contract is [`docs/design/serial-protocol.md`](../docs/design/serial-protocol.md).

## Build and flash

1. Install Rust with rustup. The first build installs the `thumbv8m.main-none-eabihf` target from `rust-toolchain.toml`.
2. Install `picotool` and put it on your PATH.
3. Hold **BOOTSEL** while plugging in the Pico 2, then:

```powershell
cargo run --release
```

`.cargo/config.toml` sets the runner to `picotool load -u -v -x -t elf`, so `cargo run` flashes and starts the board. To re-flash, hold BOOTSEL and replug again.

## Viewing the output

The Pico shows up as a USB serial port (Device Manager: **Ports (COM & LPT)**, product `wwvb-edge-logger`). Windows binds its built-in driver; no INF is needed.

Open the port with any serial terminal (PuTTY, Tera Term, the VS Code Serial Monitor) at 115200 8N1. The baud rate is ignored, but the terminal **must assert DTR**: the firmware writes nothing until it does. PuTTY and Tera Term assert it by default.

Every time a terminal opens the port, the firmware starts a fresh session with these two lines, which is how you know you have the right port:

```text
# boot
# wwvb-edge-logger 0.1.0 pin=2
```

Then one line per edge:

```text
H 12000000
L 12200412
```

| Line | Meaning |
| --- | --- |
| `H <micros>` | Pin went high: a reduced-carrier pulse started. `<micros>` is microseconds since the Pico booted |
| `L <micros>` | Pin went low: the pulse ended |
| `# dropped <n>` | At least `n` edges were lost: the queue (256 edges) was full, or edges came microseconds apart. At most once every 10 s, only when `n` > 0 |

With the receiver locked you get about **two lines per second**: an `H` at the top of each second, then an `L` 0.2 s (a 0 bit), 0.5 s (a 1 bit), or 0.8 s (a marker) later. Bursts of many lines per second mean noise near the antenna (chargers, LED drivers, the laptop itself).

Edges that happen while no terminal has the port open are discarded. The timestamps keep counting from power-up across sessions; only unplugging or resetting the Pico restarts them from zero.

To check the firmware without a receiver, disconnect T and touch a jumper from GP2 to GND or 3V3: lines appear immediately (contact bounce makes several per touch). With nothing attached GP2 floats, so a few stray lines are normal.

## Board behaviour

| Item | Behaviour |
| --- | --- |
| On-board LED (GP25) | Off until a terminal asserts DTR, then steadily on. It never blinks: a blinking LED modulates the 3V3 rail enough to show up on the receiver output |
| GP23 (regulator PS) | Driven high at boot to force PWM mode, which has less ripple than power-save mode |
| Timing | The edge interrupt runs above the USB interrupt and the USB tasks, so serial traffic does not delay timestamps |

## Wiring (WVB-0860N-03A receiver)

| Module pin | Pico 2 | Note |
| --- | --- | --- |
| VDD | 3V3 OUT (pin 36) | 1.1–3.3 V only. Never 5 V / VBUS. |
| GND (unlabeled, next to VDD) | GND (pin 38) | |
| P | GND | Low = receiver on |
| T | GP2 (pin 4) | Receiver data output. Idles low; high during the reduced-carrier pulse (measured 2026-11-02). No pull resistor is enabled; the module drives the line |
