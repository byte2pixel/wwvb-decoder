namespace WwvbDecoder.Core;

/// <summary>
/// Represents a stage in the decoder pipeline that measures pulses from edges, producing <see cref="Pulse"/> instances.
/// </summary>
public sealed class PulseMeasurement : IStage<Edge, Pulse>
{
    private ulong? _openingEdgeMicros;
    private Pulse? _pendingPulse;
    private PinLevel? _lastLevel;

    /// <summary>
    /// Gets or sets the pin level that indicates a reduced carrier signal.
    /// This level is used to identify pulses in the input edges.
    /// </summary>
    public PinLevel ReducedCarrierLevel { get; set; } = PinLevel.High;

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
    /// Edges with the same level as the edge before
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
    public IEnumerable<Pulse> Push(Edge input)
    {
        if (input.Level == _lastLevel)
        {
            UnpairedEdges++;
            return [];
        }

        _lastLevel = input.Level;

        if (input.Level == ReducedCarrierLevel)
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
    /// </summary>
    public void Reset()
    {
        // Implementation of state reset logic goes here.
        // This method should clear any internal state to prepare for a new sequence of inputs.
        throw new NotImplementedException();
    }
}
