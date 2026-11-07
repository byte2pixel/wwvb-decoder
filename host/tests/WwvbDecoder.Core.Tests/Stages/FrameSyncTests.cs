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

    [Theory]
    [InlineData(9, SymbolKind.Zero)]
    [InlineData(9, SymbolKind.One)]
    [InlineData(19, SymbolKind.Zero)]
    [InlineData(29, SymbolKind.Zero)]
    [InlineData(39, SymbolKind.Zero)]
    [InlineData(49, SymbolKind.Zero)]
    [InlineData(59, SymbolKind.Zero)]
    [InlineData(59, SymbolKind.One)]
    public void Push_BitAtMarkerSecond_LosesSync(int second, SymbolKind kind)
    {
        // Arrange
        FrameSync stage = new();
        var next = FillTo(stage, second);

        // Act
        List<FrameSyncEvent> events = [.. PushAll(stage, Sym(next, kind))];

        // Assert
        ShouldBeLoss(events, "expected", 0);
    }

    [Theory]
    [InlineData(SymbolKind.Zero)]
    [InlineData(SymbolKind.One)]
    public void Push_BitAtSecondZeroOfNextFrame_LosesSync(SymbolKind kind)
    {
        // Arrange
        FrameSync stage = new();
        Frame frame = Encoded(Second * 100, 30);
        _ = PushAll(stage, [SymbolStream.LeadIn(frame), .. SymbolStream.Of(frame)]);

        // Act
        List<FrameSyncEvent> events = [.. stage.Push(Sym(Second * 160, kind))];

        // Assert
        ShouldBeLoss(events, "expected", 0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(58)]
    public void Push_MarkerAtBitSecond_LosesSync(int second)
    {
        // Arrange
        FrameSync stage = new();
        var next = FillTo(stage, second);

        // Act
        var events = PushAll(stage, M(next));

        // Assert
        ShouldBeLoss([.. events], "expected", 1);
    }

    [Fact]
    public void Push_AfterMarkerAtBitSecond_NextMarkerStartsNewFrame()
    {
        // Arrange
        FrameSync stage = new();
        var next = FillTo(stage, 5);
        _ = stage.Push(M(next));

        // Act
        var events = stage.Push(M(next + Second));

        // Assert
        events.ShouldBe([new FrameSyncEvent.SlotFilled(next + Second, 0, SymbolKind.Marker)]);
    }

    [Fact]
    public void Push_AfterMarkerAtBitSecond_NextBitReportsSearchingZero()
    {
        // Arrange
        FrameSync stage = new();
        var next = FillTo(stage, 5);
        _ = stage.Push(M(next));

        // Act
        var events = stage.Push(Z(next + Second));

        // Assert
        events.ShouldBe([new FrameSyncEvent.Searching(0)]);
    }

    [Fact]
    public void Push_AfterBitAtMarkerSecond_NextMarkerIsOnlyTheFirstCandidate()
    {
        // Arrange
        FrameSync stage = new();
        var next = FillTo(stage, 9);
        ulong marker1Micros = next + Second;
        ulong marker2Micros = next + Second * 2;
        _ = stage.Push(Z(next));

        // Act
        var events = PushAll(stage, M(marker1Micros), M(marker2Micros));

        // Assert
        events.ShouldBe([
            new FrameSyncEvent.Searching(1),
            new FrameSyncEvent.SlotFilled(marker2Micros, 0, SymbolKind.Marker),
        ]);
    }

    [Theory]
    [InlineData(2_000_000, SymbolKind.Zero, 0)]
    [InlineData(2_000_000, SymbolKind.Marker, 1)]
    [InlineData(2_000_000, SymbolKind.Error, 0)]
    [InlineData(1_500_001, SymbolKind.Zero, 0)]
    [InlineData(499_999, SymbolKind.Zero, 0)]
    [InlineData(0, SymbolKind.Zero, 0)]
    public void Push_GapOutOfRange_LosesSync(ulong gap, SymbolKind kind, int searching)
    {
        // Arrange
        FrameSync stage = new();
        var next = FillTo(stage, 5);

        // Act
        List<FrameSyncEvent> events = [.. PushAll(stage, Sym(next + gap - Second, kind))];

        // Assert
        ShouldBeLoss(events, "gap", searching);
    }

    [Theory]
    [InlineData(500_000)]
    [InlineData(1_500_000)]
    public void Push_GapAtRangeEdge_StaysSynced(ulong gap)
    {
        // Arrange
        FrameSync stage = new();
        var next = FillTo(stage, 5);

        // Act
        List<FrameSyncEvent> events =
        [
            .. PushAll(stage, Sym(next + gap - Second, SymbolKind.Zero)),
        ];

        // Assert
        events.ShouldBe([new FrameSyncEvent.SlotFilled(Second * 100, 5, SymbolKind.Zero)]);
    }

    [Theory]
    [InlineData(9, SymbolKind.One, 0)]
    [InlineData(5, SymbolKind.Marker, 1)]
    public void Push_GapAndWrongKind_GapIsReported(int second, SymbolKind kind, int searching)
    {
        // Arrange
        FrameSync stage = new();
        var next = FillTo(stage, second);

        // Act
        List<FrameSyncEvent> events = [.. PushAll(stage, Sym(next + Second, kind))];

        // Assert
        ShouldBeLoss(events, "gap", searching);
    }

    [Fact]
    public void Push_AfterGapLoss_MarkerThenMarkerStartsNewFrame()
    {
        // Arrange
        FrameSync stage = new();
        var late = FillTo(stage, 5) + Second;
        _ = stage.Push(M(late));

        // Act
        var events = stage.Push(M(late + Second));

        // Assert
        events.ShouldBe([new FrameSyncEvent.SlotFilled(late + Second, 0, SymbolKind.Marker)]);
    }

    [Fact]
    public void Push_AfterGapLossOnBit_NextMarkerIsOnlyTheFirstCandidate()
    {
        // Arrange
        FrameSync stage = new();
        var late = FillTo(stage, 5) + Second;
        _ = stage.Push(Z(late));

        // Act
        var events = stage.Push(M(late + Second));

        // Assert
        events.ShouldBe([new FrameSyncEvent.Searching(1)]);
    }

    [Fact]
    public void Push_GapBeforeSecondZero_LosesSyncAfterComplete()
    {
        // Arrange
        FrameSync stage = new();
        Frame frame = Encoded(Second * 100, 30);
        _ = PushAll(stage, [SymbolStream.LeadIn(frame), .. SymbolStream.Of(frame)]);

        // Act
        List<FrameSyncEvent> events = [.. stage.Push(M(Second * 162))];

        // Assert
        ShouldBeLoss(events, "gap", 1);
    }

    [Theory]
    [InlineData(9, SymbolKind.Zero, 0)]
    [InlineData(5, SymbolKind.Marker, 0)]
    [InlineData(5, SymbolKind.Zero, 1_000_000)]
    [InlineData(5, SymbolKind.Marker, 1_000_000)]
    public void Push_AfterLoss_NextFrameStartsFresh(int second, SymbolKind kind, ulong late)
    {
        // Arrange
        FrameSync stage = new();
        var next = FillTo(stage, second);
        _ = stage.Push(Sym(next + late, kind));
        Frame fresh = Encoded(Second * 200, 31);

        // Act
        List<FrameSyncEvent> events =
        [
            .. PushAll(stage, [SymbolStream.LeadIn(fresh), .. SymbolStream.Of(fresh)]),
        ];

        // Assert
        Frame completed = events
            .OfType<FrameSyncEvent.FrameComplete>()
            .ShouldHaveSingleItem()
            .Frame;
        completed.StartMicros.ShouldBe(Second * 200);
        completed.Symbols.SequenceEqual(fresh.Symbols).ShouldBeTrue();
    }

    [Fact]
    public void Push_FourConsecutiveMarkers_LoseSyncThenRestart()
    {
        // Arrange
        FrameSync stage = new();

        // Act
        List<FrameSyncEvent> events =
        [
            .. PushAll(stage, M(Second * 1), M(Second * 2), M(Second * 3), M(Second * 4)),
        ];

        // Assert
        events.Count.ShouldBe(5);
        events[0].ShouldBe(new FrameSyncEvent.Searching(1));
        events[1].ShouldBe(new FrameSyncEvent.SlotFilled(Second * 2, 0, SymbolKind.Marker));
        ShouldBeLoss([events[2], events[3]], "expected", 1);
        events[4].ShouldBe(new FrameSyncEvent.SlotFilled(Second * 4, 0, SymbolKind.Marker));
    }

    [Fact]
    public void Push_LeapSecondExtraMarker_LosesSyncAtSecondOne()
    {
        // Arrange
        FrameSync stage = new();
        Frame frame = Encoded(Second * 100, 30);
        _ = PushAll(stage, [SymbolStream.LeadIn(frame), .. SymbolStream.Of(frame)]);

        // Act
        List<FrameSyncEvent> first = [.. stage.Push(M(Second * 160))];
        List<FrameSyncEvent> second = [.. stage.Push(M(Second * 161))];

        // Assert
        first.ShouldBe([new FrameSyncEvent.SlotFilled(Second * 160, 0, SymbolKind.Marker)]);
        ShouldBeLoss(second, "expected", 1);
    }

    [Fact]
    public void Push_StartMovesBackwardsWhileSearching_IsOutOfRange()
    {
        // Arrange
        FrameSync stage = new();

        // Act
        var events = PushAll(stage, M(Second * 5), M(Second * 4));

        // Assert
        events.ShouldBe([new FrameSyncEvent.Searching(1), new FrameSyncEvent.Searching(1)]);
    }

    [Fact]
    public void Push_StartMovesBackwardsWhileSynced_IsOutOfRange()
    {
        // Arrange
        FrameSync stage = new();
        var next = FillTo(stage, 5);

        // Act
        List<FrameSyncEvent> events = [.. stage.Push(Z(next - Second * 2))];

        // Assert
        ShouldBeLoss(events, "gap", 0);
    }

    [Theory]
    [InlineData(5, 3_000_000, SymbolKind.Zero, "gap 4000 ms before second 5")]
    [InlineData(5, 1_000_000, SymbolKind.Marker, "gap 2000 ms before second 5")]
    [InlineData(9, 0, SymbolKind.One, "expected marker at second 9, got One")]
    [InlineData(4, 0, SymbolKind.Marker, "expected bit at second 4, got Marker")]
    public void Push_SyncLost_ReasonNamesTheProblem(
        int second,
        ulong late,
        SymbolKind kind,
        string reason
    )
    {
        // Arrange
        FrameSync stage = new();
        var next = FillTo(stage, second);

        // Act
        List<FrameSyncEvent> events = [.. stage.Push(Sym(next + late, kind))];

        // Assert
        events[0].ShouldBe(new FrameSyncEvent.SyncLost(reason));
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

    private static ulong FillTo(FrameSync stage, int second)
    {
        Frame frame = Encoded(Second * 100, 30);
        _ = PushAll(stage, [SymbolStream.LeadIn(frame), .. SymbolStream.Of(frame).Take(second)]);
        return Second * 100 + (ulong)second * Second;
    }

    private static void ShouldBeLoss(List<FrameSyncEvent> events, string firstWord, int searching)
    {
        events.Count.ShouldBe(2);
        events[0]
            .ShouldBeOfType<FrameSyncEvent.SyncLost>()
            .Reason.Split(' ')[0]
            .ShouldBe(firstWord);
        events[1].ShouldBe(new FrameSyncEvent.Searching(searching));
    }
}
