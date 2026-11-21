//! The transmitter's state, and what its carrier does at any moment.

use core::fmt;

use wwvb_frame::{Fields, Frame, Symbol, encode};

use crate::command::{Command, Error};

const MICROS_PER_SECOND: u64 = 1_000_000;

/// The longest reply, without its line feed: a status line whose time has the 20 digits of the
/// largest `u64`. The firmware checks at compile time that it fits a USB packet.
pub const MAX_REPLY_LEN: usize = 55;

/// The state after a command. Written with `{}`, it is the status line of the contract.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Status {
    /// The clock's current second, as Unix seconds. `None` before the first `TIME`.
    pub time: Option<u64>,
    /// The carrier switch, which `ON` and `OFF` set.
    pub carrier_on: bool,
    /// Whether a custom frame waits for the next minute.
    pub custom_waiting: bool,
}

impl fmt::Display for Status {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self.time {
            Some(unix_seconds) => write!(f, "OK time={unix_seconds}")?,
            None => f.write_str("OK time=unset")?,
        }
        let carrier = if self.carrier_on { "on" } else { "off" };
        let custom = if self.custom_waiting {
            "waiting"
        } else {
            "none"
        };
        write!(f, " carrier={carrier} custom={custom}")
    }
}

/// What the carrier does from the moment that was asked about.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Keying {
    /// Whether the carrier is sent.
    pub carrier: bool,
    /// Whether a frame is being transmitted: the minute has a frame and the carrier switch is
    /// on. Unlike `carrier` it holds through the reduced part of each second, so an LED that
    /// shows it is a constant load and never blinks with the time code.
    pub transmitting: bool,
    /// When to ask again, in microseconds since boot. `None` is never: only a command changes
    /// the answer.
    pub until_micros: Option<u64>,
}

/// The clock: a Unix second and the moment it started, in microseconds since boot.
#[derive(Clone, Copy)]
struct Clock {
    unix_seconds: u64,
    at_micros: u64,
}

/// The minute in progress and the frame chosen for it.
#[derive(Clone, Copy)]
struct Minute {
    /// The Unix second the minute starts at.
    starts_at: u64,
    /// `None` when no frame can say the minute, which is so past the end of 2099.
    frame: Option<Frame>,
}

/// The transmitter's state: the clock, the carrier switch, and the custom frame.
///
/// Every moment is given in microseconds since boot, and must not go backwards from one call to
/// the next.
pub struct Transmitter {
    clock: Option<Clock>,
    carrier_on: bool,
    /// The custom frame that waits for the next minute.
    custom: Option<Frame>,
    /// `None` until `tick` first looks after a `TIME`.
    minute: Option<Minute>,
}

impl Transmitter {
    /// As at power-up: no time, the carrier switch on, no custom frame.
    pub const fn new() -> Self {
        Self {
            clock: None,
            carrier_on: true,
            custom: None,
            minute: None,
        }
    }

    /// Obeys a command that arrived at this moment. A refused command changes nothing.
    ///
    /// After `TIME`, `ON`, and `OFF` the carrier may have to change at once, so the caller asks
    /// `tick` again.
    pub fn handle(&mut self, command: Command, now_micros: u64) -> Result<Status, Error> {
        match command {
            Command::Time(unix_seconds) => {
                self.clock = Some(Clock {
                    unix_seconds,
                    at_micros: now_micros,
                });
                // The frame in progress is abandoned, custom or not.
                self.minute = None;
            }
            Command::Status => {}
            Command::On => self.carrier_on = true,
            Command::Off => self.carrier_on = false,
            Command::Frame(frame) => {
                if self.clock.is_none() {
                    return Err(Error::ClockNotSet);
                }
                self.custom = Some(frame);
            }
        }
        Ok(self.status(now_micros))
    }

    /// The state at this moment.
    pub fn status(&self, now_micros: u64) -> Status {
        Status {
            time: self.clock.map(|clock| {
                let elapsed = now_micros.saturating_sub(clock.at_micros);
                clock.unix_seconds + elapsed / MICROS_PER_SECOND
            }),
            carrier_on: self.carrier_on,
            custom_waiting: self.custom.is_some(),
        }
    }

    /// What the carrier does from this moment, and when to ask again. The firmware asks at every
    /// moment a `Keying` names and after every command, and sets the carrier as it is told.
    ///
    /// It is here that a minute's frame is chosen, the first time a moment falls in that minute.
    pub fn tick(&mut self, now_micros: u64) -> Keying {
        let Some(clock) = self.clock else {
            return Keying {
                carrier: false,
                transmitting: false,
                until_micros: None,
            };
        };
        let elapsed = now_micros.saturating_sub(clock.at_micros);
        let into_second = elapsed % MICROS_PER_SECOND;
        let second_started_at = clock.at_micros + elapsed - into_second;
        let unix_seconds = clock.unix_seconds + elapsed / MICROS_PER_SECOND;
        let second = unix_seconds % 60;
        let minute_starts_at = unix_seconds - second;

        let minute = match self.minute {
            Some(minute) if minute.starts_at == minute_starts_at => minute,
            earlier => {
                // A custom frame takes the place of a minute that follows another. The minute
                // that a `TIME` lands in follows none, so a waiting frame keeps waiting.
                let custom = if earlier.is_some() {
                    self.custom.take()
                } else {
                    None
                };
                let clock_frame =
                    || Fields::from_unix_seconds(minute_starts_at).map(|fields| encode(&fields));
                let minute = Minute {
                    starts_at: minute_starts_at,
                    frame: custom.or_else(clock_frame),
                };
                self.minute = Some(minute);
                minute
            }
        };

        let second_ends_at = second_started_at + MICROS_PER_SECOND;
        let Some(frame) = minute.frame else {
            return Keying {
                carrier: false,
                transmitting: false,
                until_micros: Some(second_ends_at),
            };
        };
        // `second` is under 60, so the cast and the index are in range.
        let reduced_micros = reduced_micros(frame[second as usize]);
        if into_second < reduced_micros {
            Keying {
                carrier: false,
                transmitting: self.carrier_on,
                until_micros: Some(second_started_at + reduced_micros),
            }
        } else {
            // The switch gates the output only: the schedule is the same on or off.
            Keying {
                carrier: self.carrier_on,
                transmitting: self.carrier_on,
                until_micros: Some(second_ends_at),
            }
        }
    }
}

impl Default for Transmitter {
    fn default() -> Self {
        Self::new()
    }
}

/// How long the carrier is reduced at the start of a second that sends this symbol.
const fn reduced_micros(symbol: Symbol) -> u64 {
    match symbol {
        Symbol::Zero => 200_000,
        Symbol::One => 500_000,
        Symbol::Marker => 800_000,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// 2026-12-10T19:30:00Z.
    const AT_1930: u64 = 1_796_931_000;

    /// The moment, in microseconds since boot, that the tests set the clock at. Not a round
    /// number, so that a schedule counted from boot and not from this moment fails.
    const SET_AT: u64 = 7_654_321;

    const SECOND: u64 = MICROS_PER_SECOND;

    /// How one second was keyed.
    #[derive(Clone, Copy, Debug, PartialEq, Eq)]
    enum Keyed {
        /// Reduced carrier for the symbol's time, then full carrier.
        Sent(Symbol),
        /// The same two parts, but the carrier stayed off in the second one.
        Gated(Symbol),
        /// No carrier, and nothing to wait for but the end of the second.
        Silent,
    }

    /// Asks as the firmware does, for this many seconds from `now`: at the start of each second,
    /// and again at the moment the first answer names. `now` must be the start of a second.
    fn key(transmitter: &mut Transmitter, mut now: u64, seconds: usize) -> Vec<Keyed> {
        let mut keyed = Vec::new();
        for _ in 0..seconds {
            let second_ends_at = now + SECOND;
            let start = transmitter.tick(now);
            assert!(!start.carrier, "a second starts without carrier");
            let reduced_until = start.until_micros.expect("a set clock names a next moment");
            if reduced_until == second_ends_at {
                keyed.push(Keyed::Silent);
            } else {
                let symbol = match reduced_until - now {
                    200_000 => Symbol::Zero,
                    500_000 => Symbol::One,
                    800_000 => Symbol::Marker,
                    other => panic!("reduced for {other} microseconds"),
                };
                let rest = transmitter.tick(reduced_until);
                assert_eq!(rest.until_micros, Some(second_ends_at));
                keyed.push(if rest.carrier {
                    Keyed::Sent(symbol)
                } else {
                    Keyed::Gated(symbol)
                });
            }
            now = second_ends_at;
        }
        keyed
    }

    /// The frame that says the minute holding this second.
    fn clock_frame(unix_seconds: u64) -> Frame {
        encode(&Fields::from_unix_seconds(unix_seconds).unwrap())
    }

    fn sent(symbols: &[Symbol]) -> Vec<Keyed> {
        symbols.iter().map(|&symbol| Keyed::Sent(symbol)).collect()
    }

    fn gated(symbols: &[Symbol]) -> Vec<Keyed> {
        symbols.iter().map(|&symbol| Keyed::Gated(symbol)).collect()
    }

    /// A transmitter whose clock was set to `unix_seconds` at `SET_AT`.
    fn set_to(unix_seconds: u64) -> Transmitter {
        let mut transmitter = Transmitter::new();
        transmitter
            .handle(Command::Time(unix_seconds), SET_AT)
            .unwrap();
        transmitter
    }

    /// A frame no clock would send: a one in every second.
    const ALL_ONES: Frame = [Symbol::One; 60];

    /// Another: a zero in every second.
    const ALL_ZEROS: Frame = [Symbol::Zero; 60];

    #[test]
    fn at_power_up_there_is_no_time_the_carrier_switch_is_on_and_no_custom_frame_waits() {
        let transmitter = Transmitter::new();

        assert_eq!(
            transmitter.status(SET_AT),
            Status {
                time: None,
                carrier_on: true,
                custom_waiting: false,
            }
        );
    }

    #[test]
    fn time_sets_the_clock_and_the_clock_runs_on() {
        let mut transmitter = Transmitter::new();

        let reply = transmitter.handle(Command::Time(AT_1930), SET_AT);

        assert_eq!(reply.unwrap().time, Some(AT_1930));
        let cases = [
            (SECOND - 1, AT_1930),
            (SECOND, AT_1930 + 1),
            (17 * SECOND + 999_999, AT_1930 + 17),
            (3_600 * SECOND, AT_1930 + 3_600),
        ];
        for (later, unix_seconds) in cases {
            let status = transmitter.status(SET_AT + later);
            assert_eq!(
                status.time,
                Some(unix_seconds),
                "{later} microseconds later"
            );
        }
    }

    #[test]
    fn status_changes_nothing_and_answers_with_the_state() {
        let mut transmitter = set_to(AT_1930);

        let reply = transmitter.handle(Command::Status, SET_AT + 17 * SECOND);

        assert_eq!(
            reply,
            Ok(Status {
                time: Some(AT_1930 + 17),
                carrier_on: true,
                custom_waiting: false,
            })
        );
    }

    #[test]
    fn off_and_on_set_the_carrier_switch() {
        let mut transmitter = Transmitter::new();

        let off = transmitter.handle(Command::Off, SET_AT).unwrap();
        let still_off = transmitter.handle(Command::Status, SET_AT).unwrap();
        let on = transmitter.handle(Command::On, SET_AT).unwrap();

        assert!(!off.carrier_on);
        assert!(!still_off.carrier_on);
        assert!(on.carrier_on);
    }

    #[test]
    fn frame_before_the_first_time_is_refused_and_no_frame_waits() {
        let mut transmitter = Transmitter::new();

        let reply = transmitter.handle(Command::Frame(ALL_ONES), SET_AT);

        assert_eq!(reply, Err(Error::ClockNotSet));
        let status = transmitter.handle(Command::Time(AT_1930), SET_AT).unwrap();
        assert!(!status.custom_waiting);
    }

    #[test]
    fn frame_after_time_waits() {
        let mut transmitter = set_to(AT_1930);

        let reply = transmitter.handle(Command::Frame(ALL_ONES), SET_AT + 42 * SECOND);

        assert_eq!(
            reply,
            Ok(Status {
                time: Some(AT_1930 + 42),
                carrier_on: true,
                custom_waiting: true,
            })
        );
    }

    /// The status lines of the example in the contract.
    #[test]
    fn a_status_is_written_as_the_status_line_of_the_contract() {
        let cases = [
            (None, true, false, "OK time=unset carrier=on custom=none"),
            (
                Some(1_796_931_017),
                true,
                false,
                "OK time=1796931017 carrier=on custom=none",
            ),
            (
                Some(1_796_931_042),
                true,
                true,
                "OK time=1796931042 carrier=on custom=waiting",
            ),
            (
                Some(1_796_931_090),
                false,
                false,
                "OK time=1796931090 carrier=off custom=none",
            ),
        ];

        for (time, carrier_on, custom_waiting, line) in cases {
            let status = Status {
                time,
                carrier_on,
                custom_waiting,
            };
            assert_eq!(status.to_string(), line);
        }
    }

    #[test]
    fn the_longest_status_line_is_max_reply_len_and_no_error_is_longer() {
        let longest = Status {
            time: Some(u64::MAX),
            carrier_on: false,
            custom_waiting: true,
        };
        let errors = [
            Error::UnknownCommand,
            Error::UnexpectedArgument,
            Error::TimeNotANumber,
            Error::TimeOutOfRange,
            Error::BadFrame,
            Error::ClockNotSet,
            Error::LineTooLong,
        ];

        assert_eq!(longest.to_string().len(), MAX_REPLY_LEN);
        for error in errors {
            assert!(error.to_string().len() <= MAX_REPLY_LEN, "{error}");
        }
    }

    #[test]
    fn without_a_time_there_is_no_carrier_and_nothing_to_wait_for() {
        let silent = Keying {
            carrier: false,
            transmitting: false,
            until_micros: None,
        };
        let mut transmitter = Transmitter::new();

        assert_eq!(transmitter.tick(SET_AT), silent);
        transmitter.handle(Command::Off, SET_AT).unwrap();
        assert_eq!(transmitter.tick(SET_AT), silent);
        transmitter.handle(Command::On, SET_AT).unwrap();
        assert_eq!(transmitter.tick(SET_AT + 90 * SECOND), silent);
    }

    /// Seconds 0, 1, and 2 of the 19:30 frame are a marker, a zero, and a one.
    #[test]
    fn a_second_is_reduced_for_its_symbols_time_and_full_for_the_rest() {
        let mut transmitter = set_to(AT_1930);
        let cases = [
            // The marker: 0.8 s.
            (0, false, 800_000),
            (799_999, false, 800_000),
            (800_000, true, SECOND),
            (999_999, true, SECOND),
            // The zero: 0.2 s.
            (SECOND, false, SECOND + 200_000),
            (SECOND + 199_999, false, SECOND + 200_000),
            (SECOND + 200_000, true, 2 * SECOND),
            // The one: 0.5 s.
            (2 * SECOND, false, 2 * SECOND + 500_000),
            (2 * SECOND + 499_999, false, 2 * SECOND + 500_000),
            (2 * SECOND + 500_000, true, 3 * SECOND),
        ];

        for (after, carrier, until_after) in cases {
            let keying = transmitter.tick(SET_AT + after);
            let expected = Keying {
                carrier,
                transmitting: true,
                until_micros: Some(SET_AT + until_after),
            };
            assert_eq!(
                keying, expected,
                "{after} microseconds after the clock was set"
            );
        }
    }

    #[test]
    fn a_minute_of_keying_spells_the_frame_that_says_that_minute() {
        let mut transmitter = set_to(AT_1930);

        let keyed = key(&mut transmitter, SET_AT, 60);

        assert_eq!(keyed, sent(&clock_frame(AT_1930)));
    }

    #[test]
    fn each_minute_says_its_own_time() {
        let mut transmitter = set_to(AT_1930);

        let keyed = key(&mut transmitter, SET_AT, 180);

        assert_eq!(keyed[..60], sent(&clock_frame(AT_1930)));
        assert_eq!(keyed[60..120], sent(&clock_frame(AT_1930 + 60)));
        assert_eq!(keyed[120..], sent(&clock_frame(AT_1930 + 120)));
    }

    #[test]
    fn a_time_in_the_middle_of_a_minute_goes_on_from_that_second_of_its_frame() {
        let mut transmitter = set_to(AT_1930 + 17);

        let keyed = key(&mut transmitter, SET_AT, 43 + 60);

        assert_eq!(keyed[..43], sent(&clock_frame(AT_1930)[17..]));
        assert_eq!(keyed[43..], sent(&clock_frame(AT_1930 + 60)));
    }

    /// The first clock is in the full carrier of its second 3 when the new time arrives. Second
    /// 10 of every frame is unused, a zero.
    #[test]
    fn a_new_time_cuts_the_second_in_progress_and_starts_its_own() {
        let mut transmitter = set_to(AT_1930);
        let arrives = SET_AT + 3 * SECOND + 900_000;
        assert!(transmitter.tick(arrives).carrier);
        let at_1945_10 = AT_1930 + 15 * 60 + 10;

        transmitter
            .handle(Command::Time(at_1945_10), arrives)
            .unwrap();

        assert_eq!(
            transmitter.tick(arrives),
            Keying {
                carrier: false,
                transmitting: true,
                until_micros: Some(arrives + 200_000),
            }
        );
        let keyed = key(&mut transmitter, arrives, 50 + 60);
        assert_eq!(keyed[..50], sent(&clock_frame(at_1945_10)[10..]));
        assert_eq!(keyed[50..], sent(&clock_frame(at_1945_10 + 60)));
    }

    #[test]
    fn off_gates_the_carrier_and_the_schedule_runs_on() {
        let mut transmitter = set_to(AT_1930);
        transmitter.handle(Command::Off, SET_AT).unwrap();

        let keyed = key(&mut transmitter, SET_AT, 120);

        assert_eq!(keyed[..60], gated(&clock_frame(AT_1930)));
        assert_eq!(keyed[60..], gated(&clock_frame(AT_1930 + 60)));
    }

    #[test]
    fn off_and_on_take_effect_in_the_middle_of_a_second() {
        let mut transmitter = set_to(AT_1930);
        let in_full_carrier = SET_AT + 900_000;
        let second_ends_at = Some(SET_AT + SECOND);
        assert!(transmitter.tick(in_full_carrier).carrier);

        transmitter.handle(Command::Off, in_full_carrier).unwrap();
        let off = transmitter.tick(in_full_carrier);
        transmitter.handle(Command::On, in_full_carrier).unwrap();
        let on = transmitter.tick(in_full_carrier);

        assert_eq!((off.carrier, off.until_micros), (false, second_ends_at));
        assert_eq!((on.carrier, on.until_micros), (true, second_ends_at));
    }

    #[test]
    fn on_does_not_fill_the_reduced_part_of_a_second() {
        let mut transmitter = set_to(AT_1930);
        transmitter.handle(Command::Off, SET_AT).unwrap();

        transmitter.handle(Command::On, SET_AT + 100_000).unwrap();

        assert!(!transmitter.tick(SET_AT + 100_000).carrier);
    }

    /// The LED shows `transmitting`. A load that switches in step with the time code could reach
    /// the receiver by another way than the coil, so it must hold through the reduced part of
    /// every second, markers and the turn of the minute included.
    #[test]
    fn a_frame_is_transmitted_through_both_parts_of_every_second() {
        let mut transmitter = set_to(AT_1930);

        for tenth in 0..1_200 {
            let keying = transmitter.tick(SET_AT + tenth * SECOND / 10);
            assert!(
                keying.transmitting,
                "{tenth} tenths of a second after the clock was set"
            );
        }
    }

    #[test]
    fn nothing_is_transmitted_while_the_carrier_is_off() {
        let mut transmitter = set_to(AT_1930);
        let in_reduced_carrier = SET_AT + 100_000;
        let in_full_carrier = SET_AT + 900_000;

        transmitter.handle(Command::Off, SET_AT).unwrap();
        let reduced = transmitter.tick(in_reduced_carrier);
        let full = transmitter.tick(in_full_carrier);
        transmitter.handle(Command::On, in_full_carrier).unwrap();
        let on_again = transmitter.tick(in_full_carrier);

        assert!(!reduced.transmitting);
        assert!(!full.transmitting);
        assert!(on_again.transmitting);
    }

    /// The example in the contract: the frame for 19:30, sent again in the 19:31 minute.
    #[test]
    fn a_custom_frame_takes_the_place_of_the_next_minutes_frame_once() {
        let mut transmitter = set_to(AT_1930);
        let before = key(&mut transmitter, SET_AT, 42);
        let custom = clock_frame(AT_1930);

        transmitter
            .handle(Command::Frame(custom), SET_AT + 42 * SECOND)
            .unwrap();
        let after = key(&mut transmitter, SET_AT + 42 * SECOND, 18 + 60 + 60);

        assert_eq!(before, sent(&clock_frame(AT_1930)[..42]));
        assert_eq!(after[..18], sent(&clock_frame(AT_1930)[42..]));
        assert_eq!(after[18..78], sent(&custom));
        assert_eq!(after[78..], sent(&clock_frame(AT_1930 + 120)));
    }

    #[test]
    fn a_custom_frame_stops_waiting_when_its_minute_starts() {
        let mut transmitter = set_to(AT_1930);
        key(&mut transmitter, SET_AT, 59);
        transmitter
            .handle(Command::Frame(ALL_ONES), SET_AT + 59 * SECOND)
            .unwrap();

        let last_second = key(&mut transmitter, SET_AT + 59 * SECOND, 1);
        let waiting = transmitter.status(SET_AT + 60 * SECOND - 1).custom_waiting;
        let first_second = key(&mut transmitter, SET_AT + 60 * SECOND, 1);
        let still_waiting = transmitter.status(SET_AT + 61 * SECOND).custom_waiting;

        assert_eq!(last_second, sent(&[Symbol::Marker]));
        assert!(waiting);
        assert_eq!(first_second, sent(&[Symbol::One]));
        assert!(!still_waiting);
    }

    #[test]
    fn a_later_frame_replaces_the_one_that_waits() {
        let mut transmitter = set_to(AT_1930);
        key(&mut transmitter, SET_AT, 10);

        transmitter
            .handle(Command::Frame(ALL_ONES), SET_AT + 10 * SECOND)
            .unwrap();
        transmitter
            .handle(Command::Frame(ALL_ZEROS), SET_AT + 10 * SECOND)
            .unwrap();
        let keyed = key(&mut transmitter, SET_AT + 10 * SECOND, 50 + 60 + 60);

        assert_eq!(keyed[50..110], sent(&ALL_ZEROS));
        assert_eq!(keyed[110..], sent(&clock_frame(AT_1930 + 120)));
    }

    #[test]
    fn a_frame_that_arrives_while_a_custom_frame_is_sent_waits_for_the_minute_after() {
        let mut transmitter = set_to(AT_1930);
        transmitter
            .handle(Command::Frame(ALL_ONES), SET_AT)
            .unwrap();
        let first = key(&mut transmitter, SET_AT, 60 + 10);

        let reply = transmitter.handle(Command::Frame(ALL_ZEROS), SET_AT + 70 * SECOND);
        let rest = key(&mut transmitter, SET_AT + 70 * SECOND, 50 + 60 + 60);

        assert!(reply.unwrap().custom_waiting);
        assert_eq!(first[60..], sent(&ALL_ONES[..10]));
        assert_eq!(rest[..50], sent(&ALL_ONES[10..]));
        assert_eq!(rest[50..110], sent(&ALL_ZEROS));
        assert_eq!(rest[110..], sent(&clock_frame(AT_1930 + 180)));
    }

    #[test]
    fn a_custom_frame_is_used_up_unsent_while_the_carrier_is_off() {
        let mut transmitter = set_to(AT_1930);
        transmitter.handle(Command::Off, SET_AT).unwrap();
        transmitter
            .handle(Command::Frame(ALL_ONES), SET_AT)
            .unwrap();

        let off = key(&mut transmitter, SET_AT, 60 + 60);
        let status = transmitter.handle(Command::On, SET_AT + 120 * SECOND);
        let on = key(&mut transmitter, SET_AT + 120 * SECOND, 60);

        assert_eq!(off[60..], gated(&ALL_ONES));
        assert!(!status.unwrap().custom_waiting);
        assert_eq!(on, sent(&clock_frame(AT_1930 + 120)));
    }

    /// The new time is the start of a minute, which is where a custom frame would start. It still
    /// waits, because that minute follows no other.
    #[test]
    fn a_new_time_leaves_a_waiting_custom_frame_waiting() {
        let mut transmitter = set_to(AT_1930);
        key(&mut transmitter, SET_AT, 5);
        let arrives = SET_AT + 5 * SECOND;
        transmitter
            .handle(Command::Frame(ALL_ONES), arrives)
            .unwrap();
        let at_1945 = AT_1930 + 15 * 60;

        let reply = transmitter.handle(Command::Time(at_1945), arrives);
        let keyed = key(&mut transmitter, arrives, 60 + 60 + 60);

        assert!(reply.unwrap().custom_waiting);
        assert_eq!(keyed[..60], sent(&clock_frame(at_1945)));
        assert_eq!(keyed[60..120], sent(&ALL_ONES));
        assert_eq!(keyed[120..], sent(&clock_frame(at_1945 + 120)));
    }

    /// The new time is in the minute whose custom frame is being sent, as it is when the host
    /// only corrects the clock by a few seconds.
    #[test]
    fn a_new_time_abandons_the_custom_frame_in_progress() {
        let mut transmitter = set_to(AT_1930);
        transmitter
            .handle(Command::Frame(ALL_ONES), SET_AT)
            .unwrap();
        let before = key(&mut transmitter, SET_AT, 60 + 20);
        let arrives = SET_AT + 80 * SECOND;
        let at_1931_25 = AT_1930 + 60 + 25;

        transmitter
            .handle(Command::Time(at_1931_25), arrives)
            .unwrap();
        let after = key(&mut transmitter, arrives, 35 + 60);

        assert_eq!(before[60..], sent(&ALL_ONES[..20]));
        assert_eq!(after[..35], sent(&clock_frame(at_1931_25)[25..]));
        assert_eq!(after[35..], sent(&clock_frame(at_1931_25 + 60)));
    }

    /// The clock is set to the last second of 2099, the marker in second 59.
    #[test]
    fn past_the_end_of_2099_no_frame_says_the_minute_and_there_is_no_carrier() {
        let last_second_of_2099 = 4_102_444_799;
        let mut transmitter = set_to(last_second_of_2099);

        let keyed = key(&mut transmitter, SET_AT, 1 + 120);

        assert_eq!(keyed[..1], sent(&[Symbol::Marker]));
        assert_eq!(keyed[1..], [Keyed::Silent; 120]);
        let status = transmitter.status(SET_AT + 121 * SECOND);
        assert_eq!(status.time, Some(last_second_of_2099 + 121));
        assert!(!transmitter.tick(SET_AT + 121 * SECOND).transmitting);
    }

    #[test]
    fn a_custom_frame_is_still_sent_past_the_end_of_2099() {
        let mut transmitter = set_to(4_102_444_799);
        transmitter
            .handle(Command::Frame(ALL_ONES), SET_AT)
            .unwrap();

        let keyed = key(&mut transmitter, SET_AT, 1 + 60 + 60);

        assert_eq!(keyed[1..61], sent(&ALL_ONES));
        assert_eq!(keyed[61..], [Keyed::Silent; 60]);
    }

    #[test]
    fn asking_twice_at_the_same_moment_gives_the_same_answer() {
        let mut transmitter = set_to(AT_1930 + 59);
        transmitter
            .handle(Command::Frame(ALL_ONES), SET_AT)
            .unwrap();
        key(&mut transmitter, SET_AT, 1);
        let minute_starts = SET_AT + SECOND;

        let first = transmitter.tick(minute_starts);
        let second = transmitter.tick(minute_starts);
        let keyed = key(&mut transmitter, minute_starts, 60 + 60);

        assert_eq!(first, second);
        assert_eq!(keyed[..60], sent(&ALL_ONES));
        assert_eq!(keyed[60..], sent(&clock_frame(AT_1930 + 120)));
    }

    #[test]
    fn a_moment_before_the_clock_was_set_is_taken_as_the_moment_it_was_set() {
        let mut transmitter = set_to(AT_1930);

        let keying = transmitter.tick(SET_AT - 1);

        assert_eq!(keying, transmitter.tick(SET_AT));
        assert_eq!(transmitter.status(SET_AT - 1).time, Some(AT_1930));
    }
}
