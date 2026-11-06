using Microsoft.Extensions.Time.Testing;

namespace WwvbDecoder.Core.Tests;

public class SyntheticThroughStage3Tests
{
    private static readonly DateTimeOffset StartUtc = new(2026, 11, 4, 12, 30, 0, TimeSpan.Zero);
    private static readonly int[] MarkerSeconds = [0, 9, 19, 29, 39, 49, 59];

    [Fact]
    public async Task ZeroNoise_TwoMinutes_ClassifiesCleanly()
    {
        // Arrange
        var source = new SyntheticEdgeSource(new FakeTimeProvider())
        {
            StartUtc = StartUtc,
            Speed = 0.0,
            Seed = 1,
        };

        // Act
        var symbols = await RunAsync(source, new PulseMeasurement(), edgeCount: 240);

        // Assert
        // 240 edges make 120 pulses, and the last stays pending with no entering edge after it.
        symbols.Count.ShouldBe(119);
        symbols.ShouldAllBe(s => s.Kind != SymbolKind.Error);
        for (int i = 0; i < symbols.Count; i++)
        {
            symbols[i].StartMicros.ShouldBe((ulong)i * 1_000_000UL);

            // Symbol 0 is the source's lead-in marker, so the first frame starts at symbol 1.
            bool expectMarker = i == 0 || MarkerSeconds.Contains((i - 1) % 60);
            (symbols[i].Kind == SymbolKind.Marker).ShouldBe(expectMarker, $"stream second {i}");
        }
    }

    [Fact]
    public async Task Noise_ReachesTheStages_WithoutThrowing()
    {
        // Arrange
        var source = new SyntheticEdgeSource(new FakeTimeProvider())
        {
            StartUtc = StartUtc,
            Speed = 0.0,
            Noise = 0.3,
            Seed = 1,
        };
        var pulses = new PulseMeasurement();

        // Act
        var symbols = await RunAsync(source, pulses, edgeCount: 240);

        // Assert
        pulses.GlitchesDropped.ShouldBeGreaterThan(0);
        pulses.GapsBridged.ShouldBeGreaterThan(0);
        pulses.UnpairedEdges.ShouldBeGreaterThan(0);
        symbols.ShouldNotBeEmpty();
    }

    private static async Task<List<Symbol>> RunAsync(
        SyntheticEdgeSource source,
        PulseMeasurement pulses,
        int edgeCount
    )
    {
        var classifier = new SymbolClassifier();
        List<Symbol> symbols = [];
        int edges = 0;

        await foreach (var evt in source.ReadAsync(TestContext.Current.CancellationToken))
        {
            if (evt is EdgeEvent.Reset)
            {
                pulses.Reset();
                classifier.Reset();
            }
            else if (evt is EdgeEvent.Sample sample)
            {
                symbols.AddRange(pulses.Push(sample.Edge).SelectMany(classifier.Push));
                if (++edges == edgeCount)
                {
                    break;
                }
            }
        }

        return symbols;
    }
}
