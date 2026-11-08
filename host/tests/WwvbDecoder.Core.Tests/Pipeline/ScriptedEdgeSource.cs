using System.Runtime.CompilerServices;

namespace WwvbDecoder.Core.Tests;

/// <summary>
/// Yields a fixed list of events. A gated source then waits at the gate, honouring the token,
/// and yields the rest once the gate opens, so a test can hold a run in progress.
/// </summary>
internal sealed class ScriptedEdgeSource : IEdgeSource
{
    private readonly EdgeEvent[] _beforeGate;
    private readonly EdgeEvent[]? _afterGate;

    // Asynchronous continuations: OpenGate() must not run the rest of the pipeline inside the
    // test's own call.
    private readonly TaskCompletionSource _atGate = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly TaskCompletionSource _gate = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    private ScriptedEdgeSource(EdgeEvent[] beforeGate, EdgeEvent[]? afterGate)
    {
        _beforeGate = beforeGate;
        _afterGate = afterGate;
    }

    /// <summary>
    /// A source that yields <paramref name="events"/> and ends.
    /// </summary>
    public static ScriptedEdgeSource Of(params EdgeEvent[] events) => new(events, null);

    /// <summary>
    /// A source that yields <paramref name="beforeGate"/>, waits for <see cref="OpenGate"/>,
    /// then yields <paramref name="afterGate"/> and ends.
    /// </summary>
    public static ScriptedEdgeSource Gated(EdgeEvent[] beforeGate, params EdgeEvent[] afterGate) =>
        new(beforeGate, afterGate);

    public string Name => "scripted";

    /// <summary>
    /// How many times <see cref="DisposeAsync"/> was called.
    /// </summary>
    public int DisposeCount { get; private set; }

    /// <summary>
    /// Completes when the enumeration reaches the gate.
    /// </summary>
    public Task AtGate => _atGate.Task;

    /// <summary>
    /// Lets a gated enumeration continue.
    /// </summary>
    public void OpenGate() => _gate.TrySetResult();

    public async IAsyncEnumerable<EdgeEvent> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        foreach (EdgeEvent e in _beforeGate)
        {
            yield return e;
        }

        if (_afterGate is null)
        {
            yield break;
        }

        _atGate.TrySetResult();

        // Cancelling the token throws OperationCanceledException here, which cancels the run.
        await _gate.Task.WaitAsync(ct);

        foreach (EdgeEvent e in _afterGate)
        {
            yield return e;
        }
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}
