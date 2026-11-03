namespace WwvbDecoder.Core;

/// <summary>
/// Represents an edge in a signal, defined by the time in microseconds and the pin level (high or low).
/// </summary>
/// <param name="Micros">The time of the edge in microseconds.</param>
/// <param name="Level">The level of the pin (high or low) at the edge.</param>
public readonly record struct Edge(ulong Micros, PinLevel Level);
