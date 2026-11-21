//! A command line from the host, and the reasons one is refused.

use core::fmt;

use wwvb_frame::{FRAME_SECONDS, Fields, Frame, Symbol};

/// What a command line asks for.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Command {
    /// `TIME <unix_seconds>`: this second starts now. Always within the years 2000 to 2099.
    Time(u64),
    /// `STATUS`: change nothing.
    Status,
    /// `ON`: switch the carrier on.
    On,
    /// `OFF`: switch the carrier off.
    Off,
    /// `FRAME <symbols>`: send this frame in place of the next minute's.
    Frame(Frame),
}

/// Why a command was refused. Written with `{}`, it is the reply line of the contract.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Error {
    /// The command word is not one of the five.
    UnknownCommand,
    /// Something follows `STATUS`, `ON`, or `OFF`.
    UnexpectedArgument,
    /// The argument of `TIME` is missing or holds anything but decimal digits.
    TimeNotANumber,
    /// The argument of `TIME` is a number outside the years 2000 to 2099.
    TimeOutOfRange,
    /// The argument of `FRAME` is not 60 symbols of `0`, `1`, or `M`.
    BadFrame,
    /// `FRAME` before the first `TIME`.
    ClockNotSet,
    /// The line passed its longest length.
    LineTooLong,
}

impl fmt::Display for Error {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Error::UnknownCommand => "ERR unknown command",
            Error::UnexpectedArgument => "ERR unexpected argument",
            Error::TimeNotANumber => "ERR time is not a number",
            Error::TimeOutOfRange => "ERR time is outside 2000 to 2099",
            Error::BadFrame => "ERR frame needs 60 symbols of 0, 1 or M",
            Error::ClockNotSet => "ERR clock not set",
            Error::LineTooLong => "ERR line too long",
        })
    }
}

/// Reads one command line, without its line end.
///
/// The command word is everything up to the first space. `TIME` and `FRAME` take what follows
/// that space as their argument, whole, so a second space or a trailing one is part of the
/// argument and makes it wrong.
pub fn parse(line: &[u8]) -> Result<Command, Error> {
    let (word, argument) = match line.iter().position(|&byte| byte == b' ') {
        Some(space) => (&line[..space], Some(&line[space + 1..])),
        None => (line, None),
    };
    match word {
        b"TIME" => parse_time(argument.unwrap_or_default()),
        b"FRAME" => parse_frame(argument.unwrap_or_default()),
        b"STATUS" => without_argument(Command::Status, argument),
        b"ON" => without_argument(Command::On, argument),
        b"OFF" => without_argument(Command::Off, argument),
        _ => Err(Error::UnknownCommand),
    }
}

fn without_argument(command: Command, argument: Option<&[u8]>) -> Result<Command, Error> {
    match argument {
        None => Ok(command),
        Some(_) => Err(Error::UnexpectedArgument),
    }
}

fn parse_time(argument: &[u8]) -> Result<Command, Error> {
    if argument.is_empty() || !argument.iter().all(u8::is_ascii_digit) {
        return Err(Error::TimeNotANumber);
    }
    // Digits only from here, so the sum fails only when the number is too large to hold, which
    // is outside the range too.
    let mut unix_seconds: u64 = 0;
    for digit in argument {
        unix_seconds = unix_seconds
            .checked_mul(10)
            .and_then(|tens| tens.checked_add(u64::from(digit - b'0')))
            .ok_or(Error::TimeOutOfRange)?;
    }
    // The range is the one a frame can say, so the frame crate decides it.
    match Fields::from_unix_seconds(unix_seconds) {
        Some(_) => Ok(Command::Time(unix_seconds)),
        None => Err(Error::TimeOutOfRange),
    }
}

fn parse_frame(argument: &[u8]) -> Result<Command, Error> {
    if argument.len() != FRAME_SECONDS {
        return Err(Error::BadFrame);
    }
    let mut frame = [Symbol::Zero; FRAME_SECONDS];
    for (symbol, byte) in frame.iter_mut().zip(argument) {
        *symbol = match byte {
            b'0' => Symbol::Zero,
            b'1' => Symbol::One,
            b'M' => Symbol::Marker,
            _ => return Err(Error::BadFrame),
        };
    }
    Ok(Command::Frame(frame))
}

#[cfg(test)]
mod tests {
    use wwvb_frame::encode;

    use super::*;

    /// The frame for 2026-12-10T19:30Z, as the argument of `FRAME`. It is the host's vector, and
    /// the example in the contract.
    const VECTOR: &str = "M01100000M000101001M001100100M010000101M000000010M011000000M";

    fn parse_str(line: &str) -> Result<Command, Error> {
        parse(line.as_bytes())
    }

    #[test]
    fn parse_reads_the_three_commands_that_take_no_argument() {
        assert_eq!(parse_str("STATUS"), Ok(Command::Status));
        assert_eq!(parse_str("ON"), Ok(Command::On));
        assert_eq!(parse_str("OFF"), Ok(Command::Off));
    }

    #[test]
    fn parse_reads_a_time_in_the_years_2000_to_2099() {
        let cases = [
            ("TIME 1796931000", 1_796_931_000),
            ("TIME 946684800", 946_684_800),
            ("TIME 4102444799", 4_102_444_799),
            ("TIME 0001796931000", 1_796_931_000),
        ];

        for (line, unix_seconds) in cases {
            assert_eq!(parse_str(line), Ok(Command::Time(unix_seconds)), "{line}");
        }
    }

    #[test]
    fn parse_reads_a_frame_as_the_encoder_writes_it() {
        let fields = Fields::from_unix_seconds(1_796_931_000).unwrap();

        let command = parse_str(&format!("FRAME {VECTOR}"));

        assert_eq!(command, Ok(Command::Frame(encode(&fields))));
    }

    #[test]
    fn parse_takes_a_frame_that_no_encoder_would_write() {
        let all_markers = "M".repeat(60);

        let command = parse_str(&format!("FRAME {all_markers}"));

        assert_eq!(command, Ok(Command::Frame([Symbol::Marker; 60])));
    }

    #[test]
    fn parse_refuses_a_word_that_is_not_a_command() {
        let lines = [
            "status",
            "Status",
            "STATUSX",
            "STAT",
            " STATUS",
            "time 1796931000",
            "TIMES 1796931000",
            "HELLO",
            "",
            " ",
        ];

        for line in lines {
            assert_eq!(parse_str(line), Err(Error::UnknownCommand), "{line:?}");
        }
    }

    #[test]
    fn parse_refuses_anything_after_status_on_and_off() {
        let lines = ["STATUS ", "STATUS now", "ON 1", "ON ", "OFF ", "OFF  "];

        for line in lines {
            assert_eq!(parse_str(line), Err(Error::UnexpectedArgument), "{line:?}");
        }
    }

    #[test]
    fn parse_refuses_a_time_that_is_not_decimal_digits() {
        let lines = [
            "TIME",
            "TIME ",
            "TIME +1796931000",
            "TIME -1796931000",
            "TIME 1796931000.5",
            "TIME 17969310oo",
            "TIME  1796931000",
            "TIME 1796931000 ",
            "TIME 1796931000 1",
            "TIME 0x6B1B2C18",
        ];

        for line in lines {
            assert_eq!(parse_str(line), Err(Error::TimeNotANumber), "{line:?}");
        }
    }

    #[test]
    fn parse_refuses_a_time_that_is_not_decimal_digits_in_bytes_above_ascii() {
        // A digit with its top bit set, and the first byte of a two-byte character.
        assert_eq!(parse(b"TIME 17\xB96931000"), Err(Error::TimeNotANumber));
        assert_eq!(parse(b"TIME \xC3\xA9"), Err(Error::TimeNotANumber));
    }

    #[test]
    fn parse_refuses_a_time_outside_the_years_2000_to_2099() {
        let lines = [
            "TIME 0",
            "TIME 946684799",
            "TIME 4102444800",
            "TIME 18446744073709551615",
            "TIME 18446744073709551616",
            // 2^64 more than a second in 2026: a sum that wrapped would be in the range.
            "TIME 18446744075506482616",
            "TIME 99999999999999999999999999999999999999999999999999999999999",
        ];

        for line in lines {
            assert_eq!(parse_str(line), Err(Error::TimeOutOfRange), "{line:?}");
        }
    }

    #[test]
    fn parse_refuses_a_frame_that_is_not_60_symbols_of_0_1_or_m() {
        let lines = [
            "FRAME".to_string(),
            "FRAME ".to_string(),
            format!("FRAME {}", &VECTOR[..59]),
            format!("FRAME {VECTOR}0"),
            format!("FRAME {VECTOR} "),
            format!("FRAME  {}", &VECTOR[..59]),
            format!("FRAME {}", VECTOR.replacen('M', "m", 1)),
            format!("FRAME {}", VECTOR.replacen('0', "2", 1)),
            format!("FRAME {}", VECTOR.replacen('1', " ", 1)),
            format!("FRAME {}", VECTOR.replacen('0', "O", 1)),
        ];

        for line in lines {
            assert_eq!(parse_str(&line), Err(Error::BadFrame), "{line:?}");
        }
    }

    #[test]
    fn an_error_is_written_as_the_reply_line_of_the_contract() {
        let cases = [
            (Error::UnknownCommand, "ERR unknown command"),
            (Error::UnexpectedArgument, "ERR unexpected argument"),
            (Error::TimeNotANumber, "ERR time is not a number"),
            (Error::TimeOutOfRange, "ERR time is outside 2000 to 2099"),
            (Error::BadFrame, "ERR frame needs 60 symbols of 0, 1 or M"),
            (Error::ClockNotSet, "ERR clock not set"),
            (Error::LineTooLong, "ERR line too long"),
        ];

        for (error, reply) in cases {
            assert_eq!(error.to_string(), reply);
        }
    }
}
