namespace WwvbDecoder.Core;

/// <summary>
/// Role of a second in the WWVB signal.
/// </summary>
public enum SecondRole
{
    /// <summary>
    /// Seconds 0, 9, 19, 29, 39, 49, 59.
    /// </summary>
    Marker,

    /// <summary>
    /// Seconds 4, 10, 11, 14, 20, 21, 24, 34, 35, 44, 54.
    /// Always 0.
    /// </summary>
    Unused,

    /// <summary>
    /// Seconds 1 to 3, 5 to 8.
    /// </summary>
    Minute,

    /// <summary>
    /// Seconds 12 to 13 and 15 to 18.
    /// </summary>
    Hour,

    /// <summary>
    /// Seconds 22 to 23, 25 to 28, 30 to 33.
    /// </summary>
    DayOfYear,

    /// <summary>
    /// Seconds 36 to 38.
    /// </summary>
    Ut1Sign,

    /// <summary>
    /// Seconds 40 to 43, in tenths of a second.
    /// </summary>
    Ut1Correction,

    /// <summary>
    /// Seconds 45 to 48, 50 to 53.
    /// </summary>
    Year,

    /// <summary>
    /// Second 55. 1 = in a leap year.
    /// </summary>
    LeapYear,

    /// <summary>
    /// Second 56. 1 = when a leap second will be inserted at the end of the month.
    /// </summary>
    LeapSecondPending,

    /// <summary>
    /// Seconds 57 and 58, read as a pair, see <see cref="DstStatus"/>.
    /// </summary>
    Dst,
}
