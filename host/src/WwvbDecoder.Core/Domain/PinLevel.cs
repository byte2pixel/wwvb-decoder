namespace WwvbDecoder.Core;

/// <summary>
/// Which level is reduced carrier depends on the receiver; receiver #1 measured <see cref="High"/>.
/// </summary>
public enum PinLevel
{
    /// <summary>
    /// Pin reads low.
    /// </summary>
    Low,

    /// <summary>
    /// Pin reads high.
    /// </summary>
    High,
}
