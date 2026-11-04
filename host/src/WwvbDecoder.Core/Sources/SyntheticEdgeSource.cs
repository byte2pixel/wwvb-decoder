namespace WwvbDecoder.Core;

/// <summary>
/// A synthetic edge source that generates edges based on a specified start time, speed, and noise level.
/// </summary>
public sealed class SyntheticEdgeSource : IEdgeSource
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyntheticEdgeSource"/> class with the specified time provider.
    /// </summary>
    /// <param name="timeProvider">The time provider to use for generating synthetic edges.</param>
    public SyntheticEdgeSource(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Gets the short display name of the synthetic edge source, which is "synthetic".
    /// </summary>
    public string Name => "synthetic";

    /// <summary>
    /// Gets the start time of the synthetic edge source in UTC.
    /// This property is required to be set before reading events.
    /// </summary>
    public required DateTimeOffset StartUtc
    {
        get;
        init
        {
            if (value.UtcTicks % TimeSpan.TicksPerMinute != 0)
            {
                throw new ArgumentException("Must fall exactly on a UTC minute.", nameof(StartUtc));
            }

            if (value.UtcDateTime.Year is < 2000 or > 2099)
            {
                throw new ArgumentException(
                    "Year must be between 2000 and 2099.",
                    nameof(StartUtc)
                );
            }

            field = value;
        }
    }

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
    /// Gets or sets the noise level for the synthetic edge source, which is a value between 0.0 and 1.0.
    /// Volatile read and write, so it can be changed while reading. Default is 0.0 (no noise).
    /// </summary>
    public double Noise
    {
        get => Volatile.Read(ref field);
        set
        {
            if (value is < 0.0 or > 1.0 or double.NaN)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Noise),
                    "Noise must be a number between 0.0 and 1.0."
                );
            }

            Volatile.Write(ref field, value);
        }
    } = 0.0;

    /// <summary>
    /// Gets the reduced carrier level for the synthetic edge source.
    /// Default is <see cref="PinLevel.High"/>.
    /// </summary>
    public PinLevel ReducedCarrierLevel { get; init; } = PinLevel.High;

    /// <summary>
    /// Gets the seed for the random number generator used in the synthetic edge source.
    /// </summary>
    public int Seed { get; init; } = 0;

    /// <summary>
    /// Reads the synthetic edge events asynchronously, yielding them in order.
    /// </summary>
    /// <param name="ct">A cancellation token that can be used to cancel the enumeration</param>
    /// <returns>An asynchronous enumerable of <see cref="EdgeEvent"/> instances.</returns>
    public IAsyncEnumerable<EdgeEvent> ReadAsync(CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Disposes of the resources used by the <see cref="SyntheticEdgeSource"/> instance asynchronously.
    /// </summary>
    /// <returns>
    /// A <see cref="ValueTask"/> that represents the asynchronous dispose operation.
    /// </returns>
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
