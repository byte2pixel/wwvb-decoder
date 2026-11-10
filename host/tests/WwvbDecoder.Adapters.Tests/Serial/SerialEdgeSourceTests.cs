namespace WwvbDecoder.Adapters.Tests;

public class SerialEdgeSourceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SerialEdgeSource Over(string text) => new("bench", new StringReader(text));

    private static async Task<List<EdgeEvent>> ReadAllText(string text)
    {
        await using SerialEdgeSource source = Over(text);
        return await source.ReadAsync(Ct).ToListAsync(Ct);
    }

    private static EdgeEvent.Sample Sample(ulong micros, PinLevel level) =>
        new(new Edge(micros, level));

    private static EdgeEvent.Reset Reset(string reason) => new(reason);

    private static EdgeEvent.Info Info(string line) => new(line);

    // 1
    [Fact]
    public async Task Name_ReaderConstructor_IsTheGivenName()
    {
        // Arrange
        await using SerialEdgeSource source = Over("");

        // Act & Assert
        source.Name.ShouldBe("bench");
    }

    // 2
    [Fact]
    public async Task Name_PortConstructor_IsSerialSpacePort()
    {
        // Arrange: no such port exists, so constructing proves the port is not opened here
        await using SerialEdgeSource source = new("COM250");

        // Act & Assert
        source.Name.ShouldBe("serial COM250");
    }

    // 3
    [Fact]
    public void Constructor_NullPortName_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new SerialEdgeSource(null!));
    }

    // 4
    [Fact]
    public void Constructor_NullName_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            new SerialEdgeSource(null!, new StringReader(""))
        );
    }

    // 5
    [Fact]
    public void Constructor_NullReader_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new SerialEdgeSource("bench", null!));
    }

    // 6
    [Fact]
    public async Task ReadAsync_EmptyReader_YieldsOnlyResetOpen()
    {
        // Act
        List<EdgeEvent> events = await ReadAllText("");

        // Assert
        events.ShouldBe([Reset("open")]);
    }

    // 7
    [Fact]
    public async Task ReadAsync_EdgeLines_YieldsSamplesInOrder()
    {
        // Act
        List<EdgeEvent> events = await ReadAllText("H 100\nL 300\n");

        // Assert
        events.ShouldBe([Reset("open"), Sample(100, PinLevel.High), Sample(300, PinLevel.Low)]);
    }

    // 8
    [Fact]
    public async Task ReadAsync_SessionLine_YieldsResetSession()
    {
        // Act
        List<EdgeEvent> events = await ReadAllText("# session\n");

        // Assert
        events.ShouldBe([Reset("open"), Reset("session")]);
    }

    // 9
    [Fact]
    public async Task ReadAsync_OtherHashLine_YieldsInfoWithTheWholeLine()
    {
        // Act
        List<EdgeEvent> events = await ReadAllText("# wwvb-edge-logger 0.1.0 pin=2\n");

        // Assert
        events.ShouldBe([Reset("open"), Info("# wwvb-edge-logger 0.1.0 pin=2")]);
    }

    // 10
    [Theory]
    [InlineData("X 12\n", 1, 1)]
    [InlineData("H 100\n\nL 200\n", 1, 3)]
    [InlineData("H 1.5\nH -1\nH\n", 3, 1)]
    public async Task ReadAsync_MalformedLines_AreSkippedAndCounted(
        string text,
        int malformed,
        int events
    )
    {
        // Arrange
        await using SerialEdgeSource source = Over(text);

        // Act
        List<EdgeEvent> read = await source.ReadAsync(Ct).ToListAsync(Ct);

        // Assert
        source.MalformedLines.ShouldBe(malformed);
        read.Count.ShouldBe(events);
    }

    // 11
    [Fact]
    public async Task ReadAsync_LineOver63Characters_IsMalformed()
    {
        // Arrange
        string line = "H 1" + new string('0', 61); // 64 characters
        await using SerialEdgeSource source = Over(line + "\n");

        // Act
        List<EdgeEvent> events = await source.ReadAsync(Ct).ToListAsync(Ct);

        // Assert
        source.MalformedLines.ShouldBe(1);
        events.ShouldBe([Reset("open")]);
    }

    // 12
    [Fact]
    public async Task ReadAsync_CarriageReturnLineFeed_IsReadAsALine()
    {
        // ReadLineAsync treats "\r\n" as one terminator, so a stray CR never reaches the codec here.
        // The firmware never sends one; a capture file keeps its CRs (CaptureFile.Open splits on "\n" only).
        // Arrange
        await using SerialEdgeSource source = Over("H 100\r\nL 300\r\n");

        // Act
        List<EdgeEvent> events = await source.ReadAsync(Ct).ToListAsync(Ct);

        // Assert
        source.MalformedLines.ShouldBe(0);
        events.ShouldBe([Reset("open"), Sample(100, PinLevel.High), Sample(300, PinLevel.Low)]);
    }

    // 13
    [Fact]
    public async Task ReadAsync_RawLineReceived_GetsEveryLineVerbatimInOrder()
    {
        // Arrange
        await using SerialEdgeSource source = Over(
            "# session\nH 100\nX 12\n\n# dropped 3\nL 300\n"
        );
        List<string> raw = [];
        source.RawLineReceived += raw.Add;

        // Act
        await source.ReadAsync(Ct).ToListAsync(Ct);

        // Assert
        raw.ShouldBe(["# session", "H 100", "X 12", "", "# dropped 3", "L 300"]);
    }

    // 14
    [Fact]
    public async Task ReadAsync_RawLineReceived_IsRaisedBeforeTheLinesEventIsYielded()
    {
        // Arrange
        await using SerialEdgeSource source = Over("H 100\n");
        List<string> order = [];
        source.RawLineReceived += line => order.Add("raw " + line);

        // Act
        await foreach (EdgeEvent evt in source.ReadAsync(Ct))
        {
            order.Add("event " + evt.GetType().Name);
        }

        // Assert
        order.ShouldBe(["event Reset", "raw H 100", "event Sample"]);
    }

    // 15
    [Fact]
    public async Task ReadAsync_SmallerTimestamp_InsertsResetBeforeTheSample()
    {
        // Act
        List<EdgeEvent> events = await ReadAllText("H 300\nL 200\n");

        // Assert
        events.ShouldBe([
            Reset("open"),
            Sample(300, PinLevel.High),
            Reset("timestamp went backwards"),
            Sample(200, PinLevel.Low),
        ]);
    }

    // 16
    [Fact]
    public async Task ReadAsync_EqualTimestamp_IsNotBackwards()
    {
        // Act
        List<EdgeEvent> events = await ReadAllText("H 300\nL 300\n");

        // Assert
        events.ShouldBe([Reset("open"), Sample(300, PinLevel.High), Sample(300, PinLevel.Low)]);
    }

    // 17
    [Fact]
    public async Task ReadAsync_SmallerTimestampAfterSession_IsNotBackwards()
    {
        // Act
        List<EdgeEvent> events = await ReadAllText("H 300\n# session\nL 200\n");

        // Assert
        events.ShouldBe([
            Reset("open"),
            Sample(300, PinLevel.High),
            Reset("session"),
            Sample(200, PinLevel.Low),
        ]);
    }

    // 18
    [Fact]
    public async Task ReadAsync_SmallerTimestampAfterInfo_IsStillBackwards()
    {
        // Act
        List<EdgeEvent> events = await ReadAllText("H 300\n# dropped 3\nL 200\n");

        // Assert
        events.ShouldBe([
            Reset("open"),
            Sample(300, PinLevel.High),
            Info("# dropped 3"),
            Reset("timestamp went backwards"),
            Sample(200, PinLevel.Low),
        ]);
    }

    // 19
    [Fact]
    public async Task ReadAsync_AfterBackwardsReset_TheSmallerTimestampIsTheNewBaseline()
    {
        // Act
        List<EdgeEvent> events = await ReadAllText("H 300\nL 200\nH 250\nL 100\n");

        // Assert
        events.ShouldBe([
            Reset("open"),
            Sample(300, PinLevel.High),
            Reset("timestamp went backwards"),
            Sample(200, PinLevel.Low),
            Sample(250, PinLevel.High),
            Reset("timestamp went backwards"),
            Sample(100, PinLevel.Low),
        ]);
    }

    // 20
    [Fact]
    public async Task ReadAsync_BaselineFollowsEveryTimestamp_NotJustTheFirst()
    {
        // Act
        List<EdgeEvent> events = await ReadAllText("H 100\nL 300\nH 200\n");

        // Assert
        events.ShouldBe([
            Reset("open"),
            Sample(100, PinLevel.High),
            Sample(300, PinLevel.Low),
            Reset("timestamp went backwards"),
            Sample(200, PinLevel.High),
        ]);
    }

    // 21
    [Fact]
    public async Task ReadAsync_CancelledBeforeStart_ThrowsOperationCanceled()
    {
        // Arrange
        await using SerialEdgeSource source = Over("H 100\nL 300\n");
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(() =>
            source.ReadAsync(cts.Token).ToListAsync(Ct).AsTask()
        );
    }

    // 22
    [Fact]
    public async Task ReadAsync_CancelledMidStream_ThrowsOperationCanceledOnTheNextLine()
    {
        // Arrange
        await using SerialEdgeSource source = Over("H 100\nL 300\nH 1100\n");
        using CancellationTokenSource cts = new();
        List<EdgeEvent> seen = [];

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (EdgeEvent evt in source.ReadAsync(cts.Token))
            {
                seen.Add(evt);
                if (seen.Count == 2)
                {
                    await cts.CancelAsync();
                }
            }
        });
        seen.ShouldBe([Reset("open"), Sample(100, PinLevel.High)]);
    }

    // 23
    [Fact]
    public async Task ReadAsync_ReaderThrows_ThePipelineSeesTheException()
    {
        // Arrange
        await using SerialEdgeSource source = new("bench", new ThrowingReader("H 100\n"));

        // Act & Assert
        IOException ex = await Should.ThrowAsync<IOException>(() =>
            source.ReadAsync(Ct).ToListAsync(Ct).AsTask()
        );
        ex.Message.ShouldBe("port gone");
    }

    // 24
    [Fact]
    public async Task DisposeAsync_DisposesTheReader()
    {
        // Arrange
        StringReader reader = new("H 100\n");
        SerialEdgeSource source = new("bench", reader);

        // Act
        await source.DisposeAsync();

        // Assert
        Should.Throw<ObjectDisposedException>(() => reader.ReadLine());
    }

    // 25
    [Fact]
    public async Task ReadAsync_SecondEnumeration_StartsOverWithAFreshBaselineAndTheSameReader()
    {
        // Arrange: the reader runs dry, is refilled with a smaller timestamp, and is read again
        RefillableReader reader = new("H 300\n");
        await using SerialEdgeSource source = new("bench", reader);
        List<EdgeEvent> first = await source.ReadAsync(Ct).ToListAsync(Ct);
        reader.Refill("L 200\n");

        // Act
        List<EdgeEvent> second = await source.ReadAsync(Ct).ToListAsync(Ct);

        // Assert
        first.ShouldBe([Reset("open"), Sample(300, PinLevel.High)]);
        second.ShouldBe([Reset("open"), Sample(200, PinLevel.Low)]);
    }

    /// <summary>
    /// Reports end of stream when its text runs out, and can be given more text afterwards,
    /// so one reader can serve two enumerations.
    /// </summary>
    private sealed class RefillableReader(string text) : TextReader
    {
        private StringReader _inner = new(text);

        public void Refill(string text) => _inner = new StringReader(text);

        public override string? ReadLine() => _inner.ReadLine();

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested
                ? ValueTask.FromCanceled<string?>(cancellationToken)
                : new(ReadLine());
    }

    /// <summary>
    /// Yields its text, then throws <see cref="IOException"/> instead of reporting end of stream.
    /// </summary>
    private sealed class ThrowingReader(string text) : TextReader
    {
        private readonly StringReader _inner = new(text);

        public override string ReadLine() =>
            _inner.ReadLine() ?? throw new IOException("port gone");

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            new(ReadLine());
    }
}
