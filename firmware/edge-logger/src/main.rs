//! WWVB edge logger firmware for the Raspberry Pi Pico 2 (RP2350).
//!
//! Timestamps every edge on the receiver's data pin and writes one line per
//! edge over USB CDC serial. It makes no decisions about what the edges mean
//! (ADR-0001); the line format is the contract in
//! `docs/design/serial-protocol.md`.
//!
//! - `edge_task` runs on an interrupt executor above both the USB interrupt
//!   and thread mode, so USB work never delays a timestamp. It stamps each
//!   edge and queues it without blocking.
//! - `usb_task` runs the USB device.
//! - `control_task` watches DTR.
//! - `writer_task` waits for DTR, writes `# session` and the banner, then drains
//!   the queue as whole lines.

#![no_std]
#![no_main]

use core::convert::Infallible;
use core::fmt::{self, Write as _};
use core::sync::atomic::{AtomicU32, Ordering};

use embassy_executor::Spawner;
use embassy_futures::select::{Either, select};
use embassy_rp::bind_interrupts;
use embassy_rp::executor::InterruptExecutor;
use embassy_rp::gpio::{Input, Level, Output, Pin as _, Pull};
use embassy_rp::interrupt;
use embassy_rp::interrupt::{InterruptExt as _, Priority};
use embassy_rp::peripherals::USB;
use embassy_rp::usb::{Driver, InterruptHandler};
use embassy_sync::blocking_mutex::raw::CriticalSectionRawMutex;
use embassy_sync::channel::Channel;
use embassy_sync::signal::Signal;
use embassy_time::{Duration, Instant, with_deadline};
use embassy_usb::class::cdc_acm::{CdcAcmClass, ControlChanged, Sender, State};
use embassy_usb::{Builder, Config, UsbDevice};
use panic_halt as _;
use static_cell::StaticCell;

// Shown by `picotool info`.
#[unsafe(link_section = ".bi_entries")]
#[used]
pub static PICOTOOL_ENTRIES: [embassy_rp::binary_info::EntryAddr; 4] = [
    embassy_rp::binary_info::rp_program_name!(c"wwvb-edge-logger"),
    embassy_rp::binary_info::rp_program_description!(c"WWVB receiver edge logger"),
    embassy_rp::binary_info::rp_cargo_version!(),
    embassy_rp::binary_info::rp_program_build_attribute!(),
];

bind_interrupts!(struct Irqs {
    USBCTRL_IRQ => InterruptHandler<USB>;
});

type UsbDriver = Driver<'static, USB>;

/// Edges waiting for the writer. When it is full, new edges are dropped and
/// counted, never waited on.
const QUEUE_LEN: usize = 256;

const DROP_REPORT_PERIOD: Duration = Duration::from_secs(10);

/// Full-speed bulk packets hold 64 bytes. Staying one under keeps every packet
/// short, so the host hands it over at once instead of waiting for a
/// zero-length packet to end the transfer.
const PACKET_LEN: usize = 63;

/// `H `, 20 digits of `u64`, and the line feed.
const MAX_EDGE_LINE: usize = 23;

/// The banner with the longest possible pin number.
const MAX_BANNER_LINE: usize =
    "# wwvb-edge-logger ".len() + env!("CARGO_PKG_VERSION").len() + " pin=255\n".len();

/// `# dropped` with the largest `u32`.
const MAX_DROPPED_LINE: usize = "# dropped 4294967295\n".len();

// A line is never split, so every kind must fit an empty packet.
const _: () = assert!(
    MAX_EDGE_LINE <= PACKET_LEN && MAX_BANNER_LINE <= PACKET_LEN && MAX_DROPPED_LINE <= PACKET_LEN
);

#[derive(Clone, Copy)]
struct Edge {
    micros: u64,
    high: bool,
}

static EDGES: Channel<CriticalSectionRawMutex, Edge, QUEUE_LEN> = Channel::new();

/// Edges lost to a full queue since the last `# dropped` report.
static DROPPED: AtomicU32 = AtomicU32::new(0);

/// Counts DTR transitions; odd while DTR is asserted. A writer that sees it
/// change knows its host went away, even if DTR is asserted again by the time
/// it looks.
static DTR_EPOCH: AtomicU32 = AtomicU32::new(0);

/// Raised by `control_task` on every DTR transition.
static DTR_CHANGED: Signal<CriticalSectionRawMutex, ()> = Signal::new();

static EDGE_EXECUTOR: InterruptExecutor = InterruptExecutor::new();

#[interrupt]
unsafe fn SWI_IRQ_0() {
    // SAFETY: called only from this handler, after `start()` in `main`.
    unsafe { EDGE_EXECUTOR.on_interrupt() }
}

#[embassy_executor::main(
    executor = "embassy_rp::executor::Executor",
    entry = "cortex_m_rt::entry"
)]
async fn main(spawner: Spawner) {
    let p = embassy_rp::init(Default::default());

    // GP23 is the regulator's PS pin. High forces PWM mode, whose ripple is
    // lower than the default power-save mode's, so it couples less into the
    // receiver. Dropping an `Output` releases the pin, so it is leaked.
    core::mem::forget(Output::new(p.PIN_23, Level::High));

    // Off until a host asserts DTR, then steadily on. A blinking LED modulates
    // the 3V3 rail enough to show up on the receiver output.
    let led = Output::new(p.PIN_25, Level::Low);

    // The module drives the line, so no pull.
    let pin_number = p.PIN_2.pin();
    let input = Input::new(p.PIN_2, Pull::None);

    // Edge path above everything USB: the edge executor (P1) above the GPIO
    // interrupt that wakes it (P2), both above the USB interrupt (P3) and
    // thread mode, where the USB stack and the writer run. embassy-rp sets
    // IO_IRQ_BANK0 to P3 and leaves USBCTRL_IRQ at P0, the highest, so both
    // are overridden here.
    interrupt::USBCTRL_IRQ.set_priority(Priority::P3);
    interrupt::IO_IRQ_BANK0.set_priority(Priority::P2);
    interrupt::SWI_IRQ_0.set_priority(Priority::P1);
    EDGE_EXECUTOR
        .start(interrupt::SWI_IRQ_0)
        .spawn(edge_task(input, pin_number).unwrap());

    let mut config = Config::new(0xC0DE, 0xCAFE);
    config.manufacturer = Some("wwvb-decoder");
    config.product = Some("wwvb-edge-logger");
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
    let class = CdcAcmClass::new(&mut builder, CDC_STATE.init(State::new()), 64);
    let usb = builder.build();

    // The host sends nothing; the receiver half is never read.
    let (sender, _receiver, control) = class.split_with_control();

    spawner.spawn(usb_task(usb).unwrap());
    spawner.spawn(control_task(control).unwrap());
    spawner.spawn(writer_task(sender, led, pin_number).unwrap());
}

#[embassy_executor::task]
async fn edge_task(mut pin: Input<'static>, pin_number: u8) {
    let raw_interrupts = embassy_rp::pac::IO_BANK0.intr(usize::from(pin_number) / 8);
    let group = usize::from(pin_number) % 8;
    loop {
        pin.wait_for_any_edge().await;
        let micros = Instant::now().as_micros();
        // Read after the wake, so an edge pair shorter than the wake latency
        // reports the same level twice. The protocol allows that.
        let high = pin.is_high();
        if EDGES.try_send(Edge { micros, high }).is_err() {
            DROPPED.fetch_add(1, Ordering::Relaxed);
        }
        // The raw edge latches hold every edge since the last re-arm, and
        // `wait_for_any_edge` clears them when it re-arms. Both set means at
        // least two edges went into this one line, so count at least one as
        // dropped. Only an edge in the few cycles between this read and the
        // clear goes uncounted.
        let latched = raw_interrupts.read();
        if latched.edge_high(group) && latched.edge_low(group) {
            DROPPED.fetch_add(1, Ordering::Relaxed);
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
async fn writer_task(mut sender: Sender<'static, UsbDriver>, mut led: Output<'static>, pin: u8) {
    loop {
        led.set_low();
        let epoch = wait_for_dtr().await;
        // Edges queued while nobody was listening belong to no session.
        EDGES.clear();
        DROPPED.store(0, Ordering::Relaxed);
        led.set_high();
        let Err(Disconnected) = stream(&mut sender, epoch, pin).await;
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

/// Writes one session: `# session`, the banner, then edges until the host leaves.
async fn stream(
    sender: &mut Sender<'static, UsbDriver>,
    epoch: u32,
    pin: u8,
) -> Result<Infallible, Disconnected> {
    let mut packet = Packet::new();
    put(sender, epoch, &mut packet, Line::Session).await?;
    put(sender, epoch, &mut packet, Line::Banner { pin }).await?;
    flush(sender, epoch, &mut packet).await?;

    let mut next_report = Instant::now() + DROP_REPORT_PERIOD;
    loop {
        // Checked every pass, not only on timeout, so a long noise burst
        // cannot starve the report.
        if Instant::now() >= next_report {
            next_report = Instant::now() + DROP_REPORT_PERIOD;
            let dropped = DROPPED.swap(0, Ordering::Relaxed);
            if dropped > 0 {
                put(sender, epoch, &mut packet, Line::Dropped(dropped)).await?;
                // Now, not with the next edge, which may be a while away.
                flush(sender, epoch, &mut packet).await?;
            }
        }

        let edge = with_deadline(next_report, EDGES.receive());
        match select(edge, session_ended(epoch)).await {
            Either::First(Ok(edge)) => {
                put(sender, epoch, &mut packet, Line::Edge(edge)).await?;
                // Fill the rest of this packet with edges already queued.
                while packet.remaining() >= MAX_EDGE_LINE {
                    let Ok(edge) = EDGES.try_receive() else { break };
                    put(sender, epoch, &mut packet, Line::Edge(edge)).await?;
                }
            }
            Either::First(Err(_)) => {}
            Either::Second(()) => return Err(Disconnected),
        }
        flush(sender, epoch, &mut packet).await?;
    }
}

/// Adds `line` to `packet`, sending the packet first if the line does not fit.
async fn put(
    sender: &mut Sender<'static, UsbDriver>,
    epoch: u32,
    packet: &mut Packet,
    line: Line,
) -> Result<(), Disconnected> {
    if !packet.push(&line) {
        flush(sender, epoch, packet).await?;
        // Every line fits an empty packet; the assertion under
        // `MAX_EDGE_LINE` checks that at compile time.
        let pushed = packet.push(&line);
        debug_assert!(pushed);
    }
    Ok(())
}

async fn flush(
    sender: &mut Sender<'static, UsbDriver>,
    epoch: u32,
    packet: &mut Packet,
) -> Result<(), Disconnected> {
    if packet.len == 0 {
        return Ok(());
    }
    // A write while DTR is down would wait until the next host opens the port
    // and then deliver stale lines ahead of its `# session`. So nothing starts
    // after the session ends, and a write still waiting for the host when it
    // ends is abandoned. A packet the USB hardware already holds still goes
    // out to the next host, but it is at most one, and the `# session` right
    // behind it resets the host.
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
    Edge(Edge),
    Dropped(u32),
}

impl fmt::Display for Line {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Line::Session => f.write_str("# session"),
            Line::Banner { pin } => write!(
                f,
                "# wwvb-edge-logger {} pin={pin}",
                env!("CARGO_PKG_VERSION")
            ),
            Line::Edge(edge) => {
                let level = if edge.high { 'H' } else { 'L' };
                write!(f, "{level} {}", edge.micros)
            }
            Line::Dropped(n) => write!(f, "# dropped {n}"),
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

    fn remaining(&self) -> usize {
        PACKET_LEN - self.len
    }

    /// Appends `line` and a line feed. Returns false, leaving the packet
    /// unchanged, if it does not fit: a line is never split.
    fn push(&mut self, line: &Line) -> bool {
        let start = self.len;
        if writeln!(self, "{line}").is_ok() {
            true
        } else {
            self.len = start;
            false
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
