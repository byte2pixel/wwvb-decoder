//! The host's bytes, gathered into command lines.

use crate::command::{Command, Error, parse};

/// The longest command line, without its line end: `FRAME`, a space, and 60 symbols.
pub const MAX_LINE_LEN: usize = 66;

/// Gathers bytes until a line ends, then reads the line as a command. A session starts with a new
/// one, which is how an unfinished line from an earlier session is forgotten.
pub struct LineBuffer {
    bytes: [u8; MAX_LINE_LEN],
    len: usize,
    /// The line in progress passed `MAX_LINE_LEN`. Its bytes past that length were dropped.
    too_long: bool,
}

impl LineBuffer {
    pub const fn new() -> Self {
        Self {
            bytes: [0; MAX_LINE_LEN],
            len: 0,
            too_long: false,
        }
    }

    /// Takes the next byte from the host. When the byte ends a line that is not empty, returns
    /// what the line said. A line feed and a carriage return both end a line, so the second of a
    /// carriage return and line feed pair ends an empty line, which is ignored.
    pub fn push(&mut self, byte: u8) -> Option<Result<Command, Error>> {
        if byte != b'\n' && byte != b'\r' {
            if self.len < MAX_LINE_LEN {
                self.bytes[self.len] = byte;
                self.len += 1;
            } else {
                self.too_long = true;
            }
            return None;
        }

        let len = core::mem::take(&mut self.len);
        if core::mem::take(&mut self.too_long) {
            return Some(Err(Error::LineTooLong));
        }
        if len == 0 {
            return None;
        }
        Some(parse(&self.bytes[..len]))
    }
}

impl Default for LineBuffer {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use wwvb_frame::Symbol;

    use super::*;

    /// Pushes every byte and collects what the lines said, in order.
    fn feed(buffer: &mut LineBuffer, bytes: &[u8]) -> Vec<Result<Command, Error>> {
        bytes.iter().filter_map(|&byte| buffer.push(byte)).collect()
    }

    /// `FRAME` and this many markers, without a line end.
    fn frame_line(symbols: usize) -> Vec<u8> {
        format!("FRAME {}", "M".repeat(symbols)).into_bytes()
    }

    #[test]
    fn a_line_feed_ends_a_line() {
        let mut buffer = LineBuffer::new();

        assert_eq!(feed(&mut buffer, b"STATUS\n"), [Ok(Command::Status)]);
    }

    #[test]
    fn a_carriage_return_ends_a_line() {
        let mut buffer = LineBuffer::new();

        assert_eq!(feed(&mut buffer, b"STATUS\r"), [Ok(Command::Status)]);
    }

    #[test]
    fn a_carriage_return_and_line_feed_end_one_line_not_two() {
        let mut buffer = LineBuffer::new();

        assert_eq!(
            feed(&mut buffer, b"ON\r\nOFF\r\n"),
            [Ok(Command::On), Ok(Command::Off)]
        );
    }

    #[test]
    fn nothing_is_said_before_the_line_ends() {
        let mut buffer = LineBuffer::new();

        assert_eq!(feed(&mut buffer, b"TIME 1796931000"), []);
        assert_eq!(buffer.push(b'\n'), Some(Ok(Command::Time(1_796_931_000))));
    }

    #[test]
    fn empty_lines_are_ignored() {
        let mut buffer = LineBuffer::new();

        assert_eq!(feed(&mut buffer, b"\n\n\r\r\n\n\r"), []);
        assert_eq!(feed(&mut buffer, b"\n\nON\n\n"), [Ok(Command::On)]);
    }

    #[test]
    fn each_line_is_read_by_itself() {
        let mut buffer = LineBuffer::new();

        let said = feed(&mut buffer, b"OFF\nstatus\nTIME 1796931000\nON\n");

        assert_eq!(
            said,
            [
                Ok(Command::Off),
                Err(Error::UnknownCommand),
                Ok(Command::Time(1_796_931_000)),
                Ok(Command::On),
            ]
        );
    }

    #[test]
    fn the_longest_line_is_a_frame_command() {
        let mut buffer = LineBuffer::new();
        let mut line = frame_line(60);
        assert_eq!(line.len(), MAX_LINE_LEN);
        line.push(b'\n');

        let said = feed(&mut buffer, &line);

        assert_eq!(said, [Ok(Command::Frame([Symbol::Marker; 60]))]);
    }

    #[test]
    fn a_line_one_byte_too_long_is_refused_though_it_starts_as_a_good_command() {
        let mut buffer = LineBuffer::new();
        let mut line = frame_line(61);
        line.push(b'\n');

        let said = feed(&mut buffer, &line);

        assert_eq!(said, [Err(Error::LineTooLong)]);
    }

    #[test]
    fn a_line_too_long_is_refused_once_at_its_end_and_the_next_line_is_read() {
        let mut buffer = LineBuffer::new();

        assert_eq!(feed(&mut buffer, &[b'X'; 1_000]), []);
        assert_eq!(
            feed(&mut buffer, b"\r\nSTATUS\n"),
            [Err(Error::LineTooLong), Ok(Command::Status)]
        );
    }

    #[test]
    fn a_line_too_long_does_not_make_the_next_long_line_too_long() {
        let mut buffer = LineBuffer::new();
        let mut lines = frame_line(61);
        lines.push(b'\n');
        lines.extend(frame_line(60));
        lines.push(b'\n');

        let said = feed(&mut buffer, &lines);

        assert_eq!(
            said,
            [
                Err(Error::LineTooLong),
                Ok(Command::Frame([Symbol::Marker; 60]))
            ]
        );
    }
}
