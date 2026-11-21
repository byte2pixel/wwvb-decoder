# PulseView captures

Logic-analyzer recordings from bring-up: the receiver's T pin, taken before the firmware existed, and the transmitter's carrier pin. They are sigrok `.sr` files (a zip of `metadata` plus raw samples); open them in PulseView.

| File | Taken | What it shows |
| --- | --- | --- |
| `2026-11-02T0648Z-receiver1-polarity.sr` | 2026-11-02 00:48 CST, receiver #1 on the bench, Pico held in BOOTSEL so only the 3V3 regulator ran | 26 s at 20 kHz, channel D0 on T. From 13.8 s on, one pulse per second at a 1.000 s period: HIGH for 0.5, 0.5, 0.2, 0.2, 0.2, 0.2, 0.5, 0.2, 0.8 s, LOW the rest of each second. Reading the high widths as WWVB symbols gives `1 1 0 0 0 0 1 0 M`, which fits seconds 31 to 39 (day-of-year units digit 6, unused bits 34 and 35 zero, UT1 sign negative, marker at 39) on day 306. Reading the low widths instead would need four markers in a row, so the output idles **low** and is **high** during reduced carrier. |
| `2026-11-21T0307Z-transmitter-gp16.sr` | 2026-11-20, about 21:07 CST (saved at 21:09), the transmitter's Pico on the bench, its clock set to 2026-12-10 so that its frames cannot be taken for the station's | 100 s at 1 MHz, channel D0 on GP16, the carrier pin. Bursts of a square wave, with a gap at the start of every second: 61 gaps of 0.2 s, 27 of 0.5 s, and 12 of 0.8 s. Read as symbols, the 100 gaps are seconds 58 and 59 of one frame, a whole frame, and seconds 0 to 37 of the next, and they are the frames for 19:45, 19:46, and 19:47 on day 344 of 2026 with UT1 plus 0.0 and no flags. The details are under [The transmitter's carrier](#the-transmitters-carrier). |

The receiver was not soldered to its header for this capture and had to be held in place, so the first 13 s and the last 2 s are dropouts.

An earlier capture the same night, with the scaffold blinky running, showed a clean 500 ms HIGH followed by 500 ms of chatter at exactly 1.000 s period: the LED toggle modulating the receiver through the Pico's supply. That is why the Step 9 firmware forces the regulator into PWM mode (GP23 high) and keeps the LED at a constant load.

## The transmitter's carrier

What `2026-11-21T0307Z-transmitter-gp16.sr` measures, all by the analyzer's clock:

| Item | Measured | Note |
| --- | --- | --- |
| Carrier frequency | 60,000.15 Hz: 3,833,804 cycles in 63.9 s of bursts | 2.5 parts per million above 60 kHz, which is the difference between the Pico's crystal and the analyzer's |
| Duty | 50.00 percent | Every half cycle is 8 or 9 samples, so no cycle is cut short at the start or the end of a burst |
| Second | 999,997.5 µs | The same 2.5 parts per million: the carrier and the seconds come from one crystal |
| Gap of a 0 | 200,007 or 200,008 µs | A gap is measured from the last high sample to the next, so it holds the low half of the last carrier cycle, about 8 µs |
| Gap of a 1 | 500,007 or 500,008 µs | |
| Gap of a marker | 800,006 or 800,007 µs | Except in second 0, see below |

At both turns of the minute in the capture, the gap of second 0 starts one carrier cycle late: second 59 lasts 1,000,014 µs, second 0 lasts 999,981 µs, and its gap is 799,989 or 799,990 µs. The likely cause is that the firmware works out the new minute's frame at that moment, so the new compare value misses one wrap of the PWM counter. The receiver cannot resolve 17 µs.

Capture the carrier at 1 MHz or more. A first try at 20 kHz, which is a third of the carrier's frequency, put every sample on the same point of the carrier's cycle: the carrier showed as a steady level that flipped every 3.2 s as the two clocks drifted, and the gaps could not be seen while it was low.
