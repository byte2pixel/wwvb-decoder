using System.Runtime.CompilerServices;
using System.Threading.Channels;
using WwvbDecoder.Host.Bridge;
using WwvbDecoder.Host.Services;

namespace WwvbDecoder.Host.Tests.Services;

/// <summary>
/// The polarity warning in the status: when the controller sends it and what takes it back.
/// The rule itself is in <see cref="PolarityWatchTests"/>.
/// </summary>
public class SourceControllerPolarityTests
{
    private const ulong Second = 1_000_000;
    private const string Launch = "COM9 was not found. Replaying the newest capture.";

    private readonly FakeSourceEnvironment _environment = new();
    private readonly RecordingPoster _poster = new();
    private readonly FakeSettingsStore _settings = new();
    private readonly DecoderPipeline _pipeline = new(TimeProvider.System);

    private SourceController CreateController() => new(_pipeline, _environment, _poster, _settings);

    /// <summary>A source the test feeds a piece at a time, so a command can come between two pieces.</summary>
    private sealed class FedSource : IEdgeSource
    {
        private readonly Channel<EdgeEvent> _events = Channel.CreateUnbounded<EdgeEvent>();

        public string Name => "synthetic";

        public void Feed(IEnumerable<EdgeEvent> events)
        {
            foreach (EdgeEvent edgeEvent in events)
            {
                _events.Writer.TryWrite(edgeEvent).ShouldBeTrue();
            }
        }

        public async IAsyncEnumerable<EdgeEvent> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct
        )
        {
            yield return new EdgeEvent.Reset("start");
            await foreach (EdgeEvent edgeEvent in _events.Reader.ReadAllAsync(ct))
            {
                yield return edgeEvent;
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// One pulse a second from <paramref name="startSecond"/> on, as the logger would send them
    /// with the level right: high for the pulse, low after it.
    /// </summary>
    private static IEnumerable<EdgeEvent> Pulses(ulong startSecond, IEnumerable<ulong> widths) =>
        widths.SelectMany(
            (width, index) =>
            {
                ulong start = (startSecond + (ulong)index) * Second;
                return new EdgeEvent[]
                {
                    new EdgeEvent.Sample(new Edge(start, PinLevel.High)),
                    new EdgeEvent.Sample(new Edge(start + width, PinLevel.Low)),
                };
            }
        );

    /// <summary>Pulses of 0.8 s, which is what every 0 looks like with the level inverted.</summary>
    private static IEnumerable<EdgeEvent> Markers(ulong startSecond, int count) =>
        Pulses(startSecond, Enumerable.Repeat(800_000UL, count));

    /// <summary>The pulses of whole frames, minute after minute, from the plan's test vector on.</summary>
    private static IEnumerable<EdgeEvent> Frames(ulong startSecond, int count) =>
        Pulses(
            startSecond,
            Enumerable
                .Range(0, count)
                .SelectMany(minute =>
                    WwvbFrameEncoder
                        .Encode(
                            new DateTimeOffset(2026, 12, 10, 19, 30 + minute, 0, TimeSpan.Zero),
                            startMicros: 0,
                            Ut1Sign.Plus,
                            ut1Tenths: 0,
                            leapSecondPending: false,
                            DstStatus.Standard
                        )
                        .Symbols
                )
                .Select(kind =>
                    kind switch
                    {
                        SymbolKind.Marker => 800_000UL,
                        SymbolKind.One => 500_000UL,
                        _ => 200_000UL,
                    }
                )
        );

    private void SyntheticPlays(IEnumerable<EdgeEvent> script, bool thenEnds = false) =>
        _environment.Synthetic = _ => new FakeEdgeSource
        {
            Name = "synthetic",
            Script = [.. script],
            EndsAfterReset = thenEnds,
        };

    private FedSource SyntheticIsFed()
    {
        var source = new FedSource();
        _environment.Synthetic = _ => source;
        return source;
    }

    private void TheLoggerIsUnpluggedAndTheCaptureHolds(IEnumerable<EdgeEvent> script)
    {
        _settings.Remembered = HostSettings.Default with
        {
            Source = SourceKind.Serial,
            Port = "COM9",
            Running = true,
        };
        _environment.Replay = (_, _) =>
            new FakeEdgeSource { Name = "replay", Script = [.. script] };
    }

    private IEnumerable<SourceStatus> Statuses() =>
        _poster.Posted.Select(p => p.Payload).OfType<SourceStatus>();

    private int TimesWarned() =>
        Statuses().Count(status => status.Warning == PolarityWatch.Warning);

    /// <summary>Waits for something the pump thread does a moment after it is fed.</summary>
    private static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    [Fact]
    public async Task ManyMarkersAndNoFrame_WarnThatTheLevelLooksInverted()
    {
        // Arrange: 23 pulses give 22 markers, because a pulse is handed on at the next edge
        SyntheticPlays(Markers(startSecond: 10, count: 23));
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));
        await UntilAsync(() => TimesWarned() > 0);

        // Assert: the run goes on, with the warning in its status, in the document's words
        controller.Status.ShouldBe(
            new SourceStatus(SourceKind.Synthetic, "synthetic", SourceState.Running, "reset: start")
            {
                Warning =
                    "The reduced-carrier level looks inverted. Try the other level in the signal settings.",
            }
        );
        TimesWarned().ShouldBe(1, "it is sent once");
    }

    [Fact]
    public async Task TwentyMarkers_DoNotWarn()
    {
        // Arrange: 21 pulses give 20 markers, and then the source ends
        SyntheticPlays(Markers(startSecond: 10, count: 21), thenEnds: true);
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));
        await _poster.WaitForAsync(p => p.Payload is SourceStatus { State: SourceState.Stopped });

        // Assert
        Statuses().ShouldAllBe(status => status.Warning.Length == 0);
    }

    [Fact]
    public async Task ARealSignal_DoesNotWarn()
    {
        // Arrange: three minutes of frames, seven markers each
        SyntheticPlays(Frames(startSecond: 10, count: 3), thenEnds: true);
        await using SourceController controller = CreateController();
        int frames = 0;
        _pipeline.FrameSyncChanged += e => frames += e is FrameSyncEvent.FrameComplete ? 1 : 0;

        // Act
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));
        await _poster.WaitForAsync(p => p.Payload is SourceStatus { State: SourceState.Stopped });

        // Assert
        frames.ShouldBeGreaterThan(0, "the script has to be a signal the pipeline decodes");
        Statuses().ShouldAllBe(status => status.Warning.Length == 0);
    }

    [Fact]
    public async Task ChangingTheLevel_TakesTheWarningBackAtOnce()
    {
        // Arrange
        SyntheticPlays(Markers(startSecond: 10, count: 23));
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));
        await UntilAsync(() => TimesWarned() > 0);

        // Act: what the warning asks for
        await controller.HandleAsync(new Command.SetReducedCarrierLevel(PinLevel.Low));

        // Assert: in the command, not at the next symbol
        controller.Status.Warning.ShouldBeEmpty();
        controller.Status.State.ShouldBe(SourceState.Running);
        _poster.Posted[^1].Payload.ShouldBe(controller.Status);
    }

    [Fact]
    public async Task ChangingTheLevel_WithNoWarning_SendsNoStatus()
    {
        // Arrange
        SyntheticPlays(Markers(startSecond: 10, count: 5));
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));
        await _poster.WaitForAsync(p => p.Payload is SourceStatus { Detail: "reset: start" });
        int posted = _poster.Posted.Count;

        // Act
        await controller.HandleAsync(new Command.SetReducedCarrierLevel(PinLevel.Low));

        // Assert
        _poster.Posted.Count.ShouldBe(posted);
    }

    [Fact]
    public async Task ChangingTheLevel_StartsTheCountAgain()
    {
        // Arrange: the level looks inverted
        FedSource source = SyntheticIsFed();
        await using SourceController controller = CreateController();
        int symbols = 0;
        _pipeline.SymbolClassified += _ => Interlocked.Increment(ref symbols);
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));
        source.Feed(Markers(startSecond: 10, count: 23));
        await UntilAsync(() => TimesWarned() == 1 && Volatile.Read(ref symbols) == 22);

        // Act: the command sets the level it already had, so the pulses stay markers
        await controller.HandleAsync(new Command.SetReducedCarrierLevel(PinLevel.High));

        // Assert: fifteen more, in the same minute as the first 22, are not enough
        source.Feed(Markers(startSecond: 33, count: 15));
        await UntilAsync(() => Volatile.Read(ref symbols) == 37);
        controller.Status.Warning.ShouldBeEmpty();

        // Eight more make 23 since the change, and the warning is there again
        source.Feed(Markers(startSecond: 48, count: 8));
        await UntilAsync(() => TimesWarned() == 2);
        controller.Status.Warning.ShouldBe(PolarityWatch.Warning);
    }

    [Fact]
    public async Task ACompletedFrame_TakesTheWarningBack()
    {
        // Arrange: the markers, then whole frames. Frame sync needs the end of one frame to find
        // the start of the next.
        SyntheticPlays(
            Markers(startSecond: 10, count: 23).Concat(Frames(startSecond: 40, count: 3))
        );
        await using SourceController controller = CreateController();
        int frames = 0;
        _pipeline.FrameSyncChanged += e =>
        {
            if (e is FrameSyncEvent.FrameComplete)
            {
                Interlocked.Increment(ref frames);
            }
        };

        // Act
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));
        await UntilAsync(() => TimesWarned() == 1);
        await UntilAsync(() =>
            Volatile.Read(ref frames) > 0 && controller.Status.Warning.Length == 0
        );

        // Assert
        controller.Status.State.ShouldBe(SourceState.Running);
        TimesWarned().ShouldBe(1);
    }

    [Fact]
    public async Task ASourceReset_TakesTheWarningBackAndStartsTheCountAgain()
    {
        // Arrange
        FedSource source = SyntheticIsFed();
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));
        source.Feed(Markers(startSecond: 10, count: 23));
        await UntilAsync(() => TimesWarned() == 1);

        // Act: the logger starts a new session
        source.Feed([new EdgeEvent.Reset("session")]);
        var reset = await _poster.WaitForAsync(p =>
            p.Payload is SourceStatus { Detail: "reset: session" }
        );

        // Assert
        reset.Payload.ShouldBeOfType<SourceStatus>().Warning.ShouldBeEmpty();
        controller.Status.Warning.ShouldBeEmpty();

        // The stream after the reset is judged for itself
        source.Feed(Markers(startSecond: 40, count: 23));
        await UntilAsync(() => TimesWarned() == 2);
        controller.Status.Warning.ShouldBe(PolarityWatch.Warning);
    }

    [Fact]
    public async Task TheWarning_TakesThePlaceOfTheLaunchWarning_AndAChangeOfLevelGivesItBack()
    {
        // Arrange: the newest capture replays in place of the logger, and it reads inverted
        TheLoggerIsUnpluggedAndTheCaptureHolds(Markers(startSecond: 10, count: 23));
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));
        await UntilAsync(() => TimesWarned() == 1);
        await controller.HandleAsync(new Command.SetReducedCarrierLevel(PinLevel.Low));

        // Assert: one warning at a time, and the one that asks for something comes first
        Statuses()
            .Select(status => status.Warning)
            .Where(warning => warning.Length > 0)
            .Distinct()
            .ShouldBe([Launch, PolarityWatch.Warning]);
        controller.Status.Warning.ShouldBe(Launch);
    }

    [Fact]
    public async Task TheWarning_TakesThePlaceOfTheLaunchWarning_AndAFrameGivesItBack()
    {
        // Arrange
        TheLoggerIsUnpluggedAndTheCaptureHolds(
            Markers(startSecond: 10, count: 23).Concat(Frames(startSecond: 40, count: 3))
        );
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));
        await UntilAsync(() => TimesWarned() == 1);
        await UntilAsync(() => controller.Status.Warning != PolarityWatch.Warning);

        // Assert
        controller.Status.Warning.ShouldBe(Launch);
    }

    [Fact]
    public async Task ANewRun_StartsWithoutTheWarning()
    {
        // Arrange
        SyntheticPlays(Markers(startSecond: 10, count: 23));
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));
        await UntilAsync(() => TimesWarned() == 1);

        // Act
        await controller.HandleAsync(new Command.SelectSerial(Port: "COM5"));

        // Assert
        controller.Status.Warning.ShouldBeEmpty();
        controller.Status.Source.ShouldBe(SourceKind.Serial);
    }
}
