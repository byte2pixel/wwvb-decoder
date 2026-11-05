namespace WwvbDecoder.Core.Tests;

public class PulseMeasurementTests
{
    [Fact]
    public void Options_HaveContractDefaults()
    {
        // Arrange & Act
        var stage = new PulseMeasurement();

        // Assert
        stage.ReducedCarrierLevel.ShouldBe(PinLevel.High);
        stage.MinPulseMicros.ShouldBe(50_000UL);
        stage.GapsBridged.ShouldBe(0L);
        stage.GlitchesDropped.ShouldBe(0L);
        stage.UnpairedEdges.ShouldBe(0L);
    }

    [Fact]
    public void Push_CleanPulse_HoldsItUntilNextEnteringEdge()
    {
        // Arrange
        var stage = new PulseMeasurement();
        List<Edge> edges = [H(12_000_000), L(12_200_412), H(13_000_003)];

        // Act & Assert
        var pulses = stage.Push(edges[0]).ToList();
        pulses.ShouldBeEmpty();

        pulses = [.. stage.Push(edges[1])];
        pulses.ShouldBeEmpty();

        pulses = [.. stage.Push(edges[2])];
        pulses.ShouldBe([new Pulse(12_000_000, 200_412)]);
    }

    [Fact]
    public void Push_ZeroOneMarker_YieldsThreePulses()
    {
        // Arrange
        var stage = new PulseMeasurement();
        List<Edge> edges =
        [
            H(0),
            L(200_000),
            H(1_000_000),
            L(1_500_000),
            H(2_000_000),
            L(2_800_000),
            H(3_000_000),
        ];

        // Act
        var pulses = edges.SelectMany(stage.Push).ToList();

        // Assert
        pulses.ShouldBe([
            new Pulse(0, 200_000),
            new Pulse(1_000_000, 500_000),
            new Pulse(2_000_000, 800_000),
        ]);
    }

    [Fact]
    public void Push_FirstEdgeLeaving_IsIgnored()
    {
        // Arrange
        var stage = new PulseMeasurement();
        List<Edge> edges = [L(100_000), H(1_000_000), L(1_200_000), H(2_000_000)];

        // Act
        var pulses = edges.SelectMany(stage.Push).ToList();

        // Assert
        pulses.ShouldBe([new Pulse(1_000_000, 200_000)]);
        stage.UnpairedEdges.ShouldBe(0L);
    }

    [Fact]
    public void Push_ResultNotEnumerated_StillAdvances()
    {
        // Arrange
        var stage = new PulseMeasurement();

        // Act
        _ = stage.Push(H(0));
        _ = stage.Push(L(200_000));
        var pulses = stage.Push(H(1_000_000));

        // Assert
        pulses.ShouldBe([new Pulse(0, 200_000)]);
    }

    private static Edge H(ulong micros) => new(micros, PinLevel.High);

    private static Edge L(ulong micros) => new(micros, PinLevel.Low);
}
