using Microsoft.Extensions.Time.Testing;

namespace WwvbDecoder.Core.Tests;

/// <summary>
/// Replays Fixtures/three-minutes.edges.txt, a real excerpt of the first overnight capture, through
/// the whole pipeline.
/// </summary>
public class ThreeMinuteFixtureTests
{
    // Splits on "\n" only and drops the empty line after the final "\n", like CaptureFile.Open in
    // Adapters, which this project does not reference.
    private static string[] LoadFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "three-minutes.edges.txt");
        string[] lines = File.ReadAllText(path).Split('\n');
        return lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    // The excerpt runs from about second 54 of 04:35Z to second 3 of 04:39Z. Sync happens on the
    // second-0 marker of 04:36, and each frame completes when stage 2 releases its second-59 marker.
    // The clock locks after the second accepted frame (04:37).
    [Fact(Timeout = 5000)]
    public async Task Fixture_AtSpeedZero_DecodesThreeFramesAndLocks()
    {
        // Arrange: the fake clock never advances, so any wait at speed 0 would hit the timeout.
        FakeTimeProvider timeProvider = new(
            new DateTimeOffset(2026, 11, 9, 4, 39, 0, TimeSpan.Zero)
        );
        ReplayEdgeSource source = new(LoadFixture(), timeProvider) { Speed = 0 };
        DecoderPipeline pipeline = new(timeProvider);

        List<FrameSyncEvent.FrameComplete> completed = [];
        List<ValidationResult> validated = [];
        List<ClockState> clock = [];
        pipeline.FrameSyncChanged += e =>
        {
            if (e is FrameSyncEvent.FrameComplete frame)
            {
                completed.Add(frame);
            }
        };
        pipeline.FrameValidated += validated.Add;
        pipeline.ClockUpdated += clock.Add;

        // Act: without Loop, the run ends when the lines run out.
        await pipeline.RunAsync(source, TestContext.Current.CancellationToken);

        // Assert
        source.MalformedLines.ShouldBe(0);
        completed.Count.ShouldBe(3);
        validated.ShouldAllBe(v => v.Accepted);
        validated.Select(v => (v.Frame.Hour, v.Frame.Minute)).ShouldBe([(4, 36), (4, 37), (4, 38)]);
        clock[^1].Status.ShouldBe(ClockStatus.Locked);
    }
}
