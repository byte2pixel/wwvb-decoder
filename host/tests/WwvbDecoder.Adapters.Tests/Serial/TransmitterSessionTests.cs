using Microsoft.Extensions.Time.Testing;

namespace WwvbDecoder.Adapters.Tests;

public sealed class TransmitterSessionTests : IDisposable
{
    private const string Banner = "# wwvb-transmitter 0.1.0 pin=16";

    // What the transmitter writes when a host opens the port.
    private const string SessionStart = "# session\n" + Banner + "\n";

    private const string OkReply = "OK time=1796931000 carrier=on custom=none";

    // 2026-12-10T19:29:59Z. The second after it is 1796931000, the time in the contract's example.
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(
        1_796_930_999
    );

    // The writer's own NewLine is CRLF on purpose: a command must still end with "\n" only.
    private readonly FlushedWriter _sent = new() { NewLine = "\r\n" };
    private readonly FakeTimeProvider _time = new(Start);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private TransmitterSession Over(string received) =>
        new(new StringReader(received), _sent, _time);

    // A task that the fake clock should have finished. Waiting on the real clock turns a hang into
    // a failure, and one that no test can take for the session's own TimeoutException.
    private static Task<T> Soon<T>(Task<T> task) => Within(TimeSpan.FromSeconds(5), task);

    // The same with little patience, for a timeout that the fake clock has already set off: one
    // that runs on the real clock is still seconds away.
    private static Task<T> AtOnce<T>(Task<T> task) => Within(TimeSpan.FromMilliseconds(500), task);

    private static async Task<T> Within<T>(TimeSpan patience, Task<T> task)
    {
        Task first = await Task.WhenAny(task, Task.Delay(patience, Ct));
        if (first != task)
        {
            Assert.Fail("The task did not finish.");
        }

        return await task;
    }

    private static TimeSpan Milliseconds(int count) => TimeSpan.FromMilliseconds(count);

    // Sets the time from a clock that is 250 ms into the second 19:29:59, and lets the second turn.
    private Task<TransmitterReply> SetTimeAtTheTurn(
        TransmitterSession session,
        long offsetSeconds = 0
    )
    {
        _time.Advance(Milliseconds(250));
        Task<TransmitterReply> set = session.SetTimeAsync(offsetSeconds, Ct);
        _time.Advance(Milliseconds(750));
        return Soon(set);
    }

    // 1
    [Fact]
    public void Constructor_NullReader_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new TransmitterSession(null!, _sent, _time));
    }

    // 2
    [Fact]
    public void Constructor_NullWriter_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            new TransmitterSession(new StringReader(""), null!, _time)
        );
    }

    // 3
    [Fact]
    public void Constructor_NullTimeProvider_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            new TransmitterSession(new StringReader(""), _sent, null!)
        );
    }

    // 4
    [Fact]
    public async Task ReadBannerAsync_SessionThenBanner_ReturnsTheBanner()
    {
        // Arrange
        TransmitterSession session = Over(SessionStart);

        // Act
        string banner = await session.ReadBannerAsync(Ct);

        // Assert
        banner.ShouldBe(Banner);
    }

    // 5
    [Theory]
    // A reply of an earlier session that its host never read.
    [InlineData("OK time=1796931000 carrier=on custom=none\n")]
    // A banner with no session line before it is not this session's.
    [InlineData("# wwvb-transmitter 0.0.9 pin=1\n")]
    [InlineData("# wwvb-edge-logger 0.1.0 pin=2\nH 12000000\n")]
    // Only the exact line starts a session.
    [InlineData("# sessions\n")]
    [InlineData("\n")]
    public async Task ReadBannerAsync_LinesBeforeTheSessionLine_AreIgnored(string before)
    {
        // Arrange
        TransmitterSession session = Over(before + SessionStart);

        // Act
        string banner = await session.ReadBannerAsync(Ct);

        // Assert
        banner.ShouldBe(Banner);
    }

    // 6
    [Fact]
    public async Task ReadBannerAsync_EdgeLoggersBanner_ThrowsNamingTheEdgeLogger()
    {
        // Arrange
        TransmitterSession session = Over("# session\n# wwvb-edge-logger 0.1.0 pin=2\n");

        // Act
        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            session.ReadBannerAsync(Ct)
        );

        // Assert
        exception.Message.ShouldContain("edge logger");
    }

    // 7
    [Theory]
    [InlineData("H 12000000")]
    [InlineData("OK time=unset carrier=on custom=none")]
    // The banner's name ends at a space.
    [InlineData("# wwvb-transmitters 0.1.0 pin=16")]
    public async Task ReadBannerAsync_AnyOtherLineAfterTheSessionLine_ThrowsQuotingIt(string line)
    {
        // Arrange
        TransmitterSession session = Over("# session\n" + line + "\n");

        // Act
        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            session.ReadBannerAsync(Ct)
        );

        // Assert
        exception.Message.ShouldContain(line);
    }

    // 8
    [Theory]
    [InlineData("")]
    [InlineData("OK time=unset carrier=on custom=none\n")]
    [InlineData("# session\n")]
    public async Task ReadBannerAsync_LinesEndBeforeTheBanner_ThrowsEndOfStream(string received)
    {
        // Arrange
        TransmitterSession session = Over(received);

        // Act & Assert
        await Should.ThrowAsync<EndOfStreamException>(() => session.ReadBannerAsync(Ct));
    }

    // 9
    [Fact]
    public async Task ReadBannerAsync_SendsNothing()
    {
        // Arrange
        TransmitterSession session = Over(SessionStart);

        // Act
        await session.ReadBannerAsync(Ct);

        // Assert
        _sent.ToString().ShouldBeEmpty();
    }

    // 10
    [Theory]
    [InlineData(1)]
    [InlineData(250)]
    [InlineData(750)]
    [InlineData(999)]
    public async Task SetTimeAsync_SendsTheNextSecondAtTheMomentItStarts(int millisecondsIn)
    {
        // Arrange: the clock is this far into the second 19:29:59
        _time.Advance(Milliseconds(millisecondsIn));
        TransmitterSession session = Over(OkReply + "\n");

        // Act: up to one millisecond before the second turns, then the turn
        Task<TransmitterReply> set = session.SetTimeAsync(0, Ct);
        _time.Advance(Milliseconds(1000 - millisecondsIn - 1));
        string sentBefore = _sent.ToString();
        bool doneBefore = set.IsCompleted;
        _time.Advance(Milliseconds(1));
        await Soon(set);

        // Assert
        sentBefore.ShouldBeEmpty();
        doneBefore.ShouldBeFalse();
        _sent.ToString().ShouldBe("TIME 1796931000\n");
    }

    // 11
    [Theory]
    [InlineData(86_400, "TIME 1797017400\n")]
    [InlineData(-3_600, "TIME 1796927400\n")]
    public async Task SetTimeAsync_Offset_IsAddedToTheTimeSentAndNotToTheWait(
        long offsetSeconds,
        string sent
    )
    {
        // Arrange
        TransmitterSession session = Over(OkReply + "\n");

        // Act
        await SetTimeAtTheTurn(session, offsetSeconds);

        // Assert
        _sent.ToString().ShouldBe(sent);
    }

    // 12
    [Theory]
    [InlineData(OkReply, true)]
    [InlineData("ERR time is outside 2000 to 2099", false)]
    public async Task SetTimeAsync_ReturnsTheTransmittersReply(string line, bool ok)
    {
        // Arrange
        TransmitterSession session = Over(line + "\n");

        // Act
        TransmitterReply reply = await SetTimeAtTheTurn(session);

        // Assert
        reply.ShouldBe(new TransmitterReply(line));
        reply.Ok.ShouldBe(ok);
    }

    // 13
    [Fact]
    public async Task SetTimeAsync_AsksForTheReplyOnlyAfterTheCommandIsFlushed()
    {
        // Arrange: a port's writer holds a command back until it is flushed, and the transmitter
        // answers nothing it has not received
        using var reader = new ReplyOnceSent(() => _sent.Flushed.EndsWith('\n'), OkReply);
        var session = new TransmitterSession(reader, _sent, _time);

        // Act
        TransmitterReply reply = await SetTimeAtTheTurn(session);

        // Assert
        reply.Line.ShouldBe(OkReply);
        _sent.Flushed.ShouldBe("TIME 1796931000\n");
    }

    // 14
    [Fact]
    public async Task SetTimeAsync_NoReply_ThrowsEndOfStream()
    {
        // Arrange
        TransmitterSession session = Over("");

        // Act & Assert
        await Should.ThrowAsync<EndOfStreamException>(() => SetTimeAtTheTurn(session));
    }

    // 15
    [Fact]
    public async Task SetTimeAsync_CancelledWhileItWaits_ThrowsAndSendsNothing()
    {
        // Arrange
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        TransmitterSession session = Over(OkReply + "\n");
        _time.Advance(Milliseconds(250));

        // Act
        Task<TransmitterReply> set = session.SetTimeAsync(0, cancel.Token);
        await cancel.CancelAsync();

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(() => Soon(set));
        _time.Advance(Milliseconds(750));
        _sent.ToString().ShouldBeEmpty();
    }

    // 16
    [Fact]
    public async Task SetTimeAsync_WaitsOnTheGivenClock()
    {
        // Arrange: almost a whole second to wait
        _time.Advance(Milliseconds(1));
        TransmitterSession session = Over(OkReply + "\n");

        // Act
        Task<TransmitterReply> set = session.SetTimeAsync(0, Ct);
        _time.Advance(Milliseconds(999));

        // Assert: the given clock has turned, so it is done long before the real one turns
        TransmitterReply reply = await set.WaitAsync(Milliseconds(500), Ct);
        reply.Ok.ShouldBeTrue();
    }

    // 17
    [Fact]
    public async Task StatusAsync_SendsStatusAtOnceAndReturnsTheReply()
    {
        // Arrange: the clock is not moved, so nothing here waits for a second to turn
        using var reader = new ReplyOnceSent(() => _sent.Flushed.EndsWith('\n'), OkReply);
        var session = new TransmitterSession(reader, _sent, _time);

        // Act
        TransmitterReply reply = await Soon(session.StatusAsync(Ct));

        // Assert
        reply.ShouldBe(new TransmitterReply(OkReply));
        _sent.Flushed.ShouldBe("STATUS\n");
    }

    // 18
    [Fact]
    public async Task StatusAsync_NoReply_ThrowsEndOfStream()
    {
        // Arrange
        TransmitterSession session = Over("");

        // Act & Assert
        await Should.ThrowAsync<EndOfStreamException>(() => Soon(session.StatusAsync(Ct)));
    }

    // 19
    [Fact]
    public async Task ASession_BannerThenTimeThenStatus_EachReadsItsOwnLine()
    {
        // Arrange
        const string StatusReply = "OK time=1796931001 carrier=on custom=none";
        TransmitterSession session = Over(SessionStart + OkReply + "\n" + StatusReply + "\n");

        // Act
        string banner = await Soon(session.ReadBannerAsync(Ct));
        TransmitterReply timeReply = await SetTimeAtTheTurn(session);
        TransmitterReply statusReply = await Soon(session.StatusAsync(Ct));

        // Assert
        banner.ShouldBe(Banner);
        timeReply.Line.ShouldBe(OkReply);
        statusReply.Line.ShouldBe(StatusReply);
        _sent.ToString().ShouldBe("TIME 1796931000\nSTATUS\n");
    }

    // 20
    [Fact]
    public async Task ReadBannerAsync_NothingArrives_TimesOutAfterTheReplyTimeout()
    {
        // Arrange
        using var silent = new FedReader();
        var session = new TransmitterSession(silent, _sent, _time);

        // Act
        Task<string> read = session.ReadBannerAsync(Ct);
        _time.Advance(TransmitterSession.ReplyTimeout - Milliseconds(1));
        bool doneBefore = read.IsCompleted;
        _time.Advance(Milliseconds(1));

        // Assert
        doneBefore.ShouldBeFalse();
        await Should.ThrowAsync<TimeoutException>(() => AtOnce(read));
    }

    // 21
    [Fact]
    public async Task ReadBannerAsync_OtherLinesKeepArriving_StillTimesOutAfterTheReplyTimeout()
    {
        // Arrange: a device that talks, but never starts a session
        using var chatty = new FedReader();
        var session = new TransmitterSession(chatty, _sent, _time);

        // Act: the timeout is for the banner, not for each line
        Task<string> read = session.ReadBannerAsync(Ct);
        chatty.Feed("H 12000000");
        _time.Advance(TransmitterSession.ReplyTimeout - Milliseconds(1));
        chatty.Feed("L 12200412");
        _time.Advance(Milliseconds(1));

        // Assert
        await Should.ThrowAsync<TimeoutException>(() => AtOnce(read));
    }

    // 22
    [Fact]
    public async Task ReadBannerAsync_BannerJustInTime_ReturnsIt()
    {
        // Arrange
        using var slow = new FedReader();
        var session = new TransmitterSession(slow, _sent, _time);

        // Act
        Task<string> read = session.ReadBannerAsync(Ct);
        _time.Advance(TransmitterSession.ReplyTimeout - Milliseconds(1));
        slow.Feed("# session");
        slow.Feed(Banner);

        // Assert
        (await Soon(read)).ShouldBe(Banner);
    }

    // 23
    [Fact]
    public async Task ReadBannerAsync_CancelledWhileItWaits_Throws()
    {
        // Arrange: a port's read does not always stop when it is cancelled, and neither does this
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var silent = new FedReader();
        var session = new TransmitterSession(silent, _sent, _time);

        // Act
        Task<string> read = session.ReadBannerAsync(cancel.Token);
        await cancel.CancelAsync();

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(() => Soon(read));
    }

    // 24
    [Fact]
    public async Task SetTimeAsync_NoReplyArrives_TimesOutThatLongAfterTheCommand()
    {
        // Arrange
        using var silent = new FedReader();
        var session = new TransmitterSession(silent, _sent, _time);
        _time.Advance(Milliseconds(250));

        // Act: the wait for the second to turn is not part of the timeout
        Task<TransmitterReply> set = session.SetTimeAsync(0, Ct);
        _time.Advance(Milliseconds(750));
        string sentAtTheTurn = _sent.Flushed;
        _time.Advance(TransmitterSession.ReplyTimeout - Milliseconds(1));
        bool doneBefore = set.IsCompleted;
        _time.Advance(Milliseconds(1));

        // Assert
        sentAtTheTurn.ShouldBe("TIME 1796931000\n");
        doneBefore.ShouldBeFalse();
        await Should.ThrowAsync<TimeoutException>(() => AtOnce(set));
    }

    // 25
    [Fact]
    public async Task StatusAsync_NoReplyArrives_TimesOutAfterTheReplyTimeout()
    {
        // Arrange
        using var silent = new FedReader();
        var session = new TransmitterSession(silent, _sent, _time);

        // Act
        Task<TransmitterReply> status = session.StatusAsync(Ct);
        _time.Advance(TransmitterSession.ReplyTimeout - Milliseconds(1));
        bool doneBefore = status.IsCompleted;
        _time.Advance(Milliseconds(1));

        // Assert
        doneBefore.ShouldBeFalse();
        await Should.ThrowAsync<TimeoutException>(() => AtOnce(status));
    }

    // 26
    [Fact]
    public async Task StatusAsync_ReplyJustInTime_ReturnsIt()
    {
        // Arrange
        using var slow = new FedReader();
        var session = new TransmitterSession(slow, _sent, _time);

        // Act
        Task<TransmitterReply> status = session.StatusAsync(Ct);
        _time.Advance(TransmitterSession.ReplyTimeout - Milliseconds(1));
        slow.Feed(OkReply);

        // Assert
        (await Soon(status)).Line.ShouldBe(OkReply);
    }

    public void Dispose()
    {
        _sent.Dispose();
    }

    // A port that the test feeds: a read waits until a line is given, and goes on waiting when it
    // is cancelled, as a port's read can.
    private sealed class FedReader : TextReader
    {
        private readonly System.Threading.Channels.Channel<string> _lines =
            System.Threading.Channels.Channel.CreateUnbounded<string>();

        public void Feed(string line) => _lines.Writer.TryWrite(line).ShouldBeTrue();

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            ReadAsync();

        public override Task<string?> ReadLineAsync() => ReadAsync().AsTask();

        private async ValueTask<string?> ReadAsync() =>
            await _lines.Reader.ReadAsync(CancellationToken.None);
    }

    // Remembers what had been written when it was last flushed.
    private sealed class FlushedWriter : StringWriter
    {
        public string Flushed { get; private set; } = "";

        public override void Flush() => Flushed = ToString();

        public override Task FlushAsync()
        {
            Flush();
            return Task.CompletedTask;
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => FlushAsync();
    }

    // Gives one reply line, and fails the test if it is asked before the command went out.
    private sealed class ReplyOnceSent(Func<bool> sent, string reply) : TextReader
    {
        public override string ReadLine() =>
            sent()
                ? reply
                : throw new InvalidOperationException(
                    "The reply was asked for before the command was flushed."
                );

        public override Task<string?> ReadLineAsync() => Task.FromResult<string?>(ReadLine());

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            new(ReadLine());
    }
}
