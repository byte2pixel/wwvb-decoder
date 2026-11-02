# firmware

Rust + [Embassy](https://embassy.dev) firmware for the Raspberry Pi Pico 2 (RP2350).

Right now it only blinks the on-board LED, to prove the toolchain and flashing work.

## Build and flash

1. Install Rust with rustup. The first build installs the `thumbv8m.main-none-eabihf` target from `rust-toolchain.toml`.
2. Install `picotool` and put it on your PATH.
3. Hold **BOOTSEL** while plugging in the Pico 2, then:

```powershell
cargo run --release
```

`.cargo/config.toml` sets the runner to `picotool load -u -v -x -t elf`, so `cargo run` flashes and starts the board.

## Wiring (WVB-0860N-03A receiver)

| Module pin | Pico 2 | Note |
| --- | --- | --- |
| VDD | 3V3 OUT (pin 36) | 1.1–3.3 V only. Never 5 V / VBUS. |
| GND (unlabeled, next to VDD) | GND (pin 38) | |
| P | GND | Low = receiver on |
| T | GP2 (pin 4) | Receiver data output. Idles low; high during the reduced-carrier pulse (measured 2026-11-02) |
