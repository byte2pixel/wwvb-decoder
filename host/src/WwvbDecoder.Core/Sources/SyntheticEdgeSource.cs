using System.Runtime.CompilerServices;

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
    public int Seed { get; init; }

    /// <summary>
    /// Reads the synthetic edge events asynchronously, yielding them in order.
    /// </summary>
    /// <param name="ct">A cancellation token that can be used to cancel the enumeration</param>
    /// <returns>An asynchronous enumerable of <see cref="EdgeEvent"/> instances.</returns>
    public async IAsyncEnumerable<EdgeEvent> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        yield return new EdgeEvent.Reset("start");

        ulong? previous = null;
        foreach (Edge edge in GenerateEdges())
        {
            double speed = Speed;
            ct.ThrowIfCancellationRequested();
            if (speed > 0.0 && previous.HasValue)
            {
                await Task.Delay(
                    TimeSpan.FromMicroseconds((edge.Micros - previous.Value) / speed),
                    _timeProvider,
                    ct
                );
            }
            yield return new EdgeEvent.Sample(edge);
            previous = edge.Micros;
        }
    }

    private IEnumerable<Edge> GenerateEdges()
    {
        Random random = new(Seed);
        PinLevel normalCarrierLevel =
            ReducedCarrierLevel == PinLevel.High ? PinLevel.Low : PinLevel.High;

        foreach (Edge edge in GenerateSecond(0, 800_000, normalCarrierLevel, random))
            yield return edge;

        ulong minute = 0;
        while (true)
        {
            Frame frame = WwvbFrameEncoder.Encode(
                StartUtc.AddMinutes(minute),
                1_000_000UL + minute * 60_000_000UL,
                Ut1Sign.Plus,
                0,
                false,
                DstStatus.Standard
            );

            ulong pulseStart = frame.StartMicros;
            foreach (SymbolKind symbol in frame.Symbols)
            {
                ulong duration = symbol switch
                {
                    SymbolKind.Zero => 200_000UL,
                    SymbolKind.One => 500_000UL,
                    SymbolKind.Marker => 800_000UL,
                    _ => throw new InvalidOperationException($"Unexpected symbol: {symbol}"),
                };
                foreach (
                    Edge edge in GenerateSecond(pulseStart, duration, normalCarrierLevel, random)
                )
                    yield return edge;
                pulseStart += 1_000_000;
            }
            minute++;
        }
    }

    private IEnumerable<Edge> GenerateSecond(
        ulong pulseStart,
        ulong duration,
        PinLevel normalCarrierLevel,
        Random random
    )
    {
        if (random.NextDouble() < Noise)
        {
            int fault = random.Next(4);
            switch (fault)
            {
                // select 1 of 4 possible faults. dropped edge, gap glitch, pulse glitch, or jitter.
                case 0:
                    // dropped edge
                    int keepEdge = random.Next(2);
                    Edge edge = keepEdge switch
                    {
                        0 => new Edge(pulseStart, ReducedCarrierLevel),
                        1 => new Edge(pulseStart + duration, normalCarrierLevel),
                        _ => throw new InvalidOperationException(
                            $"Unexpected keepEdge value: {keepEdge}"
                        ),
                    };
                    yield return edge;
                    break;
                case 1:
                    // gap glitch
                    ulong gapWidth = (ulong)random.NextInt64(10_000, 40_001);
                    ulong gapStart = (ulong)
                        random.NextInt64(20_000, (long)duration - 20_000 - (long)gapWidth + 1);
                    yield return new Edge(pulseStart, ReducedCarrierLevel);
                    yield return new Edge(pulseStart + gapStart, normalCarrierLevel);
                    yield return new Edge(pulseStart + gapStart + gapWidth, ReducedCarrierLevel);
                    yield return new Edge(pulseStart + duration, normalCarrierLevel);
                    break;
                case 2:
                    // pulse glitch
                    ulong glitchWidth = (ulong)random.NextInt64(10_000, 40_001);
                    ulong glitchStart = (ulong)
                        random.NextInt64(
                            (long)duration + 60_000,
                            1_000_000 - 60_000 - (long)glitchWidth + 1
                        );
                    yield return new Edge(pulseStart, ReducedCarrierLevel);
                    yield return new Edge(pulseStart + duration, normalCarrierLevel);
                    yield return new Edge(pulseStart + glitchStart, ReducedCarrierLevel);
                    yield return new Edge(
                        pulseStart + glitchStart + glitchWidth,
                        normalCarrierLevel
                    );
                    break;
                default:
                {
                    // jitter
                    long jittered = (long)duration + random.NextInt64(-300_000, 300_001);
                    duration = (ulong)Math.Clamp(jittered, 10_000L, 990_000L);

                    yield return new Edge(pulseStart, ReducedCarrierLevel);
                    yield return new Edge(pulseStart + duration, normalCarrierLevel);
                    break;
                }
            }
        }
        else
        {
            yield return new Edge(pulseStart, ReducedCarrierLevel);
            yield return new Edge(pulseStart + duration, normalCarrierLevel);
        }
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
