//! WWVB desk-top transmitter firmware for the Raspberry Pi Pico 2 (RP2350).
//!
//! Sends a 60 kHz carrier, keyed with WWVB frames, from one GPIO into a coil beside the
//! receiver's antenna. The host gives it the time and a few commands over USB CDC serial. The
//! contract is the section "Transmitter commands" of `docs/design/serial-protocol.md`.
//!
//! Every decision is made in `wwvb-transmitter-core`, which is tested on the PC. This file is the
//! hardware around it:
//!
//! - `usb_task` runs the USB device.
//! - `control_task` watches DTR.
//! - `command_task` waits for DTR, writes `# session` and the banner, then reads command lines
//!   and writes one reply for each.
//! - `keying_task` asks the core what the carrier does and until when, sets the carrier and the
//!   LED, and sleeps until that moment or the next command.

#![no_std]
#![no_main]

use core::cell::RefCell;
use core::convert::Infallible;
use core::fmt::{self, Write as _};
use core::sync::atomic::{AtomicU32, Ordering};
use core::task::Poll;

use embassy_executor::Spawner;
use embassy_futures::poll_once;
use embassy_futures::select::{Either, select};
use embassy_rp::bind_interrupts;
use embassy_rp::clocks::clk_sys_freq;
use embassy_rp::gpio::{Level, Output, Pin as _};
use embassy_rp::peripherals::USB;
use embassy_rp::pwm::{self, Pwm, SetDutyCycle as _};
use embassy_rp::usb::{Driver, InterruptHandler};
use embassy_sync::blocking_mutex::Mutex;
use embassy_sync::blocking_mutex::raw::CriticalSectionRawMutex;
use embassy_sync::signal::Signal;
use embassy_time::{Instant, Timer};
use embassy_usb::class::cdc_acm::{CdcAcmClass, ControlChanged, Receiver, Sender, State};
use embassy_usb::{Builder, Config, UsbDevice};
use panic_halt as _;
use static_cell::StaticCell;
use wwvb_transmitter_core::{Error, LineBuffer, MAX_REPLY_LEN, Status, Transmitter};

// Shown by `picotool info`.
#[unsafe(link_section = ".bi_entries")]
#[used]
pub static PICOTOOL_ENTRIES: [embassy_rp::binary_info::EntryAddr; 4] = [
    embassy_rp::binary_info::rp_program_name!(c"wwvb-transmitter"),
    embassy_rp::binary_info::rp_program_description!(c"WWVB desk-top transmitter"),
    embassy_rp::binary_info::rp_cargo_version!(),
    embassy_rp::binary_info::rp_program_build_attribute!(),
];

bind_interrupts!(struct Irqs {
    USBCTRL_IRQ => InterruptHandler<USB>;
});

type UsbDriver = Driver<'static, USB>;

const CARRIER_HZ: u32 = 60_000;

/// The largest packet a full-speed bulk endpoint carries, which is the most one read returns.
const MAX_PACKET_LEN: usize = 64;

/// Staying one under a full packet keeps every packet short, so the host hands it over at once
/// instead of waiting for a zero-length packet to end the transfer.
const PACKET_LEN: usize = 63;

/// `# session` and the banner with the longest possible pin number. They go out together.
const SESSION_LINES_LEN: usize = "# session\n".len()
    + "# wwvb-transmitter ".len()
    + env!("CARGO_PKG_VERSION").len()
    + " pin=255\n".len();

// A line is never split, so the session's two lines and the longest reply must each fit a packet.
const _: () = assert!(SESSION_LINES_LEN <= PACKET_LEN && MAX_REPLY_LEN < PACKET_LEN);

/// The clock, the carrier switch, and the custom frame, shared by `command_task` and
/// `keying_task`. Each use takes microseconds, so a critical section is enough.
static TRANSMITTER: Mutex<CriticalSectionRawMutex, RefCell<Transmitter>> =
    Mutex::new(RefCell::new(Transmitter::new()));

/// Raised after every command, so that `keying_task` asks at once what the carrier does now.
static COMMANDED: Signal<CriticalSectionRawMutex, ()> = Signal::new();

/// Counts DTR transitions; odd while DTR is asserted. A session that sees it change knows its
/// host went away, even if DTR is asserted again by the time it looks.
static DTR_EPOCH: AtomicU32 = AtomicU32::new(0);

/// Raised by `control_task` on every DTR transition.
static DTR_CHANGED: Signal<CriticalSectionRawMutex, ()> = Signal::new();

#[embassy_executor::main(
    executor = "embassy_rp::executor::Executor",
    entry = "cortex_m_rt::entry"
)]
async fn main(spawner: Spawner) {
    let p = embassy_rp::init(Default::default());

    // GP23 is the regulator's PS pin. High forces PWM mode, whose ripple is lower than the
    // default power-save mode's, so this board adds less noise beside the receiver. Dropping an
    // `Output` releases the pin, so it is leaked.
    core::mem::forget(Output::new(p.PIN_23, Level::High));

    // Steadily on while a frame is transmitted: from the first `TIME`, except after `OFF`. It
    // does not follow the keying. On the receiver's Pico a blinking LED reached the receiver
    // through the supply, and one that blinks in step with the time code here could pass for the
    // signal from the coil.
    let led = Output::new(p.PIN_25, Level::Low);

    // One PWM slice counts the system clock: 150 MHz over 60 kHz is 2,500 counts, with no
    // remainder. The compare value starts at 0, so the pin is low until `keying_task` says
    // otherwise.
    let pin_number = p.PIN_16.pin();
    let period = clk_sys_freq() / CARRIER_HZ;
    let mut config = pwm::Config::default();
    config.top = (period - 1) as u16;
    let carrier = Carrier {
        pwm: Pwm::new_output_a(p.PWM_SLICE0, p.PIN_16, config),
        full_duty: (period / 2) as u16,
    };

    let mut config = Config::new(0xC0DE, 0xCAFF);
    config.manufacturer = Some("wwvb-decoder");
    config.product = Some("wwvb-transmitter");
    config.max_power = 100;
    config.max_packet_size_0 = 64;
    // Miscellaneous class with IADs: Windows binds usbser.sys without an INF.
    config.device_class = 0xEF;
    config.device_sub_class = 0x02;
    config.device_protocol = 0x01;
    config.composite_with_iads = true;

    static CONFIG_DESCRIPTOR: StaticCell<[u8; 256]> = StaticCell::new();
    static BOS_DESCRIPTOR: StaticCell<[u8; 256]> = StaticCell::new();
    static CONTROL_BUF: StaticCell<[u8; 64]> = StaticCell::new();
    static CDC_STATE: StaticCell<State> = StaticCell::new();

    let mut builder = Builder::new(
        Driver::new(p.USB, Irqs),
        config,
        CONFIG_DESCRIPTOR.init([0; 256]),
        BOS_DESCRIPTOR.init([0; 256]),
        &mut [],
        CONTROL_BUF.init([0; 64]),
    );
    let class = CdcAcmClass::new(
        &mut builder,
        CDC_STATE.init(State::new()),
        MAX_PACKET_LEN as u16,
    );
    let usb = builder.build();
    let (sender, receiver, control) = class.split_with_control();

    spawner.spawn(usb_task(usb).unwrap());
    spawner.spawn(control_task(control).unwrap());
    spawner.spawn(command_task(sender, receiver, pin_number).unwrap());
    spawner.spawn(keying_task(carrier, led).unwrap());
}

/// The 60 kHz square wave on the coil's pin.
struct Carrier {
    pwm: Pwm<'static>,
    /// The compare value for 50 percent duty.
    full_duty: u16,
}

impl Carrier {
    /// Reduced carrier is no carrier: a compare value of 0 holds the pin low. The slice takes a
    /// new compare value when its counter wraps, so the wave is never cut inside a cycle.
    fn set(&mut self, on: bool) {
        let duty = if on { self.full_duty } else { 0 };
        // Cannot fail: neither value is above the slice's top.
        let _ = self.pwm.set_duty_cycle(duty);
    }
}

#[embassy_executor::task]
async fn keying_task(mut carrier: Carrier, mut led: Output<'static>) {
    loop {
        let keying = TRANSMITTER
            .lock(|transmitter| transmitter.borrow_mut().tick(Instant::now().as_micros()));
        carrier.set(keying.carrier);
        led.set_level(keying.transmitting.into());
        match keying.until_micros {
            Some(until) => {
                select(Timer::at(Instant::from_micros(until)), COMMANDED.wait()).await;
            }
            None => COMMANDED.wait().await,
        }
    }
}

#[embassy_executor::task]
async fn usb_task(mut usb: UsbDevice<'static, UsbDriver>) -> ! {
    usb.run().await
}

#[embassy_executor::task]
async fn control_task(control: ControlChanged<'static>) {
    loop {
        control.control_changed().await;
        let asserted = DTR_EPOCH.load(Ordering::Relaxed) % 2 == 1;
        // Line coding changes also land here; only DTR matters.
        if control.dtr() != asserted {
            DTR_EPOCH.fetch_add(1, Ordering::Relaxed);
            DTR_CHANGED.signal(());
        }
    }
}

#[embassy_executor::task]
async fn command_task(
    mut sender: Sender<'static, UsbDriver>,
    mut receiver: Receiver<'static, UsbDriver>,
    pin: u8,
) {
    loop {
        let epoch = wait_for_dtr().await;
        let Err(Disconnected) = session(&mut sender, &mut receiver, epoch, pin).await;
    }
}

/// Returns the `DTR_EPOCH` of the session that starts now.
async fn wait_for_dtr() -> u32 {
    loop {
        let epoch = DTR_EPOCH.load(Ordering::Relaxed);
        if epoch % 2 == 1 {
            return epoch;
        }
        DTR_CHANGED.wait().await;
    }
}

/// The host dropped DTR, or the USB bus reset.
struct Disconnected;

/// Serves one session: `# session`, the banner, then a reply for each command line until the
/// host leaves. The carrier is none of its business: `keying_task` runs on between sessions.
async fn session(
    sender: &mut Sender<'static, UsbDriver>,
    receiver: &mut Receiver<'static, UsbDriver>,
    epoch: u32,
    pin: u8,
) -> Result<Infallible, Disconnected> {
    let mut packet = Packet::new();
    packet.push(&Line::Session);
    packet.push(&Line::Banner { pin });
    send(sender, epoch, &mut packet).await?;

    // Bytes that arrived before the banner belong to no command: the end of an earlier session,
    // or a host that did not wait for the banner. Whatever was received by now is dropped
    // without waiting for more, and the first command line starts empty.
    let mut received = [0; MAX_PACKET_LEN];
    while let Poll::Ready(Ok(_)) = poll_once(receiver.read_packet(&mut received)) {}
    let mut lines = LineBuffer::new();

    loop {
        let read = receiver.read_packet(&mut received);
        let len = match select(session_ended(epoch), read).await {
            Either::First(()) => return Err(Disconnected),
            Either::Second(read) => read.map_err(|_| Disconnected)?,
        };
        for &byte in &received[..len] {
            let Some(asked) = lines.push(byte) else {
                continue;
            };
            // The moment the line ends is the moment a `TIME` means, so the clock is read here,
            // under the same lock as the command.
            let reply = TRANSMITTER.lock(|transmitter| {
                let now = Instant::now().as_micros();
                asked.and_then(|command| transmitter.borrow_mut().handle(command, now))
            });
            COMMANDED.signal(());
            packet.push(&Line::Reply(reply));
            send(sender, epoch, &mut packet).await?;
        }
    }
}

/// Sends the lines in `packet` and empties it.
async fn send(
    sender: &mut Sender<'static, UsbDriver>,
    epoch: u32,
    packet: &mut Packet,
) -> Result<(), Disconnected> {
    // A write while DTR is down would wait until the next host opens the port and then deliver
    // a stale line ahead of its `# session`. So nothing starts after the session ends, and a
    // write still waiting for the host when it ends is abandoned.
    if DTR_EPOCH.load(Ordering::Relaxed) != epoch {
        return Err(Disconnected);
    }
    let write = sender.write_packet(&packet.buf[..packet.len]);
    match select(write, session_ended(epoch)).await {
        Either::First(result) => result.map_err(|_| Disconnected)?,
        Either::Second(()) => return Err(Disconnected),
    }
    packet.len = 0;
    Ok(())
}

/// Completes when the session that started at `epoch` ends.
async fn session_ended(epoch: u32) {
    while DTR_EPOCH.load(Ordering::Relaxed) == epoch {
        DTR_CHANGED.wait().await;
    }
}

enum Line {
    Session,
    Banner { pin: u8 },
    Reply(Result<Status, Error>),
}

impl fmt::Display for Line {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Line::Session => f.write_str("# session"),
            Line::Banner { pin } => write!(
                f,
                "# wwvb-transmitter {} pin={pin}",
                env!("CARGO_PKG_VERSION")
            ),
            Line::Reply(Ok(status)) => status.fmt(f),
            Line::Reply(Err(error)) => error.fmt(f),
        }
    }
}

/// Whole lines waiting to go out in one USB packet.
struct Packet {
    buf: [u8; PACKET_LEN],
    len: usize,
}

impl Packet {
    const fn new() -> Self {
        Self {
            buf: [0; PACKET_LEN],
            len: 0,
        }
    }

    /// Appends `line` and a line feed. A line that does not fit is left out whole: a line is
    /// never split. The assertion under `SESSION_LINES_LEN` checks at compile time that every
    /// line this file pushes fits.
    fn push(&mut self, line: &Line) {
        let start = self.len;
        let fits = writeln!(self, "{line}").is_ok();
        debug_assert!(fits);
        if !fits {
            self.len = start;
        }
    }
}

impl fmt::Write for Packet {
    fn write_str(&mut self, s: &str) -> fmt::Result {
        let end = self.len + s.len();
        if end > PACKET_LEN {
            return Err(fmt::Error);
        }
        self.buf[self.len..end].copy_from_slice(s.as_bytes());
        self.len = end;
        Ok(())
    }
}
