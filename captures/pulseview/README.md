# PulseView captures

Logic-analyzer recordings of the receiver's T pin, taken before the firmware existed. They are sigrok `.sr` files (a zip of `metadata` plus raw samples); open them in PulseView.

| File | Taken | What it shows |
| --- | --- | --- |
| `2026-11-02T0648Z-receiver1-polarity.sr` | 2026-11-02 00:48 CST, receiver #1 on the bench, Pico held in BOOTSEL so only the 3V3 regulator ran | 26 s at 20 kHz, channel D0 on T. From 13.8 s on, one pulse per second at a 1.000 s period: HIGH for 0.5, 0.5, 0.2, 0.2, 0.2, 0.2, 0.5, 0.2, 0.8 s, LOW the rest of each second. Reading the high widths as WWVB symbols gives `1 1 0 0 0 0 1 0 M`, which fits seconds 31 to 39 (day-of-year units digit 6, unused bits 34 and 35 zero, UT1 sign negative, marker at 39) on day 306. Reading the low widths instead would need four markers in a row, so the output idles **low** and is **high** during reduced carrier. |

The receiver was not soldered to its header for this capture and had to be held in place, so the first 13 s and the last 2 s are dropouts.

An earlier capture the same night, with the scaffold blinky running, showed a clean 500 ms HIGH followed by 500 ms of chatter at exactly 1.000 s period: the LED toggle modulating the receiver through the Pico's supply. That is why the Step 9 firmware forces the regulator into PWM mode (GP23 high) and keeps the LED at a constant load.
