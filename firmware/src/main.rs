//! WWVB edge logger firmware for the Raspberry Pi Pico 2 (RP2350).
//!
//! Scaffold only: blinks the on-board LED to prove the toolchain and
//! flashing work. See firmware/README.md for what goes here next.

#![no_std]
#![no_main]

use embassy_executor::Spawner;
use embassy_rp::gpio::{Level, Output};
use embassy_time::Timer;
use panic_halt as _;

// Shown by `picotool info`.
#[unsafe(link_section = ".bi_entries")]
#[used]
pub static PICOTOOL_ENTRIES: [embassy_rp::binary_info::EntryAddr; 4] = [
    embassy_rp::binary_info::rp_program_name!(c"wwvb-edge-logger"),
    embassy_rp::binary_info::rp_program_description!(c"WWVB receiver edge logger"),
    embassy_rp::binary_info::rp_cargo_version!(),
    embassy_rp::binary_info::rp_program_build_attribute!(),
];

#[embassy_executor::main(executor = "embassy_rp::executor::Executor", entry = "cortex_m_rt::entry")]
async fn main(_spawner: Spawner) {
    let p = embassy_rp::init(Default::default());
    let mut led = Output::new(p.PIN_25, Level::Low);

    loop {
        led.toggle();
        Timer::after_millis(500).await;
    }
}
