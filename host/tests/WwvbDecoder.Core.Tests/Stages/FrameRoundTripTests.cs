namespace WwvbDecoder.Core.Tests;

public class FrameRoundTripTests
{
    private const ulong Second = 1_000_000;

    [Fact]
    public void RoundTrip_500RandomMinutes_ReturnsTheSameFields()
    {
        // Arrange
        Random random = new(20261210);
        DateTimeOffset first = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset end = new(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);
        long minuteCount = (long)(end - first).TotalMinutes;
        FrameSync sync = new();
        FieldDecoder decoder = new();
        ulong start = 100 * Second;

        for (int i = 0; i < 500; i++)
        {
            DateTimeOffset utc = first.AddMinutes(random.NextInt64(minuteCount));
            Ut1Sign sign = random.Next(2) == 0 ? Ut1Sign.Plus : Ut1Sign.Minus;
            int tenths = random.Next(10);
            bool pending = random.Next(2) == 0;
            DstStatus dst = (DstStatus)random.Next(4);
            Frame encoded = WwvbFrameEncoder.Encode(utc, start, sign, tenths, pending, dst);

            // The first frame needs a marker before it to reach sync. Later frames follow on.
            List<Symbol> symbols = [.. SymbolStream.Of(encoded)];
            if (i == 0)
            {
                symbols.Insert(0, SymbolStream.LeadIn(encoded));
            }

            // Act
            DecodedFrame decoded = Run(symbols, sync, decoder)
                .ShouldHaveSingleItem($"minute {utc:o}");

            // Assert
            ShouldMatch(decoded, utc, sign, tenths, pending, dst);
            decoded.Frame.StartMicros.ShouldBe(start);
            decoded.Frame.EndMicros.ShouldBe(encoded.EndMicros);
            decoded.Frame.Symbols.SequenceEqual(encoded.Symbols).ShouldBeTrue();

            start += 60 * Second;
        }
    }

    [Theory]
    [InlineData(2000, 1, 1, 0, 0)]
    [InlineData(2096, 12, 31, 23, 59)]
    [InlineData(2099, 12, 31, 23, 59)]
    public void RoundTrip_BoundaryMinutes_ReturnTheSameFields(
        int year,
        int month,
        int day,
        int hour,
        int minute
    )
    {
        // Arrange
        DateTimeOffset utc = new(year, month, day, hour, minute, 0, TimeSpan.Zero);
        Frame encoded = WwvbFrameEncoder.Encode(
            utc,
            100 * Second,
            Ut1Sign.Minus,
            9,
            true,
            DstStatus.Dst
        );
        List<Symbol> symbols = [SymbolStream.LeadIn(encoded), .. SymbolStream.Of(encoded)];

        // Act
        DecodedFrame decoded = Run(symbols, new FrameSync(), new FieldDecoder())
            .ShouldHaveSingleItem();

        // Assert
        ShouldMatch(decoded, utc, Ut1Sign.Minus, 9, true, DstStatus.Dst);
    }

    // Materialized, because ShouldHaveSingleItem enumerates its source twice and a lazy
    // query would push every symbol into the stateful FrameSync a second time.
    private static List<DecodedFrame> Run(
        List<Symbol> symbols,
        FrameSync sync,
        FieldDecoder decoder
    ) =>
        [
            .. symbols
                .SelectMany(sync.Push)
                .OfType<FrameSyncEvent.FrameComplete>()
                .SelectMany(complete => decoder.Push(complete.Frame)),
        ];

    private static void ShouldMatch(
        DecodedFrame decoded,
        DateTimeOffset utc,
        Ut1Sign sign,
        int tenths,
        bool pending,
        DstStatus dst
    )
    {
        decoded.Minute.ShouldBe(utc.Minute);
        decoded.Hour.ShouldBe(utc.Hour);
        decoded.DayOfYear.ShouldBe(utc.DayOfYear);
        decoded.Year.ShouldBe(utc.Year % 100);
        decoded.Ut1Sign.ShouldBe(sign);
        decoded.Ut1Tenths.ShouldBe(tenths);
        decoded.LeapYear.ShouldBe(DateTime.IsLeapYear(utc.Year));
        decoded.LeapSecondPending.ShouldBe(pending);
        decoded.Dst.ShouldBe(dst);
        decoded.ErrorSeconds.ShouldBeEmpty();
    }
}
