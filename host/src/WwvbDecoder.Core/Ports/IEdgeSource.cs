namespace WwvbDecoder.Core;

public interface IEdgeSource : IAsyncDisposable
{
    string Name { get; }
    IAsyncEnumerable<EdgeEvent> ReadAsync(CancellationToken ct);
}
