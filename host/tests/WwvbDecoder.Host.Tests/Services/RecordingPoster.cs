using WwvbDecoder.Host.Bridge;

namespace WwvbDecoder.Host.Tests.Services;

internal sealed class RecordingPoster : IMessagePoster
{
    private readonly Lock _lock = new();
    private readonly List<(string Type, object? Payload)> _posted = [];

    public IReadOnlyList<(string Type, object? Payload)> Posted
    {
        get
        {
            lock (_lock)
            {
                return [.. _posted];
            }
        }
    }

    public void Post<TPayload>(string type, TPayload payload)
    {
        lock (_lock)
        {
            _posted.Add((type, payload));
        }
    }

    public async Task<(string Type, object? Payload)> WaitForAsync(
        Func<(string Type, object? Payload), bool> match
    )
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var hit = Posted.FirstOrDefault(match);
            if (hit.Type is not null)
            {
                return hit;
            }
            await Task.Delay(10, timeout.Token);
        }
    }
}
