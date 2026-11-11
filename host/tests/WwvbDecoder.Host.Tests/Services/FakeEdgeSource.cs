using System.Runtime.CompilerServices;

namespace WwvbDecoder.Host.Tests.Services;

/// <summary>
/// Resets, yields <see cref="Script"/>, then faults, ends, or waits for cancellation.
/// Records whether it was disposed.
/// </summary>
internal sealed class FakeEdgeSource : IEdgeSource
{
    public string Name => "fake";
    public bool Disposed { get; private set; }

    /// <summary>Events yielded after the initial reset.</summary>
    public IReadOnlyList<EdgeEvent> Script { get; init; } = [];

    public Exception? Fault { get; init; }
    public bool EndsAfterReset { get; init; }

    public async IAsyncEnumerable<EdgeEvent> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        yield return new EdgeEvent.Reset("start");
        foreach (EdgeEvent edgeEvent in Script)
        {
            yield return edgeEvent;
        }
        if (Fault is not null)
        {
            throw Fault;
        }
        if (EndsAfterReset)
        {
            yield break;
        }
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
