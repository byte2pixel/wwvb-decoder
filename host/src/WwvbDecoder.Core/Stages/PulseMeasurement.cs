namespace WwvbDecoder.Core;

/// <summary>
/// Represents a stage in the decoder pipeline that measures pulses from edges, producing <see cref="Pulse"/> instances.
/// </summary>
public sealed class PulseMeasurement : IStage<Edge, Pulse>
{
    private ulong? _openingEdgeMicros;
    private Pulse? _pendingPulse;
    private PinLevel? _lastLevel;
    private ulong? _lastMicros;
    private PinLevel _measuredWithLevel = PinLevel.High;

    private volatile PinLevel _reducedCarrierLevel = PinLevel.High;

    /// <summary>
    /// Gets or sets the pin level that indicates a reduced carrier signal.
    /// This level is used to identify pulses in the input edges.
    /// It can change while the stage is running. The next <see cref="Push"/> that sees a new
    /// value discards the pulse in progress, so a pulse never mixes the two polarities.
    /// </summary>
    public PinLevel ReducedCarrierLevel
    {
        get => _reducedCarrierLevel;
        set => _reducedCarrierLevel = value;
    }

    /// <summary>
    /// Gets or sets the minimum duration of a pulse in microseconds.
    /// Pulses shorter than this duration are considered glitches and are dropped.
    /// </summary>
    public ulong MinPulseMicros { get; init; } = 50_000;

    /// <summary>
    /// Reduced-carrier pulses shorter than <see cref="MinPulseMicros"/> that were dropped.
    /// </summary>
    public long GlitchesDropped { get; private set; }

    /// <summary>
    /// Full-carrier gaps shorter than <see cref="MinPulseMicros"/> that were bridged.
    /// </summary>
    public long GapsBridged { get; private set; }

    /// <summary>
    /// Edges with the same level as the edge before.
    /// Causes: An edge the firmware lost (# dropped), and an edge pair the firmware merged into one line.
    /// </summary>
    public long UnpairedEdges { get; private set; }

    /// <summary>
    /// Processes an input edge and returns the resulting pulses as a materialized collection.
    /// The method advances the stage's state even if the result is not enumerated.
    /// </summary>
    /// <param name="input">The input edge to process.</param>
    /// <returns>
    /// An enumerable collection of <see cref="Pulse"/> instances produced from the input edge.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The edge's timestamp is smaller than the last edge's. The stage's state is left unchanged.
    /// </exception>
    public IEnumerable<Pulse> Push(Edge input)
    {
        if (input.Micros < _lastMicros)
        {
            throw new InvalidOperationException(
                $"Edge timestamp {input.Micros} is smaller than the last edge's, {_lastMicros}."
            );
        }

        PinLevel reducedCarrierLevel = _reducedCarrierLevel;
        if (reducedCarrierLevel != _measuredWithLevel)
        {
            _openingEdgeMicros = null;
            _pendingPulse = null;
            _lastLevel = null;
            _measuredWithLevel = reducedCarrierLevel;
        }

        _lastMicros = input.Micros;

        if (input.Level == _lastLevel)
        {
            UnpairedEdges++;
            return [];
        }

        _lastLevel = input.Level;

        if (input.Level == reducedCarrierLevel)
        {
            if (_pendingPulse is null)
            {
                _openingEdgeMicros = input.Micros;
                return [];
            }

            if (input.Micros - _pendingPulse.Value.EndMicros < MinPulseMicros)
            {
                _openingEdgeMicros = _pendingPulse.Value.StartMicros;
                _pendingPulse = null;
                GapsBridged++;
                return [];
            }

            Pulse returnedPulse = _pendingPulse.Value;
            _pendingPulse = null;
            _openingEdgeMicros = input.Micros;

            if (returnedPulse.DurationMicros >= MinPulseMicros)
            {
                return [returnedPulse];
            }

            GlitchesDropped++;
            return [];
        }

        if (_openingEdgeMicros is null)
        {
            return [];
        }

        ulong duration = input.Micros - _openingEdgeMicros.Value;
        Pulse pulse = new Pulse(_openingEdgeMicros.Value, duration);
        _openingEdgeMicros = null;
        _pendingPulse = pulse;
        return [];
    }

    /// <summary>
    /// Discards the state of the stage so that the next input is treated as the first.
    /// A pending pulse is lost, not output. The counters keep their values.
    /// </summary>
    public void Reset()
    {
        _openingEdgeMicros = null;
        _pendingPulse = null;
        _lastLevel = null;
        _lastMicros = null;
    }
}
