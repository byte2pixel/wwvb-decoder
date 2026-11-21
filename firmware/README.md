# firmware

Rust + [Embassy](https://embassy.dev) firmware for the Raspberry Pi Pico 2 (RP2350). There are two firmwares, one for each Pico:

| Firmware | What it does |
| --- | --- |
| Edge logger | Timestamps every edge on the receiver's data pin with a microsecond counter and writes one text line per edge over USB serial. It makes no decisions about what the edges mean; the host does all decoding (ADR-0001) |
| Transmitter | Sends a 60 kHz carrier, keyed with WWVB frames, into a coil beside the receiver's antenna, for a room where the station cannot be received. The PC gives it the time and a few commands over USB serial |

Both contracts are in [`docs/design/serial-protocol.md`](../docs/design/serial-protocol.md): the edge logger's lines, and in its last section the transmitter's commands.

## Layout

`firmware/` is a Cargo workspace. `Cargo.toml` at its root lists the members and holds the release profile, because cargo ignores a profile in a member. `Cargo.lock`, `rust-toolchain.toml`, and `.cargo/config.toml` are shared by every member.

| Folder | Package | What it is |
| --- | --- | --- |
| `edge-logger/` | `wwvb-edge-logger` | The edge logger, with its own `build.rs` and `memory.x` |
| `transmitter/` | `wwvb-transmitter` | The transmitter, with its own `build.rs` and `memory.x`. It holds the hardware only: USB, the PWM slice, the tasks |
| `transmitter-core/` | `wwvb-transmitter-core` | The transmitter's logic: the commands, the replies, and what the carrier does at any moment. A `no_std` library that reads no clock and touches no pin, so the PC can test it |
| `wwvb-frame/` | `wwvb-frame` | The frame layout (which second carries what), the encoder that builds a frame from its fields, and the fields for a time given in Unix seconds. A `no_std` library with no dependencies |

## Tests

The two libraries have tests. They run on the PC, not on the Pico, so the command names the PC's target in place of the default one:

```powershell
cargo test -p wwvb-frame -p wwvb-transmitter-core --target x86_64-pc-windows-msvc
```

`wwvb-frame`'s layout test reads `docs/design/message-examples/frameLayout.json`, the example of the host's `frameLayout` message. The host's tests hold `WwvbFrameLayout` to that file and the page's simulator plays a copy of it, so the three layouts cannot drift apart. `serde_json` is there to read it, as a development dependency: it is never part of a firmware image.

`wwvb-transmitter-core`'s tests give it the time as a number, so a test can key a whole minute and compare it with the frame the encoder builds. The two binaries have no tests; they are checked on the boards.

## Build and flash

1. Install Rust with rustup. The first build installs the `thumbv8m.main-none-eabihf` target from `rust-toolchain.toml`.
2. Install `picotool` and put it on your PATH.
3. Hold **BOOTSEL** while plugging in the Pico 2, then run the line for the firmware that this Pico gets:

```powershell
cargo run --release -p wwvb-edge-logger
cargo run --release -p wwvb-transmitter
```

`.cargo/config.toml` sets the runner to `picotool load -u -v -x -t elf`, so `cargo run` flashes and starts the board. To re-flash, hold BOOTSEL and replug again. Keep only one Pico in BOOTSEL at a time, so that `picotool` cannot load the wrong one.

Each Pico shows up as a USB serial port (Device Manager: **Ports (COM & LPT)**, product `wwvb-edge-logger` or `wwvb-transmitter`). Windows binds its built-in driver; no INF is needed. Any serial terminal (PuTTY, Tera Term, the VS Code Serial Monitor) opens the port at 115200 8N1. The baud rate is ignored, but the terminal **must assert DTR**: neither firmware writes anything until it does. PuTTY and Tera Term assert it by default.

## Edge logger

### Viewing the output

Every time a terminal opens the port, the firmware starts a fresh session with these two lines, which is how you know you have the right port:

```text
# session
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

### Board behaviour

| Item | Behaviour |
| --- | --- |
| On-board LED (GP25) | Off until a terminal asserts DTR, then steadily on. It never blinks: a blinking LED modulates the 3V3 rail enough to show up on the receiver output |
| GP23 (regulator PS) | Driven high at boot to force PWM mode, which has less ripple than power-save mode |
| Timing | The edge interrupt runs above the USB interrupt and the USB tasks, so serial traffic does not delay timestamps |

### Wiring (WVB-0860N-03A receiver)

| Module pin | Pico 2 | Note |
| --- | --- | --- |
| VDD | 3V3 OUT (pin 36) | 1.1–3.3 V only. Never 5 V / VBUS. |
| GND (unlabeled, next to VDD) | GND (pin 38) | |
| P | GND | Low = receiver on |
| T | GP2 (pin 4) | Receiver data output. Idles low; high during the reduced-carrier pulse (measured 2026-11-02). No pull resistor is enabled; the module drives the line |

## Transmitter

### Talking to it

Every time a terminal opens the port, the firmware starts a session with these two lines:

```text
# session
# wwvb-transmitter 0.1.0 pin=16
```

After that it writes only replies: one line for each command line. It does not echo, so turn on local echo in the terminal to see what you type. A command ends at Enter, whether the terminal sends a carriage return, a line feed, or both.

| Command | Effect |
| --- | --- |
| `TIME <unix_seconds>` | Sets the clock: that second starts at the moment the line ends. The years 2000 to 2099 only |
| `STATUS` | Changes nothing |
| `ON`, `OFF` | Switch the carrier. The clock runs on while it is off |
| `FRAME <symbols>` | Sends these 60 symbols of `0`, `1`, or `M` in place of the next minute's frame, once |

A command that worked is answered with the state, and one that did not with `ERR` and a reason:

```text
STATUS
OK time=unset carrier=on custom=none
TIME 1796931000
OK time=1796931000 carrier=on custom=none
time 1796931000
ERR unknown command
```

`time` is the clock's current second as Unix seconds, `carrier` is the switch that `ON` and `OFF` set, and `custom` says whether a `FRAME` waits for the next minute. The section "Transmitter commands" of the contract has every rule and every reason.

Typing `TIME` by hand sets the clock to within a second or so of what you meant, which is enough to see frames decode. To set it to the PC's clock, a program sends the line at the top of a second.

### Board behaviour

| Item | Behaviour |
| --- | --- |
| After power-up | No time and no carrier, until the first `TIME`. The time is lost when the power is |
| Carrier (GP16) | A 3.3 V square wave at 60 kHz, 50 percent duty: the 150 MHz system clock divided by 2,500 in one PWM slice. Reduced carrier is no carrier: the pin is held low |
| Keying | Each second starts with the carrier off for 0.2 s (a 0), 0.5 s (a 1), or 0.8 s (a marker), then on for the rest of the second. Each minute is one frame, which says that minute |
| On-board LED (GP25) | Steadily on while a frame is transmitted: from the first `TIME`, except after `OFF`. It does not follow the keying. On the receiver's Pico a blinking LED reached the receiver through the supply (see the edge logger's table), and one that blinks in step with the time code here could pass for the signal from the coil |
| Sessions | The transmitter keeps its time and keeps transmitting when the terminal closes the port |
| Clock | Runs on the Pico's crystal after `TIME`. Send `TIME` again to correct it |
| GP23 (regulator PS) | Driven high at boot to force PWM mode, as on the edge logger, so that this board adds less noise beside the receiver |

### Wiring (coil)

| From | To | Note |
| --- | --- | --- |
| GP16 (pin 21) | 1 kΩ resistor | The resistor limits the current to about 3 mA |
| 1 kΩ resistor | One end of the coil | |
| Other end of the coil | GND (pin 23) | |

The coil is 10 to 20 turns of 22 AWG wire, about 5 cm across, laid beside the receiver's ferrite rod with its axis along the rod. There is no amplifier, which keeps the field to a few centimetres. The transmitter's Pico shares nothing with the receiver but that field: the receiver stays wired to the edge logger's Pico.

The number of turns and the distance that work are not measured yet (construction plan Step 14, task 4).
