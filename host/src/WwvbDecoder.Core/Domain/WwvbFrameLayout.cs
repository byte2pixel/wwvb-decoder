using System.Collections.Immutable;

namespace WwvbDecoder.Core;

/// <summary>
/// The layout of a WWVB frame, including the roles of each second, the marker seconds, and the BCD digits.
/// </summary>
public static class WwvbFrameLayout
{
    /// <summary>
    /// A frame consists of 60 seconds, 0 to 59.
    /// </summary>
    public const int FrameSeconds = 60;

    /// <summary>
    /// Pr at 0, P1 to P5 at 9 to 49, P0 at 59
    /// </summary>
    public static ImmutableArray<int> MarkerSeconds { get; } = [0, 9, 19, 29, 39, 49, 59];

    /// <summary>
    /// Seconds that carry no information; always transmitted as 0
    /// </summary>
    public static ImmutableArray<int> UnusedSeconds { get; } =
    [4, 10, 11, 14, 20, 21, 24, 34, 35, 44, 54];

    /// <summary>
    /// The BCD digits in the frame.
    /// </summary>
    public static ImmutableArray<BcdDigit> Digits { get; } =
    [
        new BcdDigit(
            SecondRole.Minute,
            10,
            [new LayoutBit(1, 40), new LayoutBit(2, 20), new LayoutBit(3, 10)]
        ),
        new BcdDigit(
            SecondRole.Minute,
            1,
            [new LayoutBit(5, 8), new LayoutBit(6, 4), new LayoutBit(7, 2), new LayoutBit(8, 1)]
        ),
        new BcdDigit(SecondRole.Hour, 10, [new LayoutBit(12, 20), new LayoutBit(13, 10)]),
        new BcdDigit(
            SecondRole.Hour,
            1,
            [new LayoutBit(15, 8), new LayoutBit(16, 4), new LayoutBit(17, 2), new LayoutBit(18, 1)]
        ),
        new BcdDigit(SecondRole.DayOfYear, 100, [new LayoutBit(22, 200), new LayoutBit(23, 100)]),
        new BcdDigit(
            SecondRole.DayOfYear,
            10,
            [
                new LayoutBit(25, 80),
                new LayoutBit(26, 40),
                new LayoutBit(27, 20),
                new LayoutBit(28, 10),
            ]
        ),
        new BcdDigit(
            SecondRole.DayOfYear,
            1,
            [new LayoutBit(30, 8), new LayoutBit(31, 4), new LayoutBit(32, 2), new LayoutBit(33, 1)]
        ),
        new BcdDigit(
            SecondRole.Year,
            10,
            [
                new LayoutBit(45, 80),
                new LayoutBit(46, 40),
                new LayoutBit(47, 20),
                new LayoutBit(48, 10),
            ]
        ),
        new BcdDigit(
            SecondRole.Year,
            1,
            [new LayoutBit(50, 8), new LayoutBit(51, 4), new LayoutBit(52, 2), new LayoutBit(53, 1)]
        ),
    ];

    /// <summary>
    /// Seconds 36 to 38, the sign of the UT1 correction.
    /// </summary>
    public static ImmutableArray<int> Ut1SignSeconds { get; } = [36, 37, 38];

    /// <summary>
    /// Seconds 40 to 43, the UT1 correction in tenths of a second.
    /// </summary>
    public static ImmutableArray<LayoutBit> Ut1Correction { get; } =
    [new LayoutBit(40, 8), new LayoutBit(41, 4), new LayoutBit(42, 2), new LayoutBit(43, 1)];

    /// <summary>
    /// Second 55. 1 = in a leap year.
    /// </summary>
    public const int LeapYearSecond = 55;

    /// <summary>
    /// Second 56. 1 = when a leap second will be inserted at the end of the month.
    /// </summary>
    public const int LeapSecondPendingSecond = 56;

    /// <summary>
    /// Seconds 57 and 58, the Daylight Saving Time (DST) status.
    /// </summary>
    public static ImmutableArray<int> DstSeconds { get; } = [57, 58];

    /// <summary>
    /// The role of each second in the frame, indexed by second.
    /// </summary>
    public static ImmutableArray<SecondRole> Roles { get; } = BuildRoles();

    private static ImmutableArray<SecondRole> BuildRoles()
    {
        var roles = new SecondRole[FrameSeconds];

        foreach (int second in MarkerSeconds)
        {
            roles[second] = SecondRole.Marker;
        }

        foreach (int second in UnusedSeconds)
        {
            roles[second] = SecondRole.Unused;
        }

        foreach (BcdDigit digit in Digits)
        {
            foreach (LayoutBit bit in digit.Bits)
            {
                roles[bit.Second] = digit.Field;
            }
        }

        foreach (int second in Ut1SignSeconds)
        {
            roles[second] = SecondRole.Ut1Sign;
        }

        foreach (LayoutBit bit in Ut1Correction)
        {
            roles[bit.Second] = SecondRole.Ut1Correction;
        }

        roles[LeapYearSecond] = SecondRole.LeapYear;
        roles[LeapSecondPendingSecond] = SecondRole.LeapSecondPending;

        foreach (int second in DstSeconds)
        {
            roles[second] = SecondRole.Dst;
        }

        return [.. roles];
    }
}
