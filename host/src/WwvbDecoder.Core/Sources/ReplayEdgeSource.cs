using System.Runtime.CompilerServices;

namespace WwvbDecoder.Core;

/// <summary>
/// An <see cref="IEdgeSource"/> that replays edges from a capture file.
/// </summary>
public sealed class ReplayEdgeSource : IEdgeSource
{
    private static readonly string[] HeaderLines =
    [
        "# capture",
        "# recorded ",
        "# source ",
        "# reduced-carrier-level ",
    ];
    private const string WallLinePrefix = "# wall";
    private readonly IReadOnlyList<string> _lines;
    private readonly TimeProvider _timeProvider;
    private ulong? _baseline;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayEdgeSource"/> class.
    /// </summary>
    /// <param name="lines">The lines from the capture file.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <exception cref="ArgumentNullException"></exception>
    public ReplayEdgeSource(IReadOnlyList<string> lines, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _lines = lines;
        _timeProvider = timeProvider;
        Info = CaptureInfo.Parse(lines);
    }

    /// <inheritdoc/>
    public string Name => "replay";

    /// <summary>
    /// The capture info parsed from the header of the capture file.
    /// </summary>
    public CaptureInfo Info { get; }

    /// <summary>
    /// Gets or sets the speed factor for the synthetic edge source.
    /// 0 = unpaced, 1 = real time, N = N times real time.
    /// Volatile read and write, so it can be changed while reading. Default is 1.0 (real time).
    /// </summary>
    public double Speed
    {
        get => Volatile.Read(ref field);
        set
        {
            if (value is < 0.0 or double.NaN)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Speed),
                    "Speed must be a non-negative number."
                );
            }

            Volatile.Write(ref field, value);
        }
    } = 1.0;

    /// <summary>
    /// Whether to loop the replay when the end of the capture file is reached.
    /// </summary>
    public bool Loop
    {
        get => Volatile.Read(ref field);
        init;
    }

    /// <summary>
    /// The number of malformed lines in the capture file.
    /// </summary>
    public long MalformedLines { get; private set; }

    /// <summary>
    /// Yields the edges from the capture file.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An asynchronous stream of <see cref="EdgeEvent"/> instances.</returns>
    public async IAsyncEnumerable<EdgeEvent> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        yield return new EdgeEvent.Reset("start");
        do
        {
            await foreach (EdgeEvent p in ReadAsyncCore(ct))
            {
                yield return p;
            }
            if (!Loop)
            {
                break;
            }
            // Reset the last micros so that the next loop starts fresh.
            _baseline = null;
            yield return new EdgeEvent.Reset("loop");
        } while (Loop);
    }

    private async IAsyncEnumerable<EdgeEvent> ReadAsyncCore(
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        foreach (string line in _lines)
        {
            double speed = Speed;
            ct.ThrowIfCancellationRequested();
            if (HeaderLines.Any(line.StartsWith))
            {
                continue;
            }
            if (line.StartsWith(WallLinePrefix, StringComparison.Ordinal))
            {
                yield return new EdgeEvent.Info(line);
                continue;
            }
            if (!EdgeLineCodec.TryParse(line, out EdgeEvent? evt))
            {
                MalformedLines++;
                continue;
            }

            if (await TrackAsync(evt, speed, ct) is { } backwards)
            {
                yield return backwards;
            }
            yield return evt;
        }
    }

    // Keeps _baseline in step with the stream and paces each sample against the one before it.
    // Returns the Reset to yield ahead of a sample whose timestamp went backwards.
    private async ValueTask<EdgeEvent.Reset?> TrackAsync(
        EdgeEvent evt,
        double speed,
        CancellationToken ct
    )
    {
        if (evt is not EdgeEvent.Sample sample)
        {
            if (evt is EdgeEvent.Reset)
            {
                _baseline = null;
            }
            return null;
        }

        EdgeEvent.Reset? backwards = null;
        if (_baseline is { } baseline)
        {
            if (sample.Edge.Micros < baseline)
            {
                backwards = new EdgeEvent.Reset("timestamp went backwards");
            }
            else if (speed > 0.0)
            {
                await Task.Delay(
                    TimeSpan.FromMicroseconds((sample.Edge.Micros - baseline) / speed),
                    _timeProvider,
                    ct
                );
            }
        }
        _baseline = sample.Edge.Micros;
        return backwards;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
