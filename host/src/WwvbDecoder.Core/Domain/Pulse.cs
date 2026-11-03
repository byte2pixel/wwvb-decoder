namespace WwvbDecoder.Core;

/// <summary>
/// Represents a pulse in a signal, defined by its start time and duration in microseconds.
/// </summary>
/// <param name="StartMicros">The start time of the pulse in microseconds.</param>
/// <param name="DurationMicros">The duration of the pulse in microseconds.</param>
public readonly record struct Pulse(ulong StartMicros, ulong DurationMicros)
{
    /// <summary>
    /// Gets the end time of the pulse in microseconds, calculated as the sum of the start time and duration.
    /// </summary>
    public ulong EndMicros => StartMicros + DurationMicros;
}
