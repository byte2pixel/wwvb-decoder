namespace WwvbDecoder.Core;

/// <summary>
/// Validation checks that can be performed on a WWVB frame.
/// The order of the members is the order failures are reported in.
/// </summary>
public enum ValidationCheck
{
    /// <summary>
    /// A marker second is not <see cref="SymbolKind.Marker"/> or any other second is <see cref="SymbolKind.Marker"/> when it should not be.
    /// </summary>
    Markers,

    /// <summary>
    /// An unused second is not <see cref="SymbolKind.Zero"/>.
    /// </summary>
    UnusedBits,

    /// <summary>
    /// <see cref="DecodedFrame.ErrorSeconds"/> is not empty.
    /// </summary>
    ErrorSymbols,

    /// <summary>
    /// Any of the nine minute, hour, day-of-year, and year digits, re-derived from the frame through <see cref="WwvbFrameLayout"/>, exceeds 9.
    /// </summary>
    BcdDigits,

    /// <summary>
    /// Minute > 59
    /// </summary>
    MinuteRange,

    /// <summary>
    /// Hour > 23
    /// </summary>
    HourRange,

    /// <summary>
    /// <see cref="DecodedFrame.DayOfYear"/> is 0 or above 366, or 366 without <see cref="DecodedFrame.LeapYear"/>.
    /// </summary>
    DayOfYearRange,

    /// <summary>
    /// Ut1Sign is <see cref="Ut1Sign.Invalid"/>
    /// </summary>
    Ut1Sign,

    /// <summary>
    /// Ut1Tenths > 9
    /// </summary>
    Ut1Range,

    /// <summary>
    /// The frame's time isn't as many minutes after the last accepted frame's as passed between
    /// the two start times, or the frame started within 90 s of it and not 60 s ± 50 ms later.
    /// A frame passes when it follows the last frame that failed this check alone.
    /// Skipped when there's no previous frame, or when the start times don't give a whole number
    /// of minutes up to 60.
    /// </summary>
    Consecutive,
}
