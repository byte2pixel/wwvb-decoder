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

        if (_synced)
        {
            switch (_symbols.Count)
            {
                case 0:
                    _frameStartMicros = input.StartMicros;
                    _symbols.Add(input.Kind);
                    return [new FrameSyncEvent.SlotFilled(_frameStartMicros, 0, input.Kind)];
                case < WwvbFrameLayout.FrameSeconds - 1:
                    _symbols.Add(input.Kind);
                    return
                    [
                        new FrameSyncEvent.SlotFilled(
                            _frameStartMicros,
                            _symbols.Count - 1,
                            input.Kind
                        ),
                    ];
                case WwvbFrameLayout.FrameSeconds - 1:
                {
                    _symbols.Add(input.Kind);
                    var frame = new Frame(
                        _frameStartMicros,
                        input.StartMicros + input.DurationMicros,
                        [.. _symbols]
                    );
                    _symbols.Clear();
                    return
                    [
                        new FrameSyncEvent.SlotFilled(
                            _frameStartMicros,
                            WwvbFrameLayout.FrameSeconds - 1,
                            input.Kind
                        ),
                        new FrameSyncEvent.FrameComplete(frame),
                    ];
                }
            }
        }

        if (input.Kind != SymbolKind.Marker)
        {
            _markerCount = 0;
            return [new FrameSyncEvent.Searching(_markerCount)];
        }
        if (_markerCount > 0 && IsMarkerGapValid(gapMicros))
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

    private static bool IsMarkerGapValid(ulong gapMicros) =>
        gapMicros is >= 500_000 and <= 1_500_000;
}
