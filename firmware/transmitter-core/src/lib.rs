//! The desk-top transmitter without its hardware: the commands it takes, the replies it gives,
//! and what its carrier does at any moment. The contract is the section "Transmitter commands" of
//! `docs/design/serial-protocol.md`.
//!
//! Nothing here reads a clock or touches a pin. The firmware passes the time in, as microseconds
//! since it booted, and does what it is told with the carrier. So all of it is tested on the PC.

#![cfg_attr(not(test), no_std)]

mod command;
mod line;
mod transmitter;

pub use command::{Command, Error, parse};
pub use line::{LineBuffer, MAX_LINE_LEN};
pub use transmitter::{Keying, MAX_REPLY_LEN, Status, Transmitter};
