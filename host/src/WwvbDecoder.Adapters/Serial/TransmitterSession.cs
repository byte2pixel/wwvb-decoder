namespace WwvbDecoder.Adapters;

/// <summary>
/// One session with the desk-top transmitter: the lines it sends and the commands it takes.
/// </summary>
public sealed class TransmitterSession
{
    /// <summary>
    /// How long the transmitter is given to answer: with its banner after the port opens, and with
    /// its reply after a command.
    /// </summary>
    public static TimeSpan ReplyTimeout { get; } = TimeSpan.FromSeconds(3);

    private const string SessionStart = "# session";
    private const string WwvbTransmitter = "# wwvb-transmitter ";
    private const string WwvbEdgeLogger = "# wwvb-edge-logger ";
    private const string StatusCommand = "STATUS";

    private readonly TextReader _reader;
    private readonly TextWriter _writer;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransmitterSession"/> class.
    /// </summary>
    /// <param name="reader">The lines the transmitter sends.</param>
    /// <param name="writer">Where the commands go.</param>
    /// <param name="timeProvider">The clock whose time the transmitter is given.</param>
    public TransmitterSession(TextReader reader, TextWriter writer, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _reader = reader;
        _writer = writer;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Reads up to the start of the session and returns the transmitter's banner.
    /// </summary>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The banner line, for example <c># wwvb-transmitter 0.1.0 pin=16</c>.</returns>
    /// <exception cref="InvalidDataException">The device is not the transmitter.</exception>
    /// <exception cref="EndOfStreamException">The lines ended before the banner.</exception>
    /// <exception cref="TimeoutException">No banner came within <see cref="ReplyTimeout"/>.</exception>
    public async Task<string> ReadBannerAsync(CancellationToken ct)
    {
        return await ReadBannerCore(ct).WaitAsync(ReplyTimeout, _timeProvider, ct);
    }

    private async Task<string> ReadBannerCore(CancellationToken ct)
    {
        string? line = await _reader.ReadLineAsync(ct);
        while (line != null)
        {
            if (line == SessionStart)
            {
                line =
                    await _reader.ReadLineAsync(ct)
                    ?? throw new EndOfStreamException(
                        "The transmitter closed the port before sending its banner."
                    );

                if (line.StartsWith(WwvbTransmitter, StringComparison.Ordinal))
                {
                    return line;
                }

                if (line.StartsWith(WwvbEdgeLogger, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The device is an edge logger, not a transmitter."
                    );
                }

                throw new InvalidDataException($"The device is not the transmitter: {line}");
            }

            line = await _reader.ReadLineAsync(ct);
        }

        throw new EndOfStreamException("The transmitter did not send a banner.");
    }

    /// <summary>
    /// Waits for the top of the next second of the clock and sets the transmitter's clock to it.
    /// </summary>
    /// <param name="offsetSeconds">Seconds to add to the time that is sent.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The transmitter's reply.</returns>
    /// <exception cref="EndOfStreamException">The lines ended before the reply.</exception>
    /// <exception cref="TimeoutException">No reply came within <see cref="ReplyTimeout"/>.</exception>
    public async Task<TransmitterReply> SetTimeAsync(long offsetSeconds, CancellationToken ct)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset nextSecond = now.AddSeconds(1)
            .AddTicks(-now.Ticks % TimeSpan.TicksPerSecond);
        TimeSpan wait = nextSecond - now;
        await Task.Delay(wait, _timeProvider, ct);

        DateTimeOffset timeToSend = nextSecond.AddSeconds(offsetSeconds);
        string command = $"TIME {timeToSend.ToUnixTimeSeconds()}";
        return await SendAndReceiveAsync(command, ct);
    }

    /// <summary>
    /// Asks the transmitter for its state.
    /// </summary>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The transmitter's reply.</returns>
    /// <exception cref="EndOfStreamException">The lines ended before the reply.</exception>
    /// <exception cref="TimeoutException">No reply came within <see cref="ReplyTimeout"/>.</exception>
    public async Task<TransmitterReply> StatusAsync(CancellationToken ct)
    {
        return await SendAndReceiveAsync(StatusCommand, ct);
    }

    // Sends one command line and returns the transmitter's reply to it.
    private async Task<TransmitterReply> SendAndReceiveAsync(string command, CancellationToken ct)
    {
        await _writer.WriteAsync($"{command}\n".AsMemory(), ct);
        await _writer.FlushAsync(ct);
        string? replyLine = await _reader
            .ReadLineAsync(ct)
            .AsTask()
            .WaitAsync(ReplyTimeout, _timeProvider, ct);
        return replyLine is null
            ? throw new EndOfStreamException("The transmitter closed the port before replying.")
            : new TransmitterReply(replyLine);
    }
}
