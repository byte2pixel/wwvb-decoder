namespace WwvbDecoder.Core;

/// <summary>
/// Stage 3's classification of one pulse.
/// </summary>
public enum SymbolKind
{
    /// <summary>
    /// 0.2 s of reduced carrier, representing a binary 0 in the WWVB signal.
    /// </summary>
    Zero,

    /// <summary>
    /// 0.5 s of reduced carrier, representing a binary 1 in the WWVB signal.
    /// </summary>
    One,

    /// <summary>
    /// 0.8 s of reduced carrier, representing a marker in the WWVB signal.
    /// </summary>
    Marker,

    /// <summary>
    /// A duration outside every class.
    /// </summary>
    Error,
}
