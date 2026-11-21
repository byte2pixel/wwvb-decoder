//! The WWVB frame: which second carries what, `encode`, which builds a frame from its fields, and
//! `Fields::from_unix_seconds`, which gives the fields for a time. A port of `WwvbFrameLayout`
//! and `WwvbFrameEncoder` in the host's Core, for the firmware that transmits frames.

#![cfg_attr(not(test), no_std)]

/// A frame has one symbol for each of its 60 seconds, 0 to 59.
pub const FRAME_SECONDS: usize = 60;

/// One bit of a field. A field's table lists its bits from the largest weight down.
pub struct LayoutBit {
    /// The second of the frame the bit is sent in.
    pub second: usize,
    /// What the bit adds to the field's value when it is a 1.
    pub weight: u16,
}

/// The marker seconds: Pr at 0, P1 to P5 at 9 to 49, and P0 at 59.
pub const MARKER_SECONDS: [usize; 7] = [0, 9, 19, 29, 39, 49, 59];

/// Seconds that carry no information; always sent as 0.
pub const UNUSED_SECONDS: [usize; 11] = [4, 10, 11, 14, 20, 21, 24, 34, 35, 44, 54];

// Keeps a table to one line for each bit.
const fn bit(second: usize, weight: u16) -> LayoutBit {
    LayoutBit { second, weight }
}

/// The minute, 0 to 59: the tens in seconds 1 to 3, the units in 5 to 8.
pub const MINUTE_BITS: [LayoutBit; 7] = [
    bit(1, 40),
    bit(2, 20),
    bit(3, 10),
    bit(5, 8),
    bit(6, 4),
    bit(7, 2),
    bit(8, 1),
];

/// The hour in UTC, 0 to 23: the tens in seconds 12 and 13, the units in 15 to 18.
pub const HOUR_BITS: [LayoutBit; 6] = [
    bit(12, 20),
    bit(13, 10),
    bit(15, 8),
    bit(16, 4),
    bit(17, 2),
    bit(18, 1),
];

/// The day of the year, 1 to 366: the hundreds in seconds 22 and 23, the tens in 25 to 28, the
/// units in 30 to 33.
pub const DAY_OF_YEAR_BITS: [LayoutBit; 10] = [
    bit(22, 200),
    bit(23, 100),
    bit(25, 80),
    bit(26, 40),
    bit(27, 20),
    bit(28, 10),
    bit(30, 8),
    bit(31, 4),
    bit(32, 2),
    bit(33, 1),
];

/// The last two digits of the year: the tens in seconds 45 to 48, the units in 50 to 53.
pub const YEAR_BITS: [LayoutBit; 8] = [
    bit(45, 80),
    bit(46, 40),
    bit(47, 20),
    bit(48, 10),
    bit(50, 8),
    bit(51, 4),
    bit(52, 2),
    bit(53, 1),
];

/// The sign of the UT1 correction: 1 0 1 for plus, 0 1 0 for minus.
pub const UT1_SIGN_SECONDS: [usize; 3] = [36, 37, 38];

/// The UT1 correction in tenths of a second, 0 to 9.
pub const UT1_CORRECTION_BITS: [LayoutBit; 4] = [bit(40, 8), bit(41, 4), bit(42, 2), bit(43, 1)];

/// 1 in a leap year.
pub const LEAP_YEAR_SECOND: usize = 55;

/// 1 when a leap second will be inserted at the end of the month.
pub const LEAP_SECOND_PENDING_SECOND: usize = 56;

/// The daylight saving time status: 0 0 for standard time, 1 1 for daylight time, 1 0 when
/// daylight time begins today, 0 1 when it ends today.
pub const DST_SECONDS: [usize; 2] = [57, 58];

/// What one second of a frame carries. The station sends it as the time the carrier stays reduced
/// at the start of the second.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Symbol {
    /// A 0 bit: 0.2 s of reduced carrier.
    Zero,
    /// A 1 bit: 0.5 s of reduced carrier.
    One,
    /// A marker, which carries no data: 0.8 s of reduced carrier.
    Marker,
}

/// The 60 symbols of one frame; the index is the second.
pub type Frame = [Symbol; FRAME_SECONDS];

/// The sign of the UT1 correction.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Ut1Sign {
    /// Sent as 1 0 1.
    Plus,
    /// Sent as 0 1 0.
    Minus,
}

/// The daylight saving time status.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum DstStatus {
    /// Standard time, sent as 0 0.
    Standard,
    /// Daylight time, sent as 1 1.
    Dst,
    /// Daylight time begins today, sent as 1 0.
    BeginsToday,
    /// Daylight time ends today, sent as 0 1.
    EndsToday,
}

/// What one frame says: the minute it is sent in, as UTC, and the flags that go with it. The
/// members are those of `DecodedFrame` in the host's Core. `encode` does not check the ranges.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Fields {
    /// 0 to 59.
    pub minute: u8,
    /// The hour in UTC, 0 to 23.
    pub hour: u8,
    /// 1 to 366; January 1 is day 1.
    pub day_of_year: u16,
    /// The last two digits of the year, 0 to 99.
    pub year: u8,
    /// The sign of the UT1 correction.
    pub ut1_sign: Ut1Sign,
    /// The size of the UT1 correction in tenths of a second, 0 to 9.
    pub ut1_tenths: u8,
    /// Whether the year is a leap year. `encode` sends what it is given; it does not work this
    /// out from `year`.
    pub leap_year: bool,
    /// Whether a leap second will be inserted at the end of the month.
    pub leap_second_pending: bool,
    /// The daylight saving time status.
    pub dst: DstStatus,
}

impl Fields {
    /// The fields of the minute that holds this second, counted from 1970-01-01T00:00:00Z.
    /// `None` outside the years 2000 to 2099, which are the years two digits can say. The time
    /// does not give the rest, so the UT1 correction is plus 0.0, no leap second is pending, and
    /// the status is standard time.
    pub fn from_unix_seconds(unix_seconds: u64) -> Option<Fields> {
        // From 2000-01-01T00:00:00Z up to, but not including, 2100-01-01T00:00:00Z.
        if !(946_684_800..4_102_444_800).contains(&unix_seconds) {
            return None;
        }
        let days = unix_seconds / 86_400;
        let seconds_of_day = unix_seconds % 86_400;
        let mut year = 1970u64;
        let mut remaining_days = days;
        // Takes whole years off the days since 1970. Every fourth year is a leap year here: the
        // only century year from 1970 to 2099 is 2000, and 2000 is one.
        loop {
            let days_in_year = if year.is_multiple_of(4) { 366 } else { 365 };
            if remaining_days < days_in_year {
                break;
            }
            remaining_days -= days_in_year;
            year += 1;
        }
        Some(Fields {
            minute: (seconds_of_day / 60 % 60) as u8,
            hour: (seconds_of_day / 3_600) as u8,
            day_of_year: remaining_days as u16 + 1,
            year: (year % 100) as u8,
            ut1_sign: Ut1Sign::Plus,
            ut1_tenths: 0,
            leap_year: year.is_multiple_of(4),
            leap_second_pending: false,
            dst: DstStatus::Standard,
        })
    }
}

/// Builds the frame that says these fields: a marker in each marker second, a one for each bit
/// that is set, and a zero in every other second.
pub fn encode(fields: &Fields) -> Frame {
    let mut frame = [Symbol::Zero; FRAME_SECONDS];
    for &second in &MARKER_SECONDS {
        frame[second] = Symbol::Marker;
    }
    for (bits, value) in [
        (&MINUTE_BITS[..], fields.minute as u16),
        (&HOUR_BITS[..], fields.hour as u16),
        (&DAY_OF_YEAR_BITS[..], fields.day_of_year),
        (&YEAR_BITS[..], fields.year as u16),
        (&UT1_CORRECTION_BITS[..], fields.ut1_tenths as u16),
    ] {
        let mut remaining = value;
        for bit in bits {
            if remaining >= bit.weight {
                frame[bit.second] = Symbol::One;
                remaining -= bit.weight;
            }
        }
    }
    for (second, value) in UT1_SIGN_SECONDS.iter().zip(match fields.ut1_sign {
        Ut1Sign::Plus => [true, false, true],
        Ut1Sign::Minus => [false, true, false],
    }) {
        if value {
            frame[*second] = Symbol::One;
        }
    }
    for (second, value) in [
        (LEAP_YEAR_SECOND, fields.leap_year),
        (LEAP_SECOND_PENDING_SECOND, fields.leap_second_pending),
    ] {
        if value {
            frame[second] = Symbol::One;
        }
    }
    for (second, value) in DST_SECONDS.iter().zip(match fields.dst {
        DstStatus::Standard => [false, false],
        DstStatus::Dst => [true, true],
        DstStatus::BeginsToday => [true, false],
        DstStatus::EndsToday => [false, true],
    }) {
        if value {
            frame[*second] = Symbol::One;
        }
    }
    frame
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Every second this crate's tables name, with its role as the host names it and its bit
    /// weight, in order of the second.
    fn roles_and_weights() -> Vec<(usize, &'static str, Option<u16>)> {
        let mut seconds = Vec::new();
        let plain = [
            ("marker", &MARKER_SECONDS[..]),
            ("unused", &UNUSED_SECONDS[..]),
            ("ut1Sign", &UT1_SIGN_SECONDS[..]),
            ("leapYear", &[LEAP_YEAR_SECOND][..]),
            ("leapSecondPending", &[LEAP_SECOND_PENDING_SECOND][..]),
            ("dst", &DST_SECONDS[..]),
        ];
        for (role, list) in plain {
            seconds.extend(list.iter().map(|&second| (second, role, None)));
        }
        let weighted = [
            ("minute", &MINUTE_BITS[..]),
            ("hour", &HOUR_BITS[..]),
            ("dayOfYear", &DAY_OF_YEAR_BITS[..]),
            ("ut1Correction", &UT1_CORRECTION_BITS[..]),
            ("year", &YEAR_BITS[..]),
        ];
        for (role, bits) in weighted {
            seconds.extend(bits.iter().map(|bit| (bit.second, role, Some(bit.weight))));
        }
        seconds.sort();
        seconds
    }

    /// The host's `frameLayout` message example. The host's own tests hold `WwvbFrameLayout` to
    /// this file, so this test holds the two layouts to each other.
    #[test]
    fn layout_matches_the_hosts_frame_layout_example() {
        let example: serde_json::Value = serde_json::from_str(include_str!(
            "../../../docs/design/message-examples/frameLayout.json"
        ))
        .unwrap();
        let roles = example["payload"]["roles"].as_array().unwrap();
        let weights = example["payload"]["weights"].as_array().unwrap();
        assert_eq!((roles.len(), weights.len()), (FRAME_SECONDS, FRAME_SECONDS));
        let expected: Vec<_> = (0..FRAME_SECONDS)
            .map(|second| {
                let weight = weights[second].as_u64().map(|weight| weight as u16);
                (second, roles[second].as_str().unwrap(), weight)
            })
            .collect();

        assert_eq!(roles_and_weights(), expected);
    }

    /// Every field at zero or off, with the plus sign that a UT1 correction of 0.0 has.
    const BASE: Fields = Fields {
        minute: 0,
        hour: 0,
        day_of_year: 0,
        year: 0,
        ut1_sign: Ut1Sign::Plus,
        ut1_tenths: 0,
        leap_year: false,
        leap_second_pending: false,
        dst: DstStatus::Standard,
    };

    /// The ones of `BASE`: the plus sign is 1 0 1 in seconds 36 to 38.
    const PLUS_SIGN: [usize; 2] = [36, 38];

    /// `BASE` with the time set.
    fn time(minute: u8, hour: u8, day_of_year: u16, year: u8) -> Fields {
        Fields {
            minute,
            hour,
            day_of_year,
            year,
            ..BASE
        }
    }

    /// `BASE` with one thing changed.
    fn with(change: impl FnOnce(&mut Fields)) -> Fields {
        let mut fields = BASE;
        change(&mut fields);
        fields
    }

    /// Each row changes one thing in `BASE` and lists the ones that adds to the plus sign's.
    fn assert_ones(cases: &[(&str, Fields, &[usize])]) {
        for (name, fields, added) in cases {
            let mut expected = [*added, &PLUS_SIGN[..]].concat();
            expected.sort();

            assert_eq!(seconds_of(&encode(fields), Symbol::One), expected, "{name}");
        }
    }

    /// The seconds of a frame that hold this symbol.
    fn seconds_of(frame: &Frame, symbol: Symbol) -> Vec<usize> {
        (0..FRAME_SECONDS)
            .filter(|&second| frame[second] == symbol)
            .collect()
    }

    #[test]
    fn encode_puts_a_marker_in_the_seven_marker_seconds_and_nowhere_else() {
        let frame = encode(&time(59, 23, 366, 99));

        assert_eq!(
            seconds_of(&frame, Symbol::Marker),
            [0, 9, 19, 29, 39, 49, 59]
        );
    }

    #[test]
    fn encode_writes_each_time_field_as_the_weights_that_add_up_to_it() {
        let cases: [(&str, Fields, &[usize]); 11] = [
            ("minute 30", time(30, 0, 0, 0), &[2, 3]),       // 20 + 10
            ("minute 59", time(59, 0, 0, 0), &[1, 3, 5, 8]), // 40 + 10 + 8 + 1
            ("minute 7", time(7, 0, 0, 0), &[6, 7, 8]),      // 4 + 2 + 1
            ("hour 19", time(0, 19, 0, 0), &[13, 15, 18]),   // 10 + 8 + 1
            ("hour 23", time(0, 23, 0, 0), &[12, 17, 18]),   // 20 + 2 + 1
            ("day 1", time(0, 0, 1, 0), &[33]),
            ("day 199", time(0, 0, 199, 0), &[23, 25, 28, 30, 33]), // 100 + 80 + 10 + 8 + 1
            ("day 344", time(0, 0, 344, 0), &[22, 23, 26, 31]),     // 200 + 100 + 40 + 4
            ("day 366", time(0, 0, 366, 0), &[22, 23, 26, 27, 31, 32]), // 200+100+40+20+4+2
            ("year 26", time(0, 0, 0, 26), &[47, 51, 52]),          // 20 + 4 + 2
            ("year 99", time(0, 0, 0, 99), &[45, 48, 50, 53]),      // 80 + 10 + 8 + 1
        ];

        assert_ones(&cases);
    }

    #[test]
    fn encode_writes_the_ut1_correction_the_two_flags_and_the_dst_status() {
        use DstStatus::{BeginsToday, Dst, EndsToday};
        let cases: [(&str, Fields, &[usize]); 7] = [
            ("UT1 0.3", with(|f| f.ut1_tenths = 3), &[42, 43]), // 2 + 1
            ("UT1 0.9", with(|f| f.ut1_tenths = 9), &[40, 43]), // 8 + 1
            ("leap year", with(|f| f.leap_year = true), &[55]),
            ("leap second", with(|f| f.leap_second_pending = true), &[56]),
            ("daylight time", with(|f| f.dst = Dst), &[57, 58]),
            ("begins today", with(|f| f.dst = BeginsToday), &[57]),
            ("ends today", with(|f| f.dst = EndsToday), &[58]),
        ];

        assert_ones(&cases);
    }

    #[test]
    fn encode_writes_the_minus_sign_as_0_1_0() {
        let frame = encode(&with(|f| f.ut1_sign = Ut1Sign::Minus));

        assert_eq!(seconds_of(&frame, Symbol::One), [37]);
    }

    /// The vector of `Encode_KnownVector_2026_12_10T1930Z` in the host's tests.
    #[test]
    fn encode_matches_the_hosts_vector_for_2026_12_10_at_19_30_utc() {
        use Symbol::{Marker as M, One as O, Zero as Z};
        #[rustfmt::skip]
        let expected: Frame = [
         // 0  1  2  3  4  5  6  7  8  9
            M, Z, O, O, Z, Z, Z, Z, Z, M, //   0-9: minute 30 = 20 + 10
            Z, Z, Z, O, Z, O, Z, Z, O, M, // 10-19: hour 19 = 10 + 8 + 1
            Z, Z, O, O, Z, Z, O, Z, Z, M, // 20-29: day of year 344 = 200 + 100 + 40 + ...
            Z, O, Z, Z, Z, Z, O, Z, O, M, // 30-39: ... + 4, UT1 plus = 1 0 1
            Z, Z, Z, Z, Z, Z, Z, O, Z, M, // 40-49: UT1 0.0, year 26 = 20 + ...
            Z, O, O, Z, Z, Z, Z, Z, Z, M, // 50-59: ... + 4 + 2, no flags, standard time
        ];

        let frame = encode(&time(30, 19, 344, 26));

        assert_eq!(frame, expected);
    }

    /// `time`, in a leap year.
    fn leap(minute: u8, hour: u8, day_of_year: u16, year: u8) -> Fields {
        Fields {
            leap_year: true,
            ..time(minute, hour, day_of_year, year)
        }
    }

    /// Each row's seconds are what .NET's `ToUnixTimeSeconds` gives for the time in its name.
    #[test]
    fn from_unix_seconds_gives_the_fields_of_the_minute_that_holds_the_second() {
        let cases: [(&str, u64, Fields); 9] = [
            ("2026-12-10T19:30:00Z", 1_796_931_000, time(30, 19, 344, 26)),
            ("2026-12-10T19:30:59Z", 1_796_931_059, time(30, 19, 344, 26)),
            ("2026-12-10T19:31:00Z", 1_796_931_060, time(31, 19, 344, 26)),
            ("2000-01-01T00:00:00Z", 946_684_800, leap(0, 0, 1, 0)),
            ("2000-12-31T23:59:59Z", 978_307_199, leap(59, 23, 366, 0)),
            ("2001-01-01T00:00:00Z", 978_307_200, time(0, 0, 1, 1)),
            ("2024-02-29T12:00:00Z", 1_709_208_000, leap(0, 12, 60, 24)),
            ("2026-03-01T00:00:00Z", 1_772_323_200, time(0, 0, 60, 26)),
            ("2099-12-31T23:59:59Z", 4_102_444_799, time(59, 23, 365, 99)),
        ];

        for (name, seconds, expected) in cases {
            assert_eq!(Fields::from_unix_seconds(seconds), Some(expected), "{name}");
        }
    }

    #[test]
    fn from_unix_seconds_gives_nothing_outside_the_years_2000_to_2099() {
        let cases: [(&str, u64); 4] = [
            ("1970-01-01T00:00:00Z", 0),
            ("1999-12-31T23:59:59Z", 946_684_799),
            ("2100-01-01T00:00:00Z", 4_102_444_800),
            ("the largest u64", u64::MAX),
        ];

        for (name, seconds) in cases {
            assert_eq!(Fields::from_unix_seconds(seconds), None, "{name}");
        }
    }
}
