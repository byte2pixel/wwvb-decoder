using System.Collections.Immutable;

namespace WwvbDecoder.Core;

/// <summary>
/// Stage 5's output, a plain read of the frame with no range checks.
/// </summary>
/// <param name="Frame">The original frame of symbols, which contains all 60 symbols for that frame.</param>
/// <param name="Minute">Sum of the set bits' weights, 0 to 85.</param>
/// <param name="Hour">0 to 45</param>
/// <param name="DayOfYear">0 to 465</param>
/// <param name="Year">The two transmitted digits, 0 to 165.</param>
/// <param name="Ut1Sign">The sign of the UT1 time correction, which can be plus, minus, or invalid.</param>
/// <param name="Ut1Tenths">Seconds 40 to 43 as a sum of weights, 0 to 15.</param>
/// <param name="LeapYear">The leap year status, indicating whether the decoded year is a leap year.</param>
/// <param name="LeapSecondPending">The leap second pending status, indicating whether a leap second is pending in the decoded time.</param>
/// <param name="Dst">The Daylight Saving Time (DST) status.</param>
/// <param name="ErrorSeconds">Field seconds that held an Error symbol, ascending. Markers and unused seconds are never listed.</param>
public sealed record DecodedFrame(
    Frame Frame,
    int Minute,
    int Hour,
    int DayOfYear,
    int Year,
    Ut1Sign Ut1Sign,
    int Ut1Tenths,
    bool LeapYear,
    bool LeapSecondPending,
    DstStatus Dst,
    ImmutableArray<int> ErrorSeconds
);
