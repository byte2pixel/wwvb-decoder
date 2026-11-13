namespace WwvbDecoder.Core;

/// <summary>
/// Each pulse becomes exactly one symbol with the pulse's start and duration.
/// </summary>
public sealed class SymbolClassifier : IStage<Pulse, Symbol>
{
    /// <summary>
    /// Pulses shorter than this are considered invalid and classified as errors.
    /// </summary>
    public ulong MinValidMicros { get; init; } = 100_000;

    /// <summary>
    /// Pulses at or above this are ones.
    /// </summary>
    public ulong ZeroOneThresholdMicros { get; init; } = 350_000;

    /// <summary>
    /// Pulses at or above this are markers.
    /// </summary>
    public ulong OneMarkerThresholdMicros { get; init; } = 650_000;

    /// <summary>
    /// Pulses longer than this are considered invalid and classified as errors.
    /// </summary>
    public ulong MaxValidMicros { get; init; } = 900_000;

    /// <summary>
    /// Processes an input pulse and classifies it into a symbol based on its duration.
    /// </summary>
    /// <param name="input">The input pulse to be classified.</param>
    /// <returns>An enumerable containing the classified symbol.</returns>
    public IEnumerable<Symbol> Push(Pulse input)
    {
        if (input.DurationMicros < MinValidMicros || input.DurationMicros > MaxValidMicros)
        {
            return [new Symbol(input.StartMicros, input.DurationMicros, SymbolKind.Error)];
        }

        SymbolKind kind = input.DurationMicros switch
        {
            var d when d < ZeroOneThresholdMicros => SymbolKind.Zero,
            var d when d < OneMarkerThresholdMicros => SymbolKind.One,
            _ => SymbolKind.Marker,
        };

        return [new Symbol(input.StartMicros, input.DurationMicros, kind)];
    }

    /// <summary>
    /// Noop. The stage has no internal state to reset, so this method does nothing.
    /// </summary>
    public void Reset() { }
}
