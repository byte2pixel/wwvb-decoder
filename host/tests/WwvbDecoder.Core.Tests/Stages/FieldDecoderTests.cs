namespace WwvbDecoder.Core.Tests;

public class FieldDecoderTests
{
    private static DateTimeOffset CleanMinute => new(2026, 12, 10, 19, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Push_KnownFrame_ReadsEveryField()
    {
        // Arrange
        var frame = WwvbFrameEncoder.Encode(
            CleanMinute,
            61_000_000,
            Ut1Sign.Minus,
            7,
            true,
            DstStatus.BeginsToday
        );

        // Act
        DecodedFrame decoded = new FieldDecoder().Push(frame).ShouldHaveSingleItem();

        // Assert
        decoded.Frame.ShouldBeSameAs(frame);
        decoded.Minute.ShouldBe(30);
        decoded.Hour.ShouldBe(19);
        decoded.DayOfYear.ShouldBe(344);
        decoded.Year.ShouldBe(26);
        decoded.Ut1Sign.ShouldBe(Ut1Sign.Minus);
        decoded.Ut1Tenths.ShouldBe(7);
        decoded.LeapYear.ShouldBeFalse();
        decoded.LeapSecondPending.ShouldBeTrue();
        decoded.Dst.ShouldBe(DstStatus.BeginsToday);
        decoded.ErrorSeconds.IsDefault.ShouldBeFalse();
        decoded.ErrorSeconds.ShouldBeEmpty();
    }

    [Fact]
    public void Push_WorkedExample3_ReadsTheFlippedBit()
    {
        // Arrange
        // csharpier-ignore
        SymbolKind[] symbols =
        [
            M, Z, O, O, Z, Z, O, Z, O, M,
            Z, Z, Z, O, Z, O, Z, Z, O, M,
            Z, Z, O, O, Z, Z, O, Z, Z, M,
            Z, O, Z, Z, Z, Z, O, Z, O, M,
            Z, Z, Z, Z, Z, Z, Z, O, Z, M,
            Z, O, O, Z, Z, Z, Z, Z, Z, M,
        ];

        // Act
        DecodedFrame decoded = Decode(symbols);

        // Assert
        decoded.Minute.ShouldBe(35);
        decoded.Hour.ShouldBe(19);
        decoded.DayOfYear.ShouldBe(344);
        decoded.Year.ShouldBe(26);
        decoded.Ut1Sign.ShouldBe(Ut1Sign.Plus);
        decoded.Ut1Tenths.ShouldBe(0);
        decoded.LeapYear.ShouldBeFalse();
        decoded.LeapSecondPending.ShouldBeFalse();
        decoded.Dst.ShouldBe(DstStatus.Standard);
        decoded.ErrorSeconds.IsDefault.ShouldBeFalse();
        decoded.ErrorSeconds.ShouldBeEmpty();
    }

    [Fact]
    public void Push_EveryFieldBitSet_ReadsTheMaximums()
    {
        // Arrange
        SymbolKind[] symbols = AllFieldBitsSet();

        // Act
        DecodedFrame decoded = Decode(symbols);

        // Assert
        decoded.Minute.ShouldBe(85);
        decoded.Hour.ShouldBe(45);
        decoded.DayOfYear.ShouldBe(465);
        decoded.Year.ShouldBe(165);
        decoded.Ut1Sign.ShouldBe(Ut1Sign.Invalid);
        decoded.Ut1Tenths.ShouldBe(15);
        decoded.LeapYear.ShouldBeTrue();
        decoded.LeapSecondPending.ShouldBeTrue();
        decoded.Dst.ShouldBe(DstStatus.Dst);
        decoded.ErrorSeconds.IsDefault.ShouldBeFalse();
        decoded.ErrorSeconds.ShouldBeEmpty();
    }

    [Fact]
    public void Push_IllegalUnitsDigit_ReadsFifteen()
    {
        // Arrange
        SymbolKind[] symbols = Clean(new DateTimeOffset(2026, 12, 10, 19, 00, 0, TimeSpan.Zero));
        symbols[5] = O;
        symbols[6] = O;
        symbols[7] = O;
        symbols[8] = O;

        // Act
        DecodedFrame decoded = Decode(symbols);

        // Assert
        decoded.Minute.ShouldBe(15);
    }

    [Theory]
    [InlineData(O, Z, O, Ut1Sign.Plus)]
    [InlineData(Z, O, Z, Ut1Sign.Minus)]
    [InlineData(Z, Z, Z, Ut1Sign.Invalid)]
    [InlineData(Z, Z, O, Ut1Sign.Invalid)]
    [InlineData(Z, O, O, Ut1Sign.Invalid)]
    [InlineData(O, Z, Z, Ut1Sign.Invalid)]
    [InlineData(O, O, Z, Ut1Sign.Invalid)]
    [InlineData(O, O, O, Ut1Sign.Invalid)]
    [InlineData(O, E, O, Ut1Sign.Plus)]
    [InlineData(E, O, E, Ut1Sign.Minus)]
    [InlineData(O, M, O, Ut1Sign.Plus)]
    [InlineData(M, O, M, Ut1Sign.Minus)]
    public void Push_Ut1Sign_ReadsPerContract(
        SymbolKind second36,
        SymbolKind second37,
        SymbolKind second38,
        Ut1Sign expected
    )
    {
        // Arrange
        SymbolKind[] symbols = Clean();
        symbols[36] = second36;
        symbols[37] = second37;
        symbols[38] = second38;

        // Act
        DecodedFrame decoded = Decode(symbols);

        // Assert
        decoded.Ut1Sign.ShouldBe(expected);
    }

    [Theory]
    [InlineData(Z, Z, DstStatus.Standard)]
    [InlineData(O, O, DstStatus.Dst)]
    [InlineData(O, Z, DstStatus.BeginsToday)]
    [InlineData(Z, O, DstStatus.EndsToday)]
    [InlineData(O, E, DstStatus.BeginsToday)]
    [InlineData(E, O, DstStatus.EndsToday)]
    [InlineData(O, M, DstStatus.BeginsToday)]
    [InlineData(M, O, DstStatus.EndsToday)]
    public void Push_Dst_ReadsPerContract(
        SymbolKind second57,
        SymbolKind second58,
        DstStatus expected
    )
    {
        // Arrange
        SymbolKind[] symbols = Clean();
        symbols[57] = second57;
        symbols[58] = second58;

        // Act
        DecodedFrame decoded = Decode(symbols);

        // Assert
        decoded.Dst.ShouldBe(expected);
    }

    [Theory]
    [InlineData(Z, O, false, true)]
    [InlineData(O, Z, true, false)]
    public void Push_LeapFlags_ReadTheirOwnSeconds(
        SymbolKind leapYear,
        SymbolKind leapSecond,
        bool expectedLeapYear,
        bool expectedLeapSecond
    )
    {
        // Arrange
        SymbolKind[] symbols = Clean();
        symbols[55] = leapYear;
        symbols[56] = leapSecond;

        // Act
        DecodedFrame decoded = Decode(symbols);

        // Assert
        decoded.LeapYear.ShouldBe(expectedLeapYear);
        decoded.LeapSecondPending.ShouldBe(expectedLeapSecond);
    }

    [Fact]
    public void Push_ErrorAtMinuteFortyBit_ReadsTheRemainder()
    {
        // Arrange
        SymbolKind[] symbols = Clean(new DateTimeOffset(2026, 12, 10, 19, 45, 0, TimeSpan.Zero));
        symbols[1].ShouldBe(O);
        symbols[1] = E;

        // Act
        DecodedFrame decoded = Decode(symbols);

        // Assert
        decoded.Minute.ShouldBe(5);
        decoded.ErrorSeconds.ShouldBe([1]);
    }

    [Theory]
    [InlineData(E)]
    [InlineData(M)]
    public void Push_BadSymbolInEveryField_ReadsZeroAndListsAscending(SymbolKind badSymbol)
    {
        // Arrange
        SymbolKind[] symbols = AllFieldBitsSet();
        symbols[1] = badSymbol;
        symbols[12] = badSymbol;
        symbols[22] = badSymbol;
        symbols[36] = badSymbol;
        symbols[40] = badSymbol;
        symbols[45] = badSymbol;
        symbols[55] = badSymbol;
        symbols[56] = badSymbol;
        symbols[57] = badSymbol;

        // Act
        DecodedFrame decoded = Decode(symbols);

        // Assert
        decoded.Minute.ShouldBe(45);
        decoded.Hour.ShouldBe(25);
        decoded.DayOfYear.ShouldBe(265);
        decoded.Year.ShouldBe(85);
        decoded.Ut1Sign.ShouldBe(Ut1Sign.Invalid);
        decoded.Ut1Tenths.ShouldBe(7);
        decoded.LeapYear.ShouldBeFalse();
        decoded.LeapSecondPending.ShouldBeFalse();
        decoded.Dst.ShouldBe(DstStatus.EndsToday);
        decoded.ErrorSeconds.ShouldBe([1, 12, 22, 36, 40, 45, 55, 56, 57]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(59)]
    [InlineData(4)]
    [InlineData(54)]
    public void Push_ErrorAtMarkerOrUnusedSecond_IsNotListed(int second)
    {
        // Arrange
        SymbolKind[] symbols = Clean();
        symbols[second] = E;

        // Act
        DecodedFrame decoded = Decode(symbols);

        // Assert
        decoded.ErrorSeconds.ShouldBeEmpty();
    }

    [Fact]
    public void Push_BitsAtMarkerAndUnusedSeconds_AreIgnored()
    {
        // Arrange
        DecodedFrame expected = Decode(Clean());
        SymbolKind[] symbols = Clean();
        foreach (int second in WwvbFrameLayout.MarkerSeconds)
        {
            symbols[second] = Z;
        }

        foreach (int second in WwvbFrameLayout.UnusedSeconds)
        {
            symbols[second] = O;
        }

        // Act
        DecodedFrame decoded = Decode(symbols);

        // Assert
        decoded.Minute.ShouldBe(expected.Minute);
        decoded.Hour.ShouldBe(expected.Hour);
        decoded.DayOfYear.ShouldBe(expected.DayOfYear);
        decoded.Year.ShouldBe(expected.Year);
        decoded.Ut1Tenths.ShouldBe(expected.Ut1Tenths);
        decoded.ErrorSeconds.ShouldBeEmpty();
    }

    [Fact]
    public void Push_NullFrame_ThrowsWithoutEnumerating()
    {
        // Arrange
        FieldDecoder stage = new();

        // Act and Assert
        Should.Throw<ArgumentNullException>(() => stage.Push(null!));
    }

    [Fact]
    public void Push_PreviousErrors_DoNotCarryOver()
    {
        // Arrange
        FieldDecoder stage = new();
        SymbolKind[] bad = Clean();
        bad[5] = E;

        // Act
        DecodedFrame first = stage.Push(Frame(bad)).ShouldHaveSingleItem();
        DecodedFrame second = stage.Push(Frame(Clean())).ShouldHaveSingleItem();

        // Assert
        first.ErrorSeconds.ShouldBe([5]);
        second.ErrorSeconds.ShouldBeEmpty();
    }

    [Fact]
    public void Reset_LeavesTheDecoderWorking()
    {
        // Arrange
        FieldDecoder stage = new();

        // Act
        stage.Reset();
        DecodedFrame decoded = stage.Push(Frame(Clean())).ShouldHaveSingleItem();

        // Assert
        decoded.Minute.ShouldBe(30);
    }

    private const SymbolKind M = SymbolKind.Marker,
        O = SymbolKind.One,
        Z = SymbolKind.Zero,
        E = SymbolKind.Error;

    private static DecodedFrame Decode(SymbolKind[] symbols) =>
        new FieldDecoder().Push(Frame(symbols)).ShouldHaveSingleItem();

    private static Frame Frame(SymbolKind[] symbols) => new(0, 59_800_000, [.. symbols]);

    private static SymbolKind[] Clean(DateTimeOffset? minute = null) =>
        [
            .. WwvbFrameEncoder
                .Encode(minute ?? CleanMinute, 0, Ut1Sign.Plus, 0, false, DstStatus.Standard)
                .Symbols,
        ];

    private static SymbolKind[] AllFieldBitsSet()
    {
        var symbols = new SymbolKind[60];
        for (int second = 0; second < 60; second++)
        {
            symbols[second] = WwvbFrameLayout.Roles[second] switch
            {
                SecondRole.Marker => M,
                SecondRole.Unused => Z,
                _ => O,
            };
        }

        return symbols;
    }
}
