namespace WwvbDecoder.Core;

/// <summary>
/// Status of Daylight Saving Time (DST) in the WWVB signal.
/// </summary>
public enum DstStatus
{
    /// <summary>
    /// Standard (0 0)
    /// </summary>
    Standard,

    /// <summary>
    /// DST (1 1)
    /// </summary>
    Dst,

    /// <summary>
    /// Begins today (1 0)
    /// </summary>
    BeginsToday,

    /// <summary>
    /// Ends today (0 1)
    /// </summary>
    EndsToday,
}
