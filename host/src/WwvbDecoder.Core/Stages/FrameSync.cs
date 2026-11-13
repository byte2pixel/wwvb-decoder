namespace WwvbDecoder.Core;

/// <summary>
/// Stage 4: Frame sync state machine.
/// Takes the symbols from stage 3 and attempts to synchronize them into frames of 60 symbols each.
/// </summary>
public sealed class FrameSync : IStage<Symbol, FrameSyncEvent>
{
    private ulong? _lastMicros;
    private bool _synced;
    private int _markerCount;
    private ulong _frameStartMicros;
    private readonly List<SymbolKind> _symbols = [];

    /// <summary>
    /// Pushes a symbol into the stage, returning one, or two events.
    /// </summary>
    /// <param name="input">The input symbol to process.</param>
    /// <returns>
    /// An enumerable collection of <see cref="FrameSyncEvent"/> instances produced from the input symbol.
    /// </returns>
    public IEnumerable<FrameSyncEvent> Push(Symbol input)
    {
        ulong gapMicros = _lastMicros is null ? 0 : input.StartMicros - _lastMicros.Value;
        _lastMicros = input.StartMicros;

        return _synced ? PushSynced(input, gapMicros) : PushSearching(input, gapMicros);
    }

    /// <summary>
    /// Returns to <see cref="FrameSyncEvent.Searching"/> and forgets the previous symbol and any partial frame.
    /// </summary>
    public void Reset()
    {
        _lastMicros = null;
        _synced = false;
        _frameStartMicros = 0;
        _markerCount = 0;
        _symbols.Clear();
    }

    private FrameSyncEvent[] PushSynced(Symbol input, ulong gapMicros)
    {
        int expected = _symbols.Count;

        if (!IsGapValid(gapMicros))
        {
            return LoseSync(
                $"gap {gapMicros / 1000} ms before second {expected}",
                input.Kind == SymbolKind.Marker ? 1 : 0
            );
        }

        bool isMarkerExpected = WwvbFrameLayout.MarkerSeconds.Contains(expected);
        if (input.Kind is SymbolKind.One or SymbolKind.Zero && isMarkerExpected)
        {
            return LoseSync($"expected marker at second {expected}, got {input.Kind}", 0);
        }

        if (input.Kind is SymbolKind.Marker && !isMarkerExpected)
        {
            return LoseSync($"expected bit at second {expected}, got Marker", 1);
        }

        return FillSlot(input);
    }

    private FrameSyncEvent[] LoseSync(string reason, int markerCount)
    {
        _synced = false;
        _markerCount = markerCount;
        _symbols.Clear();
        return [new FrameSyncEvent.SyncLost(reason), new FrameSyncEvent.Searching(markerCount)];
    }

    private FrameSyncEvent[] FillSlot(Symbol input)
    {
        if (_symbols.Count == 0)
        {
            _frameStartMicros = input.StartMicros;
        }

        _symbols.Add(input.Kind);
        var slot = new FrameSyncEvent.SlotFilled(_frameStartMicros, _symbols.Count - 1, input.Kind);
        if (_symbols.Count < WwvbFrameLayout.FrameSeconds)
        {
            return [slot];
        }

        var frame = new Frame(
            _frameStartMicros,
            input.StartMicros + input.DurationMicros,
            [.. _symbols]
        );
        _symbols.Clear();
        return [slot, new FrameSyncEvent.FrameComplete(frame)];
    }

    private FrameSyncEvent[] PushSearching(Symbol input, ulong gapMicros)
    {
        if (input.Kind != SymbolKind.Marker)
        {
            _markerCount = 0;
            return [new FrameSyncEvent.Searching(_markerCount)];
        }
        if (_markerCount > 0 && IsGapValid(gapMicros))
        {
            _markerCount++;
            _synced = true;
            _frameStartMicros = input.StartMicros;
            _symbols.Add(SymbolKind.Marker);
            return [new FrameSyncEvent.SlotFilled(input.StartMicros, 0, SymbolKind.Marker)];
        }
        _markerCount = 1;
        return [new FrameSyncEvent.Searching(_markerCount)];
    }

    private static bool IsGapValid(ulong gapMicros) => gapMicros is >= 500_000 and <= 1_500_000;
}
