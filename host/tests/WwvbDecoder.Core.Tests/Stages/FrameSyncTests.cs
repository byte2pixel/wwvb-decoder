using System.Diagnostics.CodeAnalysis;

namespace WwvbDecoder.Core.Tests;

public class FrameSyncTests
{
    private const ulong Second = 1_000_000;

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
        Symbol input = M(Second * 5);

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
        Symbol firstMarker = M(Second * 99);
        Symbol secondMarker = M(Second * 100);

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
        Symbol firstMarker = M(Second * 10);
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

        Symbol[] symbols = [M(Second * 10), Sym(Second * 11, symbol), M(Second * 12)];

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
        Symbol[] symbols = [Z(Second), M(Second * 10), M(Second * 11)];

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
        Symbol[] symbols = [M(0), M(Second * 3), M(Second * 4)];

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
        Symbol[] symbols = [M(Second * 10), M(Second * 11)];

        // Act & Assert
        _ = stage.Push(symbols[0]);
        IEnumerable<FrameSyncEvent> events = stage.Push(symbols[1]);

        events.ShouldBe([
            new FrameSyncEvent.SlotFilled(symbols[1].StartMicros, 0, SymbolKind.Marker),
        ]);
    }

    [Fact]
    public void Push_WorkedExample2_FillsSlots()
    {
        // Arrange
        FrameSync stage = new();
        Symbol[] symbols =
        [
            Z(Second * 97),
            Z(Second * 98),
            M(Second * 99),
            M(Second * 100),
            Z(Second * 101),
            O(Second * 102),
        ];

        // Act
        IEnumerable<FrameSyncEvent> events = PushAll(stage, symbols);

        // Assert
        events.ShouldBe([
            new FrameSyncEvent.Searching(0),
            new FrameSyncEvent.Searching(0),
            new FrameSyncEvent.Searching(1),
            new FrameSyncEvent.SlotFilled(Second * 100, 0, SymbolKind.Marker),
            new FrameSyncEvent.SlotFilled(Second * 100, 1, SymbolKind.Zero),
            new FrameSyncEvent.SlotFilled(Second * 100, 2, SymbolKind.One),
        ]);
    }

    [Fact]
    public void Push_LeadInThenFrame_FillsEverySlotThenCompletesOnce()
    {
        // Arrange
        FrameSync stage = new();
        Frame frame = Encoded(Second * 100, 1);
        Symbol[] symbols = [SymbolStream.LeadIn(frame), .. SymbolStream.Of(frame)];

        // Act & Assert
        var events = symbols.Select(s => stage.Push(s).ToList()).ToList();
        events.Count.ShouldBe(61);
        events[0].ShouldBe([new FrameSyncEvent.Searching(1)]);
        for (int second = 0; second < 59; second++)
        {
            events[second + 1]
                .ShouldBe([
                    new FrameSyncEvent.SlotFilled(Second * 100, second, frame.Symbols[second]),
                ]);
        }
        events[60].Count.ShouldBe(2);
        events[60][0].ShouldBe(new FrameSyncEvent.SlotFilled(Second * 100, 59, SymbolKind.Marker));
        events[60][1].ShouldBeOfType<FrameSyncEvent.FrameComplete>();
    }

    [Fact]
    public void Push_FrameComplete_CarriesStartAndSymbols()
    {
        // Arrange
        FrameSync stage = new();
        Frame frame = Encoded(Second * 100, 1);

        // Act
        IEnumerable<FrameSyncEvent> events = PushAll(
            stage,
            [SymbolStream.LeadIn(frame), .. SymbolStream.Of(frame)]
        );

        // Assert
        Frame completed = events
            .OfType<FrameSyncEvent.FrameComplete>()
            .ShouldHaveSingleItem()
            .Frame;
        completed.StartMicros.ShouldBe(Second * 100);
        completed.Symbols.SequenceEqual(frame.Symbols).ShouldBeTrue();
    }

    [Fact]
    public void Push_FrameEnd_IsTheLastSymbolsStartPlusDuration()
    {
        // Arrange
        FrameSync stage = new();
        Frame frame = Encoded(Second * 100, 1);
        Symbol[] symbols = [SymbolStream.LeadIn(frame), .. SymbolStream.Of(frame)];
        symbols[^1] = new Symbol(Second * 159 + 123, 700_456, SymbolKind.Marker); // corrupt the last symbol's start and duration

        // Act
        IEnumerable<FrameSyncEvent> events = PushAll(stage, symbols);

        // Assert
        events
            .OfType<FrameSyncEvent.FrameComplete>()
            .ShouldHaveSingleItem()
            .Frame.EndMicros.ShouldBe(Second * 159 + 123 + 700_456);
    }

    [Fact]
    public void Push_TwoConsecutiveFrames_CompleteSixtySecondsApart()
    {
        // Arrange
        FrameSync stage = new();
        Frame frame1 = Encoded(Second * 100, 1);
        Frame frame2 = Encoded(Second * 160, 2);
        Symbol[] symbols =
        [
            SymbolStream.LeadIn(frame1),
            .. SymbolStream.Of(frame1),
            .. SymbolStream.Of(frame2),
        ];

        // Act
        List<FrameSyncEvent> events = [.. PushAll(stage, symbols)];

        // Assert
        var completed = events.OfType<FrameSyncEvent.FrameComplete>().ToList();
        completed.Count.ShouldBe(2);
        completed[1].Frame.StartMicros.ShouldBe(completed[0].Frame.StartMicros + 60 * Second);
        events.OfType<FrameSyncEvent.SyncLost>().ShouldBeEmpty();
        events.OfType<FrameSyncEvent.Searching>().Count().ShouldBe(1);
        events
            .OfType<FrameSyncEvent.SlotFilled>()
            .Where(s => s.Second == 1)
            .Select(s => s.FrameStartMicros)
            .ShouldBe([Second * 100, Second * 160]);
    }

    [Fact]
    public void Push_SecondFrame_DoesNotOverwriteTheFirst()
    {
        // Arrange
        FrameSync stage = new();
        Frame frame1 = Encoded(Second * 100, 1);
        Frame frame2 = Encoded(Second * 160, 2);
        Symbol[] symbols =
        [
            SymbolStream.LeadIn(frame1),
            .. SymbolStream.Of(frame1),
            .. SymbolStream.Of(frame2),
        ];

        // Act
        List<FrameSyncEvent> events = [.. PushAll(stage, symbols)];

        // Assert
        // Two frames complete, and each one's symbols equal its own encoded frame's
        var completed = events.OfType<FrameSyncEvent.FrameComplete>().ToList();
        completed.Count.ShouldBe(2);
        completed[0].Frame.Symbols.SequenceEqual(frame1.Symbols).ShouldBeTrue();
        completed[1].Frame.Symbols.SequenceEqual(frame2.Symbols).ShouldBeTrue();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(59)]
    public void Push_ErrorInFrame_IsPlaced(int errorSecond)
    {
        // Arrange
        FrameSync stage = new();
        Frame frame = Encoded(Second * 100, 1);
        Symbol[] symbols = [SymbolStream.LeadIn(frame), .. SymbolStream.Of(frame)];
        symbols[errorSecond + 1] = Sym(
            Second * 100 + (ulong)errorSecond * Second,
            SymbolKind.Error
        );

        // Act
        List<FrameSyncEvent> events = [.. PushAll(stage, symbols)];

        // Assert
        events.ShouldContain(
            new FrameSyncEvent.SlotFilled(Second * 100, errorSecond, SymbolKind.Error)
        );
        events.OfType<FrameSyncEvent.SyncLost>().ShouldBeEmpty();
        events
            .OfType<FrameSyncEvent.FrameComplete>()
            .ShouldHaveSingleItem()
            .Frame.Symbols[errorSecond]
            .ShouldBe(SymbolKind.Error);
    }

    [Fact]
    public void Push_ErrorAtSecondZeroOfNextFrame_StartsThatFrame()
    {
        // Arrange
        FrameSync stage = new();
        Frame frame1 = Encoded(Second * 100, 1);
        Frame frame2 = Encoded(Second * 160, 2);
        Symbol[] symbols =
        [
            Sym(Second * 160 + 7, SymbolKind.Error),
            .. SymbolStream.Of(frame2).TakeLast(59),
        ];
        _ = PushAll(stage, [SymbolStream.LeadIn(frame1), .. SymbolStream.Of(frame1)]);

        // Act
        List<FrameSyncEvent> events = [.. PushAll(stage, symbols)];

        // Assert
        events[0].ShouldBe(new FrameSyncEvent.SlotFilled(Second * 160 + 7, 0, SymbolKind.Error));
        var completedFrame = events
            .OfType<FrameSyncEvent.FrameComplete>()
            .ShouldHaveSingleItem()
            .Frame;
        completedFrame.StartMicros.ShouldBe(Second * 160 + 7);
        completedFrame.Symbols[0].ShouldBe(SymbolKind.Error);
    }

    private static Symbol M(ulong startMicros) => new(startMicros, 0, SymbolKind.Marker);

    private static Symbol Z(ulong startMicros) => new(startMicros, 0, SymbolKind.Zero);

    private static Symbol O(ulong startMicros) => new(startMicros, 0, SymbolKind.One);

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

    private static Frame Encoded(ulong start, int minute) =>
        WwvbFrameEncoder.Encode(
            new DateTimeOffset(2026, 12, 10, 19, minute, 0, TimeSpan.Zero),
            start,
            Ut1Sign.Plus,
            0,
            false,
            DstStatus.Standard
        );
}
