namespace WwvbDecoder.Core;

/// <summary>
/// Stage 5: Field decoder.
/// Takes a frame of 60 symbols and decodes it into a <see cref="DecodedFrame"/> instance.
/// </summary>
public sealed class FieldDecoder : IStage<Frame, DecodedFrame>
{
    /// <inheritdoc />
    public IEnumerable<DecodedFrame> Push(Frame input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var minutes = SumBits(GetBits(SecondRole.Minute), input);
        var hours = SumBits(GetBits(SecondRole.Hour), input);
        var dayOfYear = SumBits(GetBits(SecondRole.DayOfYear), input);
        var year = SumBits(GetBits(SecondRole.Year), input);
        var ut1Tenths = SumBits(WwvbFrameLayout.Ut1Correction, input);
        Ut1Sign ut1Sign = GetUt1Sign(input);
        bool leapYear = GetLeapYear(input);
        bool leapSecondPending = GetLeapSecondPending(input);
        DstStatus dst = GetDstStatus(input);

        var errorSlots = input
            .Symbols.Zip(WwvbFrameLayout.Roles, (symbol, role) => (symbol, role))
            .ToArray();
        List<int> errorSecondsList = [];
        for (int second = 0; second < errorSlots.Length; second++)
        {
            (SymbolKind symbol, SecondRole role) = errorSlots[second];
            if (role is SecondRole.Unused or SecondRole.Marker)
            {
                continue;
            }

            if (symbol is SymbolKind.Error or SymbolKind.Marker)
            {
                errorSecondsList.Add(second);
            }
        }

        return
        [
            new DecodedFrame(
                Frame: input,
                Minute: minutes,
                Hour: hours,
                DayOfYear: dayOfYear,
                Year: year,
                Ut1Sign: ut1Sign,
                Ut1Tenths: ut1Tenths,
                LeapYear: leapYear,
                LeapSecondPending: leapSecondPending,
                Dst: dst,
                ErrorSeconds: [.. errorSecondsList]
            ),
        ];
    }

    private static IEnumerable<LayoutBit> GetBits(SecondRole secondRole)
    {
        return WwvbFrameLayout.Digits.Where(d => d.Field == secondRole).SelectMany(d => d.Bits);
    }

    private static bool GetLeapYear(Frame input)
    {
        var symbol = input.Symbols[WwvbFrameLayout.LeapYearSecond];
        return symbol == SymbolKind.One;
    }

    private static bool GetLeapSecondPending(Frame input)
    {
        var symbol = input.Symbols[WwvbFrameLayout.LeapSecondPendingSecond];
        return symbol == SymbolKind.One;
    }

    private static Ut1Sign GetUt1Sign(Frame input)
    {
        bool[] signSeconds =
        [
            .. WwvbFrameLayout.Ut1SignSeconds.Select(i => input.Symbols[i] == SymbolKind.One),
        ];

        return signSeconds switch
        {
            [true, false, true] => Ut1Sign.Plus,
            [false, true, false] => Ut1Sign.Minus,
            _ => Ut1Sign.Invalid,
        };
    }

    private static DstStatus GetDstStatus(Frame input)
    {
        bool[] dstSeconds =
        [
            .. WwvbFrameLayout.DstSeconds.Select(i => input.Symbols[i] == SymbolKind.One),
        ];

        return dstSeconds switch
        {
            [true, true] => DstStatus.Dst,
            [true, false] => DstStatus.BeginsToday,
            [false, true] => DstStatus.EndsToday,
            _ => DstStatus.Standard,
        };
    }

    /// <summary>
    /// Noop. This stage does not maintain any state.
    /// </summary>
    public void Reset() { }

    private static int SumBits(IEnumerable<LayoutBit> bits, Frame frame)
    {
        return bits.Sum(bit => frame.Symbols[bit.Second] == SymbolKind.One ? bit.Weight : 0);
    }
}
