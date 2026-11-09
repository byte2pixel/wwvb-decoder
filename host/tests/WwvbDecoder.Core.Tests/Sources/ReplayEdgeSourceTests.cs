using Microsoft.Extensions.Time.Testing;

namespace WwvbDecoder.Core.Tests;

public class ReplayEdgeSourceTests
{
    private readonly FakeTimeProvider _timeProvider = new();

    // The example capture from docs/design/capture-format.md.
    private static readonly string[] Example =
    [
        "# capture v1",
        "# recorded 2026-11-02T03:10:27Z",
        "# source serial COM5, WVB-0860N-03A receiver #1, home west window on 5 m extension",
        "# reduced-carrier-level H",
        "# session",
        "# wwvb-edge-logger 0.1.0 pin=2",
        "H 12000000",
        "L 12200412",
        "H 13000003",
        "L 13800377",
        "H 14000011",
        "L 14500398",
        "# wall 2026-11-02T03:11:00Z 14500398",
        "H 15000006",
        "L 15200401",
        "H 16000002",
        "L 16800390",
    ];

    private async Task<List<EdgeEvent>> ReadAllUnpaced(
        IReadOnlyList<string> lines,
        bool loop = false
    )
    {
        ReplayEdgeSource source = new(lines, _timeProvider) { Speed = 0, Loop = loop };
        return await source
            .ReadAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private static readonly string[] Header =
    [
        "# capture v1",
        "# recorded 2026-11-02T03:10:27Z",
        "# source test",
    ];

    [Fact]
    public void Constructor_ParsesHeaderAndExposesDefaults()
    {
        // Arrange & Act
        ReplayEdgeSource source = new(Header, _timeProvider);

        // Assert
        source.Name.ShouldBe("replay");
        source.Info.Recorded.ShouldBe(new DateTimeOffset(2026, 11, 02, 03, 10, 27, TimeSpan.Zero));
        source.Info.Source.ShouldBe("test");
        source.Speed.ShouldBe(1.0);
        source.Loop.ShouldBeFalse();
        source.MalformedLines.ShouldBe(0);
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        Should.Throw<ArgumentNullException>(() => new ReplayEdgeSource(null!, _timeProvider));
        Should.Throw<ArgumentNullException>(() => new ReplayEdgeSource(Header, null!));
    }

    [Fact]
    public void Constructor_InvalidHeader_ThrowsFormatException()
    {
        Should.Throw<FormatException>(() => new ReplayEdgeSource(["H 1"], _timeProvider));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void Speed_Invalid_Throws(double speed)
    {
        ReplayEdgeSource source = new(Header, _timeProvider);
        Should.Throw<ArgumentOutOfRangeException>(() => source.Speed = speed);
    }

    [Fact]
    public async Task ReadAsync_Unpaced_YieldsStartThenBodyEventsInOrder()
    {
        // Act
        List<EdgeEvent> events = await ReadAllUnpaced(Example);

        // Assert
        events.ShouldBe([
            new EdgeEvent.Reset("start"),
            new EdgeEvent.Reset("session"),
            new EdgeEvent.Info("# wwvb-edge-logger 0.1.0 pin=2"),
            new EdgeEvent.Sample(new Edge(12000000, PinLevel.High)),
            new EdgeEvent.Sample(new Edge(12200412, PinLevel.Low)),
            new EdgeEvent.Sample(new Edge(13000003, PinLevel.High)),
            new EdgeEvent.Sample(new Edge(13800377, PinLevel.Low)),
            new EdgeEvent.Sample(new Edge(14000011, PinLevel.High)),
            new EdgeEvent.Sample(new Edge(14500398, PinLevel.Low)),
            new EdgeEvent.Info("# wall 2026-11-02T03:11:00Z 14500398"),
            new EdgeEvent.Sample(new Edge(15000006, PinLevel.High)),
            new EdgeEvent.Sample(new Edge(15200401, PinLevel.Low)),
            new EdgeEvent.Sample(new Edge(16000002, PinLevel.High)),
            new EdgeEvent.Sample(new Edge(16800390, PinLevel.Low)),
        ]);
    }

    [Fact]
    public async Task ReadAsync_MalformedLines_CountedAndSkipped()
    {
        // Arrange
        string[] lines = [.. Header, "H 100", "garbage", "", "X 200", "L 300"];
        ReplayEdgeSource source = new(lines, _timeProvider) { Speed = 0 };

        // Act
        List<EdgeEvent> events = await source
            .ReadAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        events.ShouldBe([
            new EdgeEvent.Reset("start"),
            new EdgeEvent.Sample(new Edge(100, PinLevel.High)),
            new EdgeEvent.Sample(new Edge(300, PinLevel.Low)),
        ]);
        source.MalformedLines.ShouldBe(3);
    }

    [Fact]
    public async Task ReadAsync_TimestampGoesBackwards_InsertsResetBeforeSample()
    {
        // Arrange
        string[] lines = [.. Header, "H 100", "L 200", "H 150", "L 250"];

        // Act
        List<EdgeEvent> events = await ReadAllUnpaced(lines);

        // Assert
        events.ShouldBe([
            new EdgeEvent.Reset("start"),
            new EdgeEvent.Sample(new Edge(100, PinLevel.High)),
            new EdgeEvent.Sample(new Edge(200, PinLevel.Low)),
            new EdgeEvent.Reset("timestamp went backwards"),
            new EdgeEvent.Sample(new Edge(150, PinLevel.High)),
            new EdgeEvent.Sample(new Edge(250, PinLevel.Low)),
        ]);
    }

    [Fact]
    public async Task ReadAsync_SessionResetClearsBaseline_NoBackwardsReset()
    {
        // Arrange
        string[] lines = [.. Header, "H 100", "L 200", "# session", "H 50", "L 60"];

        // Act
        List<EdgeEvent> events = await ReadAllUnpaced(lines);

        // Assert
        events.ShouldBe([
            new EdgeEvent.Reset("start"),
            new EdgeEvent.Sample(new Edge(100, PinLevel.High)),
            new EdgeEvent.Sample(new Edge(200, PinLevel.Low)),
            new EdgeEvent.Reset("session"),
            new EdgeEvent.Sample(new Edge(50, PinLevel.High)),
            new EdgeEvent.Sample(new Edge(60, PinLevel.Low)),
        ]);
    }

    [Fact]
    public async Task ReadAsync_Loop_EmitsLoopResetAndReplaysBody()
    {
        // Arrange
        string[] lines = [.. Header, "# reduced-carrier-level H", "H 100", "L 200"];
        ReplayEdgeSource source = new(lines, _timeProvider) { Speed = 0, Loop = true };

        // Act
        List<EdgeEvent> events = await source
            .ReadAsync(TestContext.Current.CancellationToken)
            .Take(7)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        events.ShouldBe([
            new EdgeEvent.Reset("start"),
            new EdgeEvent.Sample(new Edge(100, PinLevel.High)),
            new EdgeEvent.Sample(new Edge(200, PinLevel.Low)),
            new EdgeEvent.Reset("loop"),
            new EdgeEvent.Sample(new Edge(100, PinLevel.High)),
            new EdgeEvent.Sample(new Edge(200, PinLevel.Low)),
            new EdgeEvent.Reset("loop"),
        ]);
    }

    [Fact]
    public async Task ReadAsync_NoLoop_CompletesAtEnd()
    {
        // Arrange
        string[] lines = [.. Header, "H 100", "L 200"];

        // Act
        List<EdgeEvent> events = await ReadAllUnpaced(lines);

        // Assert
        events.Count.ShouldBe(3);
        events[^1].ShouldBe(new EdgeEvent.Sample(new Edge(200, PinLevel.Low)));
    }

    [Fact(Timeout = 5000)]
    public async Task ReadAsync_Loop_HonorsCancellation()
    {
        // Arrange
        string[] lines = [.. Header, "H 100", "L 200"];
        ReplayEdgeSource source = new(lines, _timeProvider) { Speed = 0, Loop = true };
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        int seen = 0;

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (EdgeEvent _ in source.ReadAsync(cts.Token))
            {
                if (++seen == 10)
                {
                    await cts.CancelAsync();
                }
            }
        });
        seen.ShouldBe(10);
    }
}
