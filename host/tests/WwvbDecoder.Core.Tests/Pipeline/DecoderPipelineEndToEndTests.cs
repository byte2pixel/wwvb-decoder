using Microsoft.Extensions.Time.Testing;

namespace WwvbDecoder.Core.Tests;

public class DecoderPipelineEndToEndTests
{
    private static readonly DateTimeOffset StartUtc = new(2026, 12, 10, 19, 30, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Runs the synthetic source at Speed = 0 through a pipeline until untilMicros of stream time.
    // Frames complete at 61, 121, 181 s, ...
    private static async Task<(
        List<ValidationResult> Validated,
        List<ClockState> Clock,
        List<FrameSyncEvent> Sync
    )> Run(double noise, int seed, ulong untilMicros)
    {
        DecoderPipeline pipeline = new(new FakeTimeProvider(StartUtc));
        List<ValidationResult> validated = [];
        List<ClockState> clock = [];
        List<FrameSyncEvent> sync = [];
        pipeline.FrameValidated += validated.Add;
        pipeline.ClockUpdated += clock.Add;
        pipeline.FrameSyncChanged += sync.Add;
        SyntheticEdgeSource synthetic = new(new FakeTimeProvider(StartUtc))
        {
            StartUtc = StartUtc,
            Speed = 0,
            Noise = noise,
            Seed = seed,
        };

        await pipeline.RunAsync(new BoundedEdgeSource(synthetic, untilMicros), Ct);

        return (validated, clock, sync);
    }

    [Fact]
    public async Task Clean_ThreeMinutes_LocksWithTheEncodedTimes()
    {
        // Arrange & Act: 180 s stops before the third frame completes at 181 s.
        var (validated, clock, sync) = await Run(noise: 0, seed: 0, untilMicros: 180_000_000);

        // Assert
        sync.OfType<FrameSyncEvent.FrameComplete>().Count().ShouldBe(2);
        validated.Count.ShouldBe(2);
        validated.ShouldAllBe(v => v.Accepted);
        clock.Select(c => c.Status).ShouldBe([ClockStatus.Acquiring, ClockStatus.Locked]);
        clock.Select(c => c.FrameUtc).ShouldBe([StartUtc, StartUtc.AddMinutes(1)]);
    }

    [Fact]
    public async Task LowNoise_TenMinutes_RejectsWithReasons()
    {
        // Arrange & Act: measured at 3 accepted and 2 rejected frames.
        var (validated, _, _) = await Run(noise: 0.05, seed: 2, untilMicros: 600_000_000);

        // Assert
        validated.ShouldContain(v => !v.Accepted && !v.Failures.IsEmpty);
    }

    [Fact]
    public async Task HeavyNoise_TenMinutes_LosesSyncWithoutThrowing()
    {
        // Arrange & Act: awaiting the run is the no-exception check.
        var (validated, _, sync) = await Run(noise: 0.5, seed: 0, untilMicros: 600_000_000);

        // Assert: no frame completes at this level.
        sync.ShouldContain(e => e is FrameSyncEvent.SyncLost);
        sync.ShouldNotContain(e => e is FrameSyncEvent.FrameComplete);
        validated.ShouldBeEmpty();
    }
}
