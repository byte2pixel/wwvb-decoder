using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace WwvbDecoder.Tools;

/// <summary>
/// Runs the decoder pipeline on an edge source and prints one line per stage output.
/// </summary>
internal static class DecodeRunner
{
    /// <summary>
    /// Runs the decoder pipeline on the given edge source, printing each stage's output to the
    /// console as one line prefixed by the stage number. Stage 7 doubles as the per-frame status
    /// line with the clock status and the offset from the PC clock.
    /// </summary>
    /// <param name="source">The edge source to decode.</param>
    /// <param name="reducedCarrierLevel">The pin level of reduced carrier, or null for the default.</param>
    /// <param name="wallSamples">
    /// The capture's <c># wall</c> samples, used to print the offset from the PC clock at
    /// recording time on each stage 7 line. Empty for a live source.
    /// </param>
    /// <param name="flushEveryLine">
    /// True for a live source (serial, or a paced replay), so each line appears as it happens.
    /// False for an unpaced replay, where buffering keeps the hundred thousand lines of an
    /// overnight capture fast; the output is then flushed once per frame and at the end.
    /// </param>
    /// <param name="ct">Cancellation stops the run; the pending output is flushed first.</param>
    public static async Task RunAsync(
        IEdgeSource source,
        PinLevel? reducedCarrierLevel,
        ImmutableArray<WallSample> wallSamples,
        bool flushEveryLine,
        CancellationToken ct
    )
    {
        var pipeline = new DecoderPipeline(TimeProvider.System)
        {
            PulseMeasurement = { ReducedCarrierLevel = reducedCarrierLevel ?? PinLevel.High },
        };

        await using StagePrinter printer = new(
            new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = flushEveryLine,
            },
            wallSamples
        );

        pipeline.SourceReset += printer.OnReset;
        pipeline.InfoReceived += printer.OnInfo;
        pipeline.EdgeReceived += printer.OnEdge;
        pipeline.PulseMeasured += printer.OnPulse;
        pipeline.SymbolClassified += printer.OnSymbol;
        pipeline.FrameSyncChanged += printer.OnFrameSync;
        pipeline.FrameDecoded += printer.OnFrameDecoded;
        pipeline.FrameValidated += printer.OnFrameValidated;
        pipeline.ClockUpdated += printer.OnClock;

        try
        {
            await pipeline.RunAsync(source, ct);
        }
        finally
        {
            printer.WriteSummary(pipeline.PulseMeasurement);
        }
    }

    /// <summary>
    /// Formats each stage's output as one line and counts frames. Owns the writer; disposing
    /// flushes it.
    /// </summary>
    private sealed class StagePrinter(TextWriter output, ImmutableArray<WallSample> wallSamples)
        : IAsyncDisposable
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private int _resetCount;
        private int _frameCount;
        private int _acceptedCount;
        private int _rejectedCount;

        public void OnReset(string reason)
        {
            _resetCount++;
            output.WriteLine($"Reset: {reason}");
        }

        public void OnInfo(string line) => output.WriteLine($"Info: {line}");

        public void OnEdge(Edge e) =>
            output.WriteLine(string.Create(Inv, $"Stage 1 (Edge): {e.Level} {e.Micros}"));

        public void OnPulse(Pulse p) =>
            output.WriteLine(
                string.Create(
                    Inv,
                    $"Stage 2 (Pulse): start {p.StartMicros} duration {p.DurationMicros} us"
                )
            );

        public void OnSymbol(Symbol s) =>
            output.WriteLine(
                string.Create(
                    Inv,
                    $"Stage 3 (Symbol): {s.Kind} start {s.StartMicros} duration {s.DurationMicros} us"
                )
            );

        public void OnFrameSync(FrameSyncEvent e) =>
            output.WriteLine($"Stage 4 (Frame Sync): {FormatFrameSync(e)}");

        public void OnFrameDecoded(DecodedFrame f)
        {
            _frameCount++;
            output.WriteLine($"Stage 5 (Frame Decoded): {FormatFrame(f)}");
        }

        public void OnFrameValidated(ValidationResult v)
        {
            if (v.Accepted)
            {
                _acceptedCount++;
                output.WriteLine("Stage 6 (Frame Validated): ACCEPTED");
                return;
            }

            _rejectedCount++;
            string failures = string.Join("; ", v.Failures.Select(f => $"{f.Check}: {f.Detail}"));
            output.WriteLine($"Stage 6 (Frame Validated): REJECTED {failures}");
        }

        public void OnClock(ClockState c)
        {
            output.WriteLine($"Stage 7 (Clock): {FormatClock(c)}");
            output.Flush();
        }

        public void WriteSummary(PulseMeasurement stage2) =>
            output.WriteLine(
                string.Create(
                    Inv,
                    $"Decoding complete. Resets: {_resetCount}, Frames: {_frameCount}, Accepted: {_acceptedCount}, Rejected: {_rejectedCount}, Glitches dropped: {stage2.GlitchesDropped}, Gaps bridged: {stage2.GapsBridged}, Unpaired edges: {stage2.UnpairedEdges}"
                )
            );

        public ValueTask DisposeAsync() => output.DisposeAsync();

        private static string FormatFrameSync(FrameSyncEvent e) =>
            e switch
            {
                FrameSyncEvent.Searching s => string.Create(
                    Inv,
                    $"Searching markers={s.ConsecutiveMarkers}"
                ),
                FrameSyncEvent.SlotFilled s => string.Create(
                    Inv,
                    $"SlotFilled second={s.Second} {s.Kind}"
                ),
                FrameSyncEvent.FrameComplete c => string.Create(
                    Inv,
                    $"FrameComplete start={c.Frame.StartMicros} end={c.Frame.EndMicros}"
                ),
                FrameSyncEvent.SyncLost l => $"SyncLost {l.Reason}",
                _ => e.GetType().Name,
            };

        private static string FormatFrame(DecodedFrame f)
        {
            string ut1Sign = f.Ut1Sign switch
            {
                Ut1Sign.Plus => "+",
                Ut1Sign.Minus => "-",
                _ => "?",
            };
            string errorSeconds = string.Join(",", f.ErrorSeconds);
            // Stage 5 is the raw read: the two transmitted year digits and UT1 tenths as sent,
            // which can be out of range before stage 6 judges them. Stage 7 prints the full UTC date.
            return string.Create(
                Inv,
                $"year={f.Year} day={f.DayOfYear} {f.Hour:D2}:{f.Minute:D2} ut1Tenths={ut1Sign}{f.Ut1Tenths} leapYear={f.LeapYear} leapSecondPending={f.LeapSecondPending} dst={f.Dst} errorSeconds=[{errorSeconds}]"
            );
        }

        private string FormatClock(ClockState c)
        {
            string frameUtc = c.FrameUtc is { } utc
                ? utc.ToString("yyyy-MM-ddTHH:mm:ssZ", Inv)
                : "-";
            string offset = c.OffsetFromPc is { } o ? FormatOffset(o) : "-";
            string recorded = RecordedOffset(c) is { } r ? FormatOffset(r) : "unknown";
            return string.Create(
                Inv,
                $"{c.Status} frameUtc={frameUtc} offsetFromPc={offset} recordedOffset={recorded} accepted={c.ConsecutiveAccepted} rejectedSince={c.RejectedSinceLastAccepted}"
            );
        }

        // The offset between the decoded WWVB time and the PC clock at recording time, from the
        // nearest "# wall" sample: positive when WWVB was ahead of the PC. Under replay
        // OffsetFromPc compares a recorded frame with today's clock and means little; this is the
        // number the capture format keeps the wall lines for. Good to about a second, because the
        // recorder writes the wall time truncated to whole seconds and pairs it with the last edge
        // it had already written.
        private TimeSpan? RecordedOffset(ClockState c)
        {
            if (c.FrameUtc is not { } frameUtc || c.FrameStartMicros is not { } start)
            {
                return null;
            }

            if (wallSamples.IsDefaultOrEmpty)
            {
                return null;
            }

            // Nearest either side, so the first frame still gets a value when the first wall line
            // comes after it. Signed arithmetic: the frame can start before the sample.
            WallSample nearest = wallSamples.MinBy(w => Math.Abs((long)start - (long)w.Micros));
            long deltaMicros = (long)start - (long)nearest.Micros;
            DateTimeOffset recordedPcTime = nearest.Utc + TimeSpan.FromMicroseconds(deltaMicros);
            return frameUtc - recordedPcTime;
        }

        // Seconds, not hh:mm:ss: under replay the offset from today's clock can exceed a day.
        private static string FormatOffset(TimeSpan offset) =>
            string.Create(
                Inv,
                $"{(offset < TimeSpan.Zero ? "-" : "+")}{Math.Abs(offset.TotalSeconds):F3} s"
            );
    }
}
