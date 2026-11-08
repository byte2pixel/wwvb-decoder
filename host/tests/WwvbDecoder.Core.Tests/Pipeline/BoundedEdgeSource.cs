using System.Runtime.CompilerServices;

namespace WwvbDecoder.Core.Tests;

/// <summary>
/// Ends any source after a given timestamp, so the pipeline can be tested with a finite number of events.
/// </summary>
/// <param name="untilMicros">The timestamp in microseconds until which events should be yielded.</param>
public class BoundedEdgeSource(IEdgeSource inner, ulong untilMicros) : IEdgeSource
{
    public string Name => inner.Name;

    public async IAsyncEnumerable<EdgeEvent> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        await foreach (var e in inner.ReadAsync(ct))
        {
            if (e is EdgeEvent.Sample sample && sample.Edge.Micros > untilMicros)
            {
                yield break;
            }
            yield return e;
        }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return inner.DisposeAsync();
    }
}
