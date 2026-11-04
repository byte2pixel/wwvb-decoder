namespace WwvbDecoder.Core;

/// <summary>
/// A single WWVB symbol.
/// </summary>
/// <param name="StartMicros">Pico timestamp. The start time of the symbol, in microseconds.</param>
/// <param name="DurationMicros">The duration of the symbol, in microseconds.</param>
/// <param name="Kind">The kind of the symbol.</param>
public readonly record struct Symbol(ulong StartMicros, ulong DurationMicros, SymbolKind Kind);
