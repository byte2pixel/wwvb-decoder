namespace WwvbDecoder.Core;

/// <summary>
/// Stage 4's output. Each <c>Push</c> yields one event, or two.
/// </summary>
public abstract record FrameSyncEvent
{
    private FrameSyncEvent() { }

    /// <summary>
    /// Not synced, still counting markers.
    /// </summary>
    /// <param name="ConsecutiveMarkers">The number of consecutive markers found, 0 or 1.
    /// Reaching 2 starts a frame, which is reported as <see cref="FrameSyncEvent.SlotFilled"/>.</param>
    public sealed record Searching(int ConsecutiveMarkers) : FrameSyncEvent;

    /// <summary>
    /// A symbol was placed at <paramref name="Second"/>.
    /// </summary>
    /// <param name="FrameStartMicros">is the timestamp of the second-0 symbol.</param>
    /// <param name="Second">The second (0-59) of the frame that is being filled with a symbol.</param>
    /// <param name="Kind">The kind of symbol that was placed in the slot.</param>
    public sealed record SlotFilled(ulong FrameStartMicros, int Second, SymbolKind Kind)
        : FrameSyncEvent;

    /// <summary>
    /// Second 59 was placed. Sync stays on, expecting second 0 of the next frame.
    /// </summary>
    /// <param name="Frame">The completed frame, which contains all 60 symbols for that frame.</param>
    public sealed record FrameComplete(Frame Frame) : FrameSyncEvent;

    /// <summary>
    /// The event of losing synchronization in the WWVB signal.
    /// </summary>
    /// <param name="Reason">Why sync was lost, as display text for people. The wording may change;
    /// don't parse it. Code that needs to react should use the event case.</param>
    public sealed record SyncLost(string Reason) : FrameSyncEvent;
}
