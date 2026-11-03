namespace WwvbDecoder.Core;

/// <summary>
/// Represents the kind of symbol in a WWVB signal, which can be Zero, One, Marker, or Error.
/// </summary>
public enum SymbolKind
{
    /// <summary>
    /// Represents a symbol that corresponds to a binary 0 in the WWVB signal.
    /// </summary>
    Zero,

    /// <summary>
    /// Represents a symbol that corresponds to a binary 1 in the WWVB signal.
    /// </summary>
    One,

    /// <summary>
    /// Represents a marker symbol in the WWVB signal, which is used for synchronization and timing
    /// </summary>
    Marker,

    /// <summary>
    /// Represents an error symbol in the WWVB signal, which indicates that there was a problem decoding the signal at this point.
    /// </summary>
    Error,
}
