using System.Diagnostics.CodeAnalysis;

namespace WwvbDecoder.Core.Tests;

public class FrameSyncTests
{
    [Theory]
    [InlineData(SymbolKind.Zero)]
    [InlineData(SymbolKind.One)]
    [InlineData(SymbolKind.Error)]
    public void Push_NonMarker_ReportsSearchingZero(SymbolKind kind)
    {
        // Arrange
        FrameSync stage = new();
        Symbol input = Sym(0, kind);

        // Act
        IEnumerable<FrameSyncEvent> events = stage.Push(input);

        // Assert
        events.ShouldBe([new FrameSyncEvent.Searching(0)]);
    }

    [Fact]
    public void Push_FirstMarker_ReportsSearchingOne()
    {
        // Arrange
        FrameSync stage = new();
        Symbol input = M(5_000_000);

        // Act
        IEnumerable<FrameSyncEvent> events = stage.Push(input);

        // Assert
        events.ShouldBe([new FrameSyncEvent.Searching(1)]);
    }

    [Fact]
    public void Push_SecondConsecutiveMarker_StartsFrameAtSecondZero()
    {
        // Arrange
        FrameSync stage = new();
        Symbol firstMarker = M(99_000_000);
        Symbol secondMarker = M(100_000_000);

        // Act & Assert
        IEnumerable<FrameSyncEvent> events1 = stage.Push(firstMarker);
        events1.ShouldBe([new FrameSyncEvent.Searching(1)]);

        IEnumerable<FrameSyncEvent> events2 = stage.Push(secondMarker);
        events2.ShouldBe([
            new FrameSyncEvent.SlotFilled(secondMarker.StartMicros, 0, SymbolKind.Marker),
        ]);
    }

    [Theory]
    [InlineData(499_999, false)]
    [InlineData(500_000, true)]
    [InlineData(1_500_000, true)]
    [InlineData(1_500_001, false)]
    public void Push_MarkerGap_CountsOnlyInRange(ulong gap, bool startsFrame)
    {
        // Arrange
        FrameSync stage = new();
        Symbol firstMarker = M(10_000_000);
        Symbol secondMarker = M(firstMarker.StartMicros + gap);
        FrameSyncEvent[] expectedEvents = startsFrame
            ? [new FrameSyncEvent.SlotFilled(secondMarker.StartMicros, 0, SymbolKind.Marker)]
            : [new FrameSyncEvent.Searching(1)];

        // Act
        _ = stage.Push(firstMarker);
        IEnumerable<FrameSyncEvent> events = stage.Push(secondMarker);

        // Assert
        events.ShouldBe(expectedEvents);
    }

    [Theory]
    [InlineData(SymbolKind.Zero)]
    [InlineData(SymbolKind.One)]
    [InlineData(SymbolKind.Error)]
    public void Push_NonMarkerBetweenMarkers_RestartsTheCount(SymbolKind symbol)
    {
        // Arrange
        FrameSync stage = new();

        Symbol[] symbols = [M(10_000_000), Sym(11_000_000, symbol), M(12_000_000)];

        // Act
        IEnumerable<FrameSyncEvent> events = PushAll(stage, symbols);

        // Assert
        events.ShouldBe([
            new FrameSyncEvent.Searching(1),
            new FrameSyncEvent.Searching(0),
            new FrameSyncEvent.Searching(1),
        ]);
    }

    [Fact]
    public void Push_MarkerAfterNonMarker_IsTheCandidateForTheNextMarker()
    {
        // Arrange
        FrameSync stage = new();
        Symbol[] symbols = [Z(1_000_000), M(10_000_000), M(11_000_000)];

        // Act & Assert
        IEnumerable<FrameSyncEvent> events = stage.Push(symbols[0]);
        events.ShouldBe([new FrameSyncEvent.Searching(0)]);

        events = stage.Push(symbols[1]);
        events.ShouldBe([new FrameSyncEvent.Searching(1)]);

        events = stage.Push(symbols[2]);
        events.ShouldBe([
            new FrameSyncEvent.SlotFilled(symbols[2].StartMicros, 0, SymbolKind.Marker),
        ]);
    }

    [Fact]
    public void Push_TooLateMarker_BecomesTheNewCandidate()
    {
        // Arrange
        FrameSync stage = new();
        Symbol[] symbols = [M(0), M(3_000_000), M(4_000_000)];

        // Act & Assert
        IEnumerable<FrameSyncEvent> events = stage.Push(symbols[0]);
        events.ShouldBe([new FrameSyncEvent.Searching(1)]);

        events = stage.Push(symbols[1]);
        events.ShouldBe([new FrameSyncEvent.Searching(1)]);

        events = stage.Push(symbols[2]);
        events.ShouldBe([
            new FrameSyncEvent.SlotFilled(symbols[2].StartMicros, 0, SymbolKind.Marker),
        ]);
    }

    [Fact]
    public void Push_ResultNotEnumerated_StillAdvances()
    {
        // Arrange
        FrameSync stage = new();
        Symbol[] symbols = [M(10_000_000), M(11_000_000)];

        // Act & Assert
        _ = stage.Push(symbols[0]);
        IEnumerable<FrameSyncEvent> events = stage.Push(symbols[1]);

        events.ShouldBe([
            new FrameSyncEvent.SlotFilled(symbols[1].StartMicros, 0, SymbolKind.Marker),
        ]);
    }

    private static Symbol M(ulong startMicros) => new(startMicros, 0, SymbolKind.Marker);

    private static Symbol Z(ulong startMicros) => new(startMicros, 0, SymbolKind.Zero);

    private static Symbol Sym(ulong startMicros, SymbolKind kind) => new(startMicros, 0, kind);

    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "Use to break compile if the contract changes."
    )]
    private static IEnumerable<FrameSyncEvent> PushAll(
        IStage<Symbol, FrameSyncEvent> stage,
        params Symbol[] symbols
    )
    {
        List<FrameSyncEvent> events = [];
        foreach (Symbol symbol in symbols)
        {
            events.AddRange(stage.Push(symbol));
        }
        return events;
    }
}
