namespace WwvbDecoder.Core.Tests;

public class WwvbFrameEncoderTests
{
    private const SymbolKind M = SymbolKind.Marker,
        O = SymbolKind.One,
        Z = SymbolKind.Zero;

    [Fact]
    public void Encode_KnownVector_2026_12_10T1930Z()
    {
        // csharpier-ignore
        SymbolKind[] expected =
        [
         // 0  1  2  3  4  5  6  7  8  9
            M, Z, O, O, Z, Z, Z, Z, Z, M, //   0-9: minute 30 = 20 + 10
            Z, Z, Z, O, Z, O, Z, Z, O, M, // 10-19: hour 19 = 10+8+1
            Z, Z, O, O, Z, Z, O, Z, Z, M, // 20-29: day-of-year 344 = 200+100+40+...
            Z, O, Z, Z, Z, Z, O, Z, O, M, // 30-39: ...+4, UT1 + = 1 0 1
            Z, Z, Z, Z, Z, Z, Z, O, Z, M, // 40-49: UT1 0, year 26 = 20+...
            Z, O, O, Z, Z, Z, Z, Z, Z, M, // 50-59: ...+4+2, not leap year, no leap second pending, standard time
        ];

        // Act
        Frame frame = WwvbFrameEncoder.Encode(
            new DateTimeOffset(2026, 12, 10, 19, 30, 00, TimeSpan.Zero),
            61_000_000,
            Ut1Sign.Plus,
            0,
            false,
            DstStatus.Standard
        );

        // Assert
        frame.StartMicros.ShouldBe(61_000_000UL);
        frame.EndMicros.ShouldBe(61_000_000UL + 59_800_000UL);
        frame.Symbols.ShouldBe(expected);
    }

    [Fact]
    public void Encode_Ut1AndLeapSecond_WriteTheirSeconds()
    {
        // Arrange
        Dictionary<int, SymbolKind> expected = new()
        {
            { 36, Z }, // UT1 sign bit 1
            { 37, O }, // UT1 sign bit 2
            { 38, Z }, // UT1 sign bit 3
            { 40, O }, // UT1 correction 8
            { 41, Z }, // UT1 correction 4
            { 42, Z }, // UT1 correction 2
            { 43, O }, // UT1 correction 1
            { 56, O }, // leap second pending
        };

        // Act
        Frame frame = WwvbFrameEncoder.Encode(
            new DateTimeOffset(2024, 6, 30, 12, 0, 0, TimeSpan.Zero),
            100_000_000,
            Ut1Sign.Minus,
            9,
            true,
            DstStatus.BeginsToday
        );

        // Assert
        foreach ((int second, SymbolKind symbol) in expected)
        {
            frame.Symbols[second].ShouldBe(symbol, $"second {second} should be {symbol}");
        }
    }

    [Theory]
    [InlineData(DstStatus.Standard, new[] { SymbolKind.Zero, SymbolKind.Zero })]
    [InlineData(DstStatus.Dst, new[] { SymbolKind.One, SymbolKind.One })]
    [InlineData(DstStatus.BeginsToday, new[] { SymbolKind.One, SymbolKind.Zero })]
    [InlineData(DstStatus.EndsToday, new[] { SymbolKind.Zero, SymbolKind.One })]
    public void Encode_Dst_WritesSecond57And58(DstStatus status, SymbolKind[] expected)
    {
        // Act
        Frame frame = WwvbFrameEncoder.Encode(
            new DateTimeOffset(2024, 6, 30, 12, 0, 0, TimeSpan.Zero),
            100_000_000,
            Ut1Sign.Plus,
            0,
            false,
            status
        );

        // Assert
        frame.Symbols[57].ShouldBe(expected[0], $"second 57 should be {expected[0]}");
        frame.Symbols[58].ShouldBe(expected[1], $"second 58 should be {expected[1]}");
    }

    [Theory]
    [InlineData(2026, false)]
    [InlineData(2028, true)]
    public void Encode_LeapYearFlag_FollowsTheYear(int year, bool expectedLeapYear)
    {
        // Act
        Frame frame = WwvbFrameEncoder.Encode(
            new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero),
            0,
            Ut1Sign.Plus,
            0,
            false,
            DstStatus.Standard
        );

        // Assert
        frame
            .Symbols[WwvbFrameLayout.LeapYearSecond]
            .ShouldBe(
                expectedLeapYear ? SymbolKind.One : SymbolKind.Zero,
                $"year {year} should have leap year flag {expectedLeapYear}"
            );
    }

    [Fact]
    public void Encode_ConvertsToUtc()
    {
        // Arrange & Act
        Frame local = WwvbFrameEncoder.Encode(
            new DateTimeOffset(2026, 12, 10, 14, 30, 00, TimeSpan.FromHours(-5)), // 14:30 EST is 19:30 UTC
            0,
            Ut1Sign.Plus,
            0,
            false,
            DstStatus.Standard
        );
        Frame utc = WwvbFrameEncoder.Encode(
            new DateTimeOffset(2026, 12, 10, 19, 30, 00, TimeSpan.Zero),
            0,
            Ut1Sign.Plus,
            0,
            false,
            DstStatus.Standard
        );

        // Assert
        local.Symbols.ShouldBe(utc.Symbols);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    public void Encode_NotOnAWholeMinute_Throws(int second, int millisecond, int tick = 0)
    {
        // Arrange
        DateTimeOffset notOnAWholeMinute = new DateTimeOffset(
            2026,
            12,
            10,
            19,
            30,
            second,
            TimeSpan.Zero
        )
            .AddMilliseconds(millisecond)
            .AddTicks(tick);

        // Act & Assert
        Should
            .Throw<ArgumentException>(() =>
                WwvbFrameEncoder.Encode(
                    notOnAWholeMinute,
                    0,
                    Ut1Sign.Plus,
                    0,
                    false,
                    DstStatus.Standard
                )
            )
            .ShouldBeOfType<ArgumentException>();
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2100)]
    public void Encode_YearOutsideTwoThousands_Throws(int year)
    {
        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() =>
            WwvbFrameEncoder.Encode(
                new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero),
                0,
                Ut1Sign.Plus,
                0,
                false,
                DstStatus.Standard
            )
        );
    }

    [Theory]
    [InlineData(Ut1Sign.Invalid)]
    [InlineData((Ut1Sign)3)]
    public void Encode_InvalidUt1Sign_Throws(Ut1Sign sign)
    {
        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() =>
            WwvbFrameEncoder.Encode(
                new DateTimeOffset(2026, 12, 10, 19, 30, 0, TimeSpan.Zero),
                0,
                sign,
                0,
                false,
                DstStatus.Standard
            )
        );
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void Encode_Ut1TenthsOutsideZeroToNine_Throws(int ut1Tenths)
    {
        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() =>
            WwvbFrameEncoder.Encode(
                new DateTimeOffset(2026, 12, 10, 19, 30, 0, TimeSpan.Zero),
                0,
                Ut1Sign.Plus,
                ut1Tenths,
                false,
                DstStatus.Standard
            )
        );
    }
}
