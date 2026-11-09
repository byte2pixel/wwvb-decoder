namespace WwvbDecoder.Core.Tests;

internal static class EnumeratorHelpers
{
    public static async Task<EdgeEvent> NextAsync(IAsyncEnumerator<EdgeEvent> e, Task<bool> move)
    {
        bool moved = await move.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        moved.ShouldBeTrue();
        return e.Current;
    }

    public static Task<bool> MoveAsync(IAsyncEnumerator<EdgeEvent> e) => e.MoveNextAsync().AsTask();
}
