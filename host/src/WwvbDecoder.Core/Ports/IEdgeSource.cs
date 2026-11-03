namespace WwvbDecoder.Core;

/// <summary>
/// A stream of receiver edges: the serial port, a capture file, or the synthetic generator.
/// </summary>
/// <remarks>
/// <para>Each enumeration starts with an <see cref="EdgeEvent.Reset"/>, so a new run clears
/// the stages without the pipeline doing it.</para>
/// <para>Between two resets, <see cref="EdgeEvent.Sample"/> timestamps never decrease. A source
/// that reads a smaller timestamp emits <c>Reset("timestamp went backwards")</c> before it.</para>
/// <para>One enumeration at a time. Enumerating again after the previous one ended starts over.</para>
/// </remarks>
public interface IEdgeSource : IAsyncDisposable
{
    /// <summary>Short display name: <c>synthetic</c>, <c>replay</c>, or <c>serial COM5</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Yields the source's events in order. Ends at the end of the data or when the port closes;
    /// I/O failures surface as exceptions from the enumeration.
    /// </summary>
    /// <param name="ct">Cancels the enumeration, which then throws <see cref="OperationCanceledException"/>.
    /// Implementations mark it <c>[EnumeratorCancellation]</c>.</param>
    IAsyncEnumerable<EdgeEvent> ReadAsync(CancellationToken ct);
}
