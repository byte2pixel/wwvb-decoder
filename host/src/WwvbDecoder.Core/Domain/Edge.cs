namespace WwvbDecoder.Core;

/// <summary>
/// One edge on the receiver's data pin.
/// </summary>
/// <param name="Micros">Microseconds since the Pico booted.</param>
/// <param name="Level">The level after the edge.</param>
public readonly record struct Edge(ulong Micros, PinLevel Level);
