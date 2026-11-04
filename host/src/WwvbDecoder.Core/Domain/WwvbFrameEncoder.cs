namespace WwvbDecoder.Core;

/// <summary>
/// Builds the 60 symbols for the frame transmitted during a minute. The only code that writes bits into a frame.
/// </summary>
public static class WwvbFrameEncoder
{
    /// <summary>
    /// Encodes a DateTimeOffset into a WWVB frame of symbols.
    /// </summary>
    /// <param name="utcMinute">The UTC time to encode. It can have any offset and is converted to UTC. Must be at the start of a minute (0 seconds, 0 milliseconds).</param>
    /// <param name="startMicros">The start time of the frame in microseconds.</param>
    /// <param name="sign">The sign of the UT1 time correction</param>
    /// <param name="ut1Tenths">The UT1 time correction in tenths of a second</param>
    /// <param name="leapSecondPending">Whether a leap second is pending at the end of the month</param>
    /// <param name="dst">The Daylight Saving Time (DST) status</param>
    /// <returns>
    /// A Frame containing the encoded symbols, start time, and end time.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown if utcMinute is not at the start of a minute (0 seconds, 0 milliseconds).</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if utcMinute.Year is not between 2000 and 2099, if sign is not Plus or Minus, or if ut1Tenths is not between 0 and 9.</exception>
    public static Frame Encode(
        DateTimeOffset utcMinute,
        ulong startMicros,
        Ut1Sign sign,
        int ut1Tenths,
        bool leapSecondPending,
        DstStatus dst
    )
    {
        // convert utcMinute to UTC
        DateTime utc = utcMinute.UtcDateTime;

        // Must have 0 seconds and sub-seconds or throw ArgumentException
        if (utc.Ticks % TimeSpan.TicksPerMinute != 0)
        {
            throw new ArgumentException(
                "utcMinute must be at the start of a minute.",
                nameof(utcMinute)
            );
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(utc.Year, 2000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(utc.Year, 2099);

        if (sign != Ut1Sign.Plus && sign != Ut1Sign.Minus)
        {
            throw new ArgumentOutOfRangeException(nameof(sign), "sign must be Plus or Minus.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(ut1Tenths, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ut1Tenths, 9);

        var symbols = new SymbolKind[WwvbFrameLayout.FrameSeconds]; // 60 × Zero

        foreach (int second in WwvbFrameLayout.MarkerSeconds)
        {
            symbols[second] = SymbolKind.Marker;
        }

        var minuteLayoutBits = FieldBits(SecondRole.Minute);
        WriteValue(symbols, minuteLayoutBits, utc.Minute);
        var hourLayoutBits = FieldBits(SecondRole.Hour);
        WriteValue(symbols, hourLayoutBits, utc.Hour);
        var dayOfYearLayoutBits = FieldBits(SecondRole.DayOfYear);
        WriteValue(symbols, dayOfYearLayoutBits, utc.DayOfYear);
        var yearLayoutBits = FieldBits(SecondRole.Year);
        WriteValue(symbols, yearLayoutBits, utc.Year % 100);
        WriteValue(symbols, WwvbFrameLayout.Ut1Correction, ut1Tenths);

        symbols[WwvbFrameLayout.LeapYearSecond] = DateTime.IsLeapYear(utc.Year)
            ? SymbolKind.One
            : SymbolKind.Zero;
        symbols[WwvbFrameLayout.LeapSecondPendingSecond] = leapSecondPending
            ? SymbolKind.One
            : SymbolKind.Zero;

        WriteUt1Sign(symbols, sign);

        WriteDstStatus(symbols, dst);

        return new Frame(
            Symbols: [.. symbols],
            StartMicros: startMicros,
            EndMicros: startMicros + 59_800_000
        );
    }

    private static void WriteDstStatus(SymbolKind[] symbols, DstStatus dstStatus)
    {
        SymbolKind[] standardBits = [SymbolKind.Zero, SymbolKind.Zero];
        SymbolKind[] daylightBits = [SymbolKind.One, SymbolKind.One];
        SymbolKind[] beginsTodayBits = [SymbolKind.One, SymbolKind.Zero];
        SymbolKind[] endsTodayBits = [SymbolKind.Zero, SymbolKind.One];

        var bits = dstStatus switch
        {
            DstStatus.Standard => standardBits,
            DstStatus.Dst => daylightBits,
            DstStatus.BeginsToday => beginsTodayBits,
            DstStatus.EndsToday => endsTodayBits,
            _ => throw new ArgumentOutOfRangeException(
                nameof(dstStatus),
                "dstStatus must be Standard, Dst, BeginsToday, or EndsToday."
            ),
        };

        foreach ((int second, SymbolKind bit) in WwvbFrameLayout.DstSeconds.Zip(bits))
        {
            symbols[second] = bit;
        }
    }

    private static void WriteUt1Sign(SymbolKind[] symbols, Ut1Sign sign)
    {
        SymbolKind[] plusBits = [SymbolKind.One, SymbolKind.Zero, SymbolKind.One];
        SymbolKind[] minusBits = [SymbolKind.Zero, SymbolKind.One, SymbolKind.Zero];

        foreach (
            (int second, SymbolKind bit) in WwvbFrameLayout.Ut1SignSeconds.Zip(
                sign == Ut1Sign.Plus ? plusBits : minusBits
            )
        )
        {
            symbols[second] = bit;
        }
    }

    private static void WriteValue(
        SymbolKind[] symbols,
        IEnumerable<LayoutBit> layoutBits,
        int value
    )
    {
        int leftover = value;
        foreach (LayoutBit layoutBit in layoutBits)
        {
            if (leftover >= layoutBit.Weight)
            {
                symbols[layoutBit.Second] = SymbolKind.One;
                leftover -= layoutBit.Weight;
            }
            else
            {
                symbols[layoutBit.Second] = SymbolKind.Zero;
            }
        }
    }

    private static IEnumerable<LayoutBit> FieldBits(SecondRole field)
    {
        return WwvbFrameLayout.Digits.Where(d => d.Field == field).SelectMany(d => d.Bits);
    }
}
