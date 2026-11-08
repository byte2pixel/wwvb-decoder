using Microsoft.Extensions.Time.Testing;

namespace WwvbDecoder.Core.Tests;

public class DecoderPipelineTests
{
    private static DateTimeOffset CleanMinute => new(2026, 12, 10, 19, 30, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private DecoderPipeline NewPipeline() => new(new FakeTimeProvider(CleanMinute));

    private EdgeEvent Sample(ulong micros, PinLevel level) =>
        new EdgeEvent.Sample(new Edge(micros, level));

    private BoundedEdgeSource Synthetic(ulong untilMicros) =>
        new(
            new SyntheticEdgeSource(new FakeTimeProvider(CleanMinute))
            {
                StartUtc = CleanMinute,
                Speed = 0,
            },
            untilMicros
        );

    private async Task<List<EdgeEvent>> Collect(IEdgeSource source) =>
        await source.ReadAsync(Ct).ToListAsync(Ct);

    // Every pipeline event, in the order raised, as (kind, value).
    private static List<(string Kind, object Value)> Record(DecoderPipeline pipeline)
    {
        List<(string Kind, object Value)> log = [];
        pipeline.EdgeReceived += e => log.Add(("Edge", e));
        pipeline.PulseMeasured += p => log.Add(("Pulse", p));
        pipeline.SymbolClassified += s => log.Add(("Symbol", s));
        pipeline.FrameSyncChanged += f => log.Add(("Sync", f));
        pipeline.FrameDecoded += d => log.Add(("Decoded", d));
        pipeline.FrameValidated += v => log.Add(("Validated", v));
        pipeline.ClockUpdated += c => log.Add(("Clock", c));
        pipeline.SourceReset += r => log.Add(("Reset", r));
        pipeline.InfoReceived += i => log.Add(("Info", i));
        return log;
    }

    [Fact]
    public void Constructor_ThrowsOnNullTimeProvider()
    {
        //Arrange
        Action act = () => _ = new DecoderPipeline(null!);

        // Act & Assert
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ExposesFreshStages()
    {
        //Arrange
        DecoderPipeline pipeline = NewPipeline();

        // Act & Assert
        pipeline.PulseMeasurement.ShouldNotBeNull();
        pipeline.SymbolClassifier.ShouldNotBeNull();
        pipeline.FrameSync.ShouldNotBeNull();
        pipeline.FieldDecoder.ShouldNotBeNull();
        pipeline.FrameValidator.ShouldNotBeNull();
        pipeline.Clock.ShouldNotBeNull();
        pipeline.Clock.Current.Status.ShouldBe(ClockStatus.NoSignal);
    }

    [Fact]
    public async Task RunAsync_NullSource_ThrowsSynchronouslyAndLeavesPipelineIdle()
    {
        // Arrange
        DecoderPipeline pipeline = NewPipeline();
        Action act = () => _ = pipeline.RunAsync(null!, Ct);

        // Act & Assert
        act.ShouldThrow<ArgumentNullException>();
        var log = Record(pipeline);
        await pipeline.RunAsync(ScriptedEdgeSource.Of(new EdgeEvent.Info("next")), Ct);
        log.ShouldBe([("Info", "next")]);
    }

    [Fact]
    public async Task RunAsync_WhileRunning_ThrowsSynchronouslyAndFirstRunContinues()
    {
        //Arrange
        DecoderPipeline pipeline = NewPipeline();
        var log = Record(pipeline);
        var source = ScriptedEdgeSource.Gated(
            [new EdgeEvent.Reset("start")],
            new EdgeEvent.Info("after")
        );
        Task run = pipeline.RunAsync(source, Ct);
        await source.AtGate.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        // Act
        Action act = () => _ = pipeline.RunAsync(ScriptedEdgeSource.Of(), Ct);

        // Assert
        act.ShouldThrow<InvalidOperationException>();
        source.OpenGate();

        await run.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        log.ShouldBe([("Reset", "start"), ("Info", "after")]);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("faulted")]
    [InlineData("cancelled")]
    public async Task RunAsync_AfterRunEnds_CanRunAgain(string howFirstRunEnds)
    {
        // Arrange
        DecoderPipeline pipeline = NewPipeline();
        switch (howFirstRunEnds)
        {
            case "completed":
                await pipeline.RunAsync(ScriptedEdgeSource.Of(new EdgeEvent.Info("x")), Ct);
                break;
            case "faulted":
                // PulseMeasurement throws on a decreasing timestamp.
                Task faulted = pipeline.RunAsync(
                    ScriptedEdgeSource.Of(Sample(1_000, PinLevel.High), Sample(500, PinLevel.Low)),
                    Ct
                );
                await Should.ThrowAsync<InvalidOperationException>(faulted.WaitAsync(Timeout, Ct));
                break;
            case "cancelled":
                using (
                    CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(Ct)
                )
                {
                    ScriptedEdgeSource gated = ScriptedEdgeSource.Gated([]);
                    Task cancelled = pipeline.RunAsync(gated, cts.Token);
                    await gated.AtGate.WaitAsync(Timeout, Ct);
                    await cts.CancelAsync();
                    await Should.ThrowAsync<OperationCanceledException>(
                        cancelled.WaitAsync(Timeout, Ct)
                    );
                }

                break;
        }

        var log = Record(pipeline);

        // Act
        await pipeline.RunAsync(ScriptedEdgeSource.Of(new EdgeEvent.Info("again")), Ct);

        // Assert
        log.ShouldBe([("Info", "again")]);
    }

    [Fact]
    public async Task Info_RaisesInfoReceivedOnly()
    {
        // Arrange
        DecoderPipeline pipeline = NewPipeline();
        var log = Record(pipeline);

        // Act
        await pipeline.RunAsync(ScriptedEdgeSource.Of(new EdgeEvent.Info("# dropped 3")), Ct);

        // Assert
        log.ShouldBe([("Info", "# dropped 3")]);
    }

    [Fact]
    public async Task Reset_ResetsEveryStageThenRaisesSourceReset()
    {
        // Arrange: two frames lock the clock, then the source resets.
        DecoderPipeline pipeline = NewPipeline();
        List<EdgeEvent> events = await Collect(Synthetic(150_000_000));
        events.Add(new EdgeEvent.Reset("session"));
        DecodedFrame? lastDecoded = null;
        pipeline.FrameDecoded += d => lastDecoded = d;
        ClockStatus? lastBeforeReset = null;
        pipeline.ClockUpdated += c => lastBeforeReset = c.Status;
        ClockState? clockAtReset = null;
        pipeline.SourceReset += reason =>
        {
            if (reason == "session")
            {
                clockAtReset = pipeline.Clock.Current;
            }
        };

        // Act
        await pipeline.RunAsync(ScriptedEdgeSource.Of([.. events]), Ct);

        // Assert: the clock was reset before SourceReset was raised.
        lastBeforeReset.ShouldBe(ClockStatus.Locked);
        clockAtReset.ShouldBe(new ClockState(ClockStatus.NoSignal, null, null, null, null, 0, 0));

        // FrameSync is searching again. It was synced, and without its reset it would lose
        // sync on this symbol before searching.
        pipeline
            .FrameSync.Push(new Symbol(0, 800_000, SymbolKind.Marker))
            .ShouldBe([new FrameSyncEvent.Searching(1)]);

        // FrameValidator forgot the last frame. Without its reset, the same minute fails
        // Consecutive.
        lastDecoded.ShouldNotBeNull();
        pipeline.FrameValidator.Push(lastDecoded).ShouldHaveSingleItem().Accepted.ShouldBeTrue();
    }

    [Fact]
    public async Task Reset_MidStream_StagesStartOver()
    {
        // Arrange: reset at 150 s. Without it, frames complete at 61, 121, 181 and 241 s.
        // After it, FrameSync must find two markers again (179 s and 180 s), so the frame
        // that started at 120 s is lost and the next one completes at 241 s.
        DecoderPipeline pipeline = NewPipeline();
        List<EdgeEvent> events = await Collect(Synthetic(250_000_000));
        int resetAt = events.FindIndex(e =>
            e is EdgeEvent.Sample s && s.Edge.Micros >= 150_000_000
        );
        events.Insert(resetAt, new EdgeEvent.Reset("session"));
        var log = Record(pipeline);

        // Act
        await pipeline.RunAsync(ScriptedEdgeSource.Of([.. events]), Ct);

        // Assert
        int reset = log.IndexOf(("Reset", "session"));
        reset.ShouldBeGreaterThan(0);
        log.Take(reset).Count(e => e.Kind == "Decoded").ShouldBe(2);
        ClockState afterReset = log.Skip(reset)
            .Where(e => e.Kind == "Clock")
            .Select(e => (ClockState)e.Value)
            .ShouldHaveSingleItem();
        afterReset.Status.ShouldBe(ClockStatus.Acquiring);
        afterReset.ConsecutiveAccepted.ShouldBe(1);
    }

    // A pulse is released only by the next entering edge. A reset between them discards it.
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task Reset_DiscardsThePendingPulse(bool reset, int expectedPulses)
    {
        // Arrange
        DecoderPipeline pipeline = NewPipeline();
        List<EdgeEvent> events =
        [
            Sample(0, PinLevel.High),
            Sample(200_000, PinLevel.Low),
            Sample(1_000_000, PinLevel.High),
        ];
        if (reset)
        {
            events.Insert(2, new EdgeEvent.Reset("session"));
        }

        var log = Record(pipeline);

        // Act
        await pipeline.RunAsync(ScriptedEdgeSource.Of([.. events]), Ct);

        // Assert
        log.Count(e => e.Kind == "Pulse").ShouldBe(expectedPulses);
    }

    [Fact]
    public async Task Sample_EventsAreRaisedDepthFirst()
    {
        // Arrange: three frames complete, at 61, 121 and 181 s.
        DecoderPipeline pipeline = NewPipeline();
        int samples = (await Collect(Synthetic(190_000_000))).Count(e => e is EdgeEvent.Sample);
        var log = Record(pipeline);

        // Act
        await pipeline.RunAsync(Synthetic(190_000_000), Ct);

        // Assert
        log[0].ShouldBe(("Reset", "start"));
        log.Count(e => e.Kind == "Edge").ShouldBe(samples);
        for (int i = 1; i < log.Count; i++)
        {
            string previous = log[i - 1].Kind;
            switch (log[i].Kind)
            {
                case "Pulse":
                    previous.ShouldBe("Edge");
                    break;
                case "Symbol":
                    previous.ShouldBe("Pulse");
                    break;
                case "Sync":
                    previous.ShouldBeOneOf("Symbol", "Sync");
                    break;
                case "Decoded":
                    var complete = log[i - 1].Value.ShouldBeOfType<FrameSyncEvent.FrameComplete>();
                    var decoded = (DecodedFrame)log[i].Value;
                    decoded.Frame.ShouldBeSameAs(complete.Frame);
                    log[i + 1].Kind.ShouldBe("Validated");
                    ((ValidationResult)log[i + 1].Value).Frame.ShouldBeSameAs(decoded);
                    log[i + 2].Kind.ShouldBe("Clock");
                    break;
            }
        }

        log.Count(e => e.Value is FrameSyncEvent.FrameComplete).ShouldBe(3);
        log.Count(e => e.Kind == "Decoded").ShouldBe(3);
        log.Where(e => e.Kind == "Clock")
            .Select(e => ((ClockState)e.Value).Status)
            .ShouldBe([ClockStatus.Acquiring, ClockStatus.Locked, ClockStatus.Locked]);
    }

    [Fact]
    public async Task StageException_FaultsTheTaskAndStopsTheRun()
    {
        // Arrange: PulseMeasurement throws on a decreasing timestamp.
        DecoderPipeline pipeline = NewPipeline();
        var log = Record(pipeline);
        ScriptedEdgeSource source = ScriptedEdgeSource.Of(
            Sample(1_000, PinLevel.High),
            Sample(500, PinLevel.Low),
            new EdgeEvent.Info("after")
        );

        // Act: if RunAsync threw here instead of faulting the Task, the test would fail.
        Task run = pipeline.RunAsync(source, Ct);

        // Assert
        await Should.ThrowAsync<InvalidOperationException>(run.WaitAsync(Timeout, Ct));
        log.ShouldNotContain(("Info", "after"));
    }

    [Fact]
    public async Task HandlerException_FaultsTheTaskAndStopsTheRun()
    {
        // Arrange
        DecoderPipeline pipeline = NewPipeline();
        InvalidOperationException boom = new("boom");
        List<string> lines = [];
        pipeline.InfoReceived += line =>
        {
            lines.Add(line);
            if (line == "boom")
            {
                throw boom;
            }
        };

        // Act
        Task run = pipeline.RunAsync(
            ScriptedEdgeSource.Of(new EdgeEvent.Info("boom"), new EdgeEvent.Info("after")),
            Ct
        );

        // Assert
        (await Should.ThrowAsync<InvalidOperationException>(run.WaitAsync(Timeout, Ct)))
            .ShouldBeSameAs(boom);
        lines.ShouldBe(["boom"]);
    }

    [Fact]
    public async Task Cancellation_EndsAsACancelledTask()
    {
        // Arrange
        DecoderPipeline pipeline = NewPipeline();
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        ScriptedEdgeSource gated = ScriptedEdgeSource.Gated([new EdgeEvent.Reset("start")]);
        Task run = pipeline.RunAsync(gated, cts.Token);
        await gated.AtGate.WaitAsync(Timeout, Ct);

        // Act
        await cts.CancelAsync();

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(run.WaitAsync(Timeout, Ct));
        run.IsCanceled.ShouldBeTrue();
    }

    [Fact]
    public async Task RunAsync_NeverDisposesTheSource()
    {
        // Arrange
        DecoderPipeline pipeline = NewPipeline();
        ScriptedEdgeSource source = ScriptedEdgeSource.Of(new EdgeEvent.Info("x"));

        // Act
        await pipeline.RunAsync(source, Ct);

        // Assert
        source.DisposeCount.ShouldBe(0);
    }
}
