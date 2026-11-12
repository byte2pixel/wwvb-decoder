using System.Globalization;

namespace WwvbDecoder.Adapters;

/// <summary>
/// Writes the capture.
/// </summary>
public sealed class CaptureWriter
{
    private const string CaptureVersion = "1";
    private readonly TextWriter _writer;
    private readonly TimeProvider _timeProvider;
    private bool _started;
    private DateTimeOffset? _nextWallTime;
    private ulong? _lastMicros;

    /// <summary>
    /// Initializes a new instance of the <see cref="CaptureWriter"/> class.
    /// </summary>
    /// <param name="writer">The <see cref="TextWriter"/> to write to.</param>
    /// <param name="timeProvider">The <see cref="TimeProvider"/> to use for timestamps.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="writer"/> or <paramref name="timeProvider"/> is <c>null</c>.</exception>
    public CaptureWriter(TextWriter writer, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _writer = writer;
        _timeProvider = timeProvider;
        _writer.NewLine = "\n";
    }

    /// <summary>
    /// Writes a header to the capture file.
    /// </summary>
    /// <param name="recorded">The time the capture was recorded.</param>
    /// <param name="source">The source of the capture.</param>
    /// <param name="reducedCarrierLevel">The reduced carrier level, if any.</param>
    public void WriteHeader(DateTimeOffset recorded, string source, PinLevel? reducedCarrierLevel)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_started)
        {
            throw new InvalidOperationException("Cannot write header after writing lines.");
        }
        if (source.ContainsAny('\n', '\r'))
        {
            throw new ArgumentException(
                "Source cannot contain newline characters.",
                nameof(source)
            );
        }
        WriteHeaderVersion();
        WriteHeaderRecordedUtc(recorded);
        WriteHeaderSource(source);
        WriteHeaderReducedCarrierLevel(reducedCarrierLevel);
        _started = true;
    }

    /// <summary>
    /// Writes a raw line to the capture file.
    /// </summary>
    /// <param name="rawLine">The raw line to write.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="rawLine"/> is <c>null</c>.</exception>
    public void WriteLine(string rawLine)
    {
        ArgumentNullException.ThrowIfNull(rawLine);
        if (!_started)
        {
            throw new InvalidOperationException("Cannot write source before writing header.");
        }
        if (rawLine.ContainsAny('\n', '\r'))
        {
            throw new ArgumentException(
                "Raw line cannot contain newline characters.",
                nameof(rawLine)
            );
        }
        WriteLineCore(rawLine);
    }

    # region header helpers

    private void WriteHeaderReducedCarrierLevel(PinLevel? reducedCarrierLevel)
    {
        if (!reducedCarrierLevel.HasValue)
        {
            return;
        }

        string levelString = reducedCarrierLevel.Value switch
        {
            PinLevel.High => "H",
            PinLevel.Low => "L",
            _ => throw new ArgumentOutOfRangeException(
                nameof(reducedCarrierLevel),
                "Invalid PinLevel value."
            ),
        };
        _writer.WriteLine($"# reduced-carrier-level {levelString}");
    }

    private void WriteHeaderSource(string source)
    {
        _writer.WriteLine($"# source {source}");
    }

    private void WriteHeaderRecordedUtc(DateTimeOffset recorded)
    {
        DateTimeOffset recordedUtc = recorded.ToUniversalTime();
        string recordedString = string.Create(
            CultureInfo.InvariantCulture,
            $"# recorded {recordedUtc:yyyy-MM-ddTHH:mm:ssZ}"
        );
        _writer.WriteLine(recordedString);
    }

    private void WriteHeaderVersion()
    {
        _writer.WriteLine($"# capture v{CaptureVersion}");
    }

    #endregion

    private void WriteLineCore(string rawLine)
    {
        var now = _timeProvider.GetUtcNow();
        var boundary = DropSecondsFromTime(now);
        _writer.WriteLine(rawLine);

        if (EdgeLineCodec.TryParse(rawLine, out EdgeEvent? evt) && evt is EdgeEvent.Sample sample)
        {
            _lastMicros = sample.Edge.Micros;
            _nextWallTime ??= boundary;
        }

        if (!_lastMicros.HasValue || !_nextWallTime.HasValue || now < _nextWallTime.Value)
        {
            return;
        }

        WriteWallLine(now, _lastMicros.Value);
        _nextWallTime = boundary;
    }

    private void WriteWallLine(DateTimeOffset now, ulong lastMicros)
    {
        var wallLine = string.Create(
            CultureInfo.InvariantCulture,
            $"# wall {now:yyyy-MM-ddTHH:mm:ssZ} {lastMicros}"
        );
        _writer.WriteLine(wallLine);
    }

    private static DateTimeOffset DropSecondsFromTime(DateTimeOffset wallTime)
    {
        var minute = new DateTimeOffset(
            wallTime.Year,
            wallTime.Month,
            wallTime.Day,
            wallTime.Hour,
            wallTime.Minute,
            0,
            TimeSpan.Zero
        ).AddMinutes(1);
        return minute;
    }
}
