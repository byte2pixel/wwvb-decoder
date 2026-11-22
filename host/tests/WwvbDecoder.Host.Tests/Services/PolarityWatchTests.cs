using WwvbDecoder.Host.Services;

namespace WwvbDecoder.Host.Tests.Services;

/// <summary>The polarity warning's rule, on symbols alone.</summary>
public class PolarityWatchTests
{
    private const ulong Second = 1_000_000;

    private static Symbol Marker(ulong startMicros) => new(startMicros, 800_000, SymbolKind.Marker);

    /// <summary>The frame of the plan's test vector, 2026-12-10T19:30Z, as stage 3 would hand it on.</summary>
    private static IEnumerable<Symbol> FrameSymbols(int minute, bool inverted)
    {
        Frame frame = WwvbFrameEncoder.Encode(
            new DateTimeOffset(2026, 12, 10, 19, 30 + minute, 0, TimeSpan.Zero),
            startMicros: (ulong)minute * 60 * Second,
            Ut1Sign.Plus,
            ut1Tenths: 0,
            leapSecondPending: false,
            DstStatus.Standard
        );
        return frame.Symbols.Select(
            (kind, second) =>
                new Symbol(
                    frame.StartMicros + (ulong)second * Second,
                    200_000,
                    // With the level inverted the 0.2 s of a 0 reads as 0.8 s, and the other way round.
                    (inverted, kind) switch
                    {
                        (true, SymbolKind.Zero) => SymbolKind.Marker,
                        (true, SymbolKind.Marker) => SymbolKind.Zero,
                        _ => kind,
                    }
                )
        );
    }

    [Fact]
    public void NewWatch_DoesNotLookInverted()
    {
        new PolarityWatch().LooksInverted.ShouldBeFalse();
    }

    [Fact]
    public void TwentyMarkersInAMinute_AreNotEnough_TheNextOneIs()
    {
        // Arrange
        var watch = new PolarityWatch();

        // Act: a marker every second
        bool[] changed =
        [
            .. Enumerable.Range(0, 21).Select(i => watch.Add(Marker((ulong)i * Second))),
        ];

        // Assert: only the twenty-first reports the change
        changed.Take(20).ShouldAllBe(c => !c);
        changed[20].ShouldBeTrue();
        watch.LooksInverted.ShouldBeTrue();
    }

    [Fact]
    public void MarkersOlderThanAMinute_DoNotCount()
    {
        // Arrange
        var watch = new PolarityWatch();

        // Act: a marker every 3.1 s is 20 in any 60 s, however long it goes on
        for (int i = 0; i < 200; i++)
        {
            watch.Add(Marker((ulong)i * 3_100_000)).ShouldBeFalse();
        }

        // Assert
        watch.LooksInverted.ShouldBeFalse();
    }

    [Theory]
    [InlineData(SymbolKind.Zero)]
    [InlineData(SymbolKind.One)]
    [InlineData(SymbolKind.Error)]
    public void SymbolsThatAreNotMarkers_DoNotCount(SymbolKind kind)
    {
        // Arrange
        var watch = new PolarityWatch();

        // Act
        for (int i = 0; i < 100; i++)
        {
            watch.Add(new Symbol((ulong)i * Second, 200_000, kind)).ShouldBeFalse();
        }

        // Assert
        watch.LooksInverted.ShouldBeFalse();
    }

    [Fact]
    public void TenMinutesOfFrames_WithNoWordThatAnyWasCompleted_DoNotLookInverted()
    {
        // Arrange: seven markers a minute, and Clear is never called
        var watch = new PolarityWatch();

        // Act
        foreach (Symbol symbol in Enumerable.Range(0, 10).SelectMany(m => FrameSymbols(m, false)))
        {
            watch.Add(symbol).ShouldBeFalse();
        }

        // Assert
        watch.LooksInverted.ShouldBeFalse();
    }

    [Fact]
    public void OneFrameReadWithTheLevelInverted_LooksInverted()
    {
        // Arrange
        var watch = new PolarityWatch();

        // Act: every 0 of the frame reads as a marker
        int changes = FrameSymbols(0, inverted: true).Count(watch.Add);

        // Assert: it changed once, and stays
        changes.ShouldBe(1);
        watch.LooksInverted.ShouldBeTrue();
    }

    [Fact]
    public void OnceItLooksInverted_ItStaysSoWhenTheMarkersStop()
    {
        // Arrange
        var watch = new PolarityWatch();
        for (int i = 0; i < 21; i++)
        {
            watch.Add(Marker((ulong)i * Second));
        }

        // Act: ten minutes of a signal with no markers, then one marker
        for (int i = 0; i < 600; i++)
        {
            watch.Add(new Symbol((ulong)(30 + i) * Second, 200_000, SymbolKind.Zero));
        }
        bool changed = watch.Add(Marker(700 * Second));

        // Assert: nothing but Clear takes it back, and no change is reported twice
        changed.ShouldBeFalse();
        watch.LooksInverted.ShouldBeTrue();
    }

    [Fact]
    public void Clear_TakesItBackAndStartsTheCountAgain()
    {
        // Arrange
        var watch = new PolarityWatch();
        for (int i = 0; i < 21; i++)
        {
            watch.Add(Marker((ulong)i * Second));
        }

        // Act
        bool looked = watch.Clear();

        // Assert
        looked.ShouldBeTrue();
        watch.LooksInverted.ShouldBeFalse();

        // Twenty more in the same minute are not enough again
        for (int i = 21; i < 41; i++)
        {
            watch.Add(Marker((ulong)i * Second)).ShouldBeFalse();
        }
        watch.Add(Marker(41 * Second)).ShouldBeTrue();
    }

    [Fact]
    public void Clear_BeforeItLooksInverted_SaysSoAndForgetsTheMarkers()
    {
        // Arrange: twenty markers, one short
        var watch = new PolarityWatch();
        for (int i = 0; i < 20; i++)
        {
            watch.Add(Marker((ulong)i * Second));
        }

        // Act: a frame was completed
        bool looked = watch.Clear();

        // Assert
        looked.ShouldBeFalse();
        watch.Add(Marker(20 * Second)).ShouldBeFalse();
        watch.LooksInverted.ShouldBeFalse();
    }
}
