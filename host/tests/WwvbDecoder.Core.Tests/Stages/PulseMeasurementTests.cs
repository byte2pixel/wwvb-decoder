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

    [Fact]
    public void Push_FullCarrierGlitchInsidePulse_BridgesIt()
    {
        // Arrange
        var stage = new PulseMeasurement();
        List<Edge> edges = [H(0), L(240_000), H(260_000), L(500_000), H(1_000_000)];

        // Act & Assert
        var pulses = edges.Take(edges.Count - 1).SelectMany(stage.Push).ToList();

        pulses.ShouldBeEmpty();
        pulses = [.. edges.TakeLast(1).SelectMany(stage.Push)];

        pulses.ShouldBe([new Pulse(0, 500_000)]);
        stage.GapsBridged.ShouldBe(1L);
    }

    [Theory]
    [InlineData(49_999, 1)]
    [InlineData(50_000, 0)]
    public void Push_GapAtThreshold_BridgesOnlyBelowIt(ulong micros, int expectedGapsBridged)
    {
        // Arrange
        var stage = new PulseMeasurement();
        List<Edge> edges = [H(0), L(200_000), H(200_000 + micros)];

        // Act
        _ = edges.SelectMany(stage.Push).ToList();

        // Assert
        stage.GapsBridged.ShouldBe(expectedGapsBridged);
    }

    [Fact]
    public void Push_TwoGapsInOnePulse_BridgesBoth()
    {
        // Arrange
        var stage = new PulseMeasurement();
        List<Edge> edges =
        [
            H(0),
            L(100_000),
            H(120_000),
            L(300_000),
            H(320_000),
            L(500_000),
            H(1_000_000),
        ];

        // Act
        var pulses = edges.SelectMany(stage.Push).ToList();

        // Assert
        pulses.ShouldBe([new Pulse(0, 500_000)]);
        stage.GapsBridged.ShouldBe(2);
    }

    [Fact]
    public void Push_ReducedCarrierGlitch_IsDropped()
    {
        // Arrange
        var stage = new PulseMeasurement();
        List<Edge> edges = [H(0), L(500_000), H(600_000)];

        // Act & Assert
        var pulses = edges.SelectMany(stage.Push).ToList();
        pulses.ShouldBe([new Pulse(0, 500_000)]);

        edges = [L(620_000), H(1_000_000)];
        pulses = [.. edges.SelectMany(stage.Push)];
        pulses.ShouldBeEmpty();

        edges = [L(1_200_000), H(2_000_000)];
        pulses = [.. edges.SelectMany(stage.Push)];
        pulses.ShouldBe([new Pulse(1_000_000, 200_000)]);
        stage.GlitchesDropped.ShouldBe(1);
    }

    [Theory]
    [InlineData(49_999, 0, 1)]
    [InlineData(50_000, 1, 0)]
    public void Push_PulseAtThreshold_DropsOnlyBelowIt(
        ulong micros,
        int expectedPulses,
        int expectedGlitchesDropped
    )
    {
        // Arrange
        var stage = new PulseMeasurement();
        List<Edge> edges = [H(0), L(micros), H(1_000_000)];

        // Act
        var pulses = edges.SelectMany(stage.Push).ToList();

        // Assert
        pulses.Count.ShouldBe(expectedPulses);
        stage.GlitchesDropped.ShouldBe(expectedGlitchesDropped);
    }

    [Fact]
    public void Push_CustomMinPulse_MovesBothThresholds()
    {
        // Arrange
        var stage = new PulseMeasurement { MinPulseMicros = 10_000 };
        List<Edge> edges = [H(0), L(20_000), H(40_000)];

        // Act
        var pulses = edges.SelectMany(stage.Push).ToList();

        // Assert
        pulses.ShouldBe([new Pulse(0, 20_000)]);
        stage.GapsBridged.ShouldBe(0);
        stage.GlitchesDropped.ShouldBe(0);
    }

    [Fact]
    public void Push_ShortPulseThenShortGap_BridgesInsteadOfDropping()
    {
        // Arrange
        var stage = new PulseMeasurement();
        List<Edge> edges = [H(0), L(20_000), H(40_000), L(500_000), H(1_000_000)];

        // Act
        var pulses = edges.SelectMany(stage.Push).ToList();

        // Assert
        pulses.ShouldBe([new Pulse(0, 500_000)]);
        stage.GapsBridged.ShouldBe(1);
        stage.GlitchesDropped.ShouldBe(0);
    }

    [Fact]
    public void Push_MissingLeavingEdge_CountsUnpairedThenMeasuresNextPulse()
    {
        // Arrange
        var stage = new PulseMeasurement();
        List<Edge> edges = [H(0), H(1_000_000)];

        // Act & Assert
        var pulses = edges.SelectMany(stage.Push).ToList();
        pulses.ShouldBeEmpty();
        stage.UnpairedEdges.ShouldBe(1);

        edges = [L(1_200_000), H(2_000_000)];
        pulses = [.. edges.SelectMany(stage.Push)];
        pulses.ShouldBe([new Pulse(0, 1_200_000)]);

        edges = [L(2_500_000), H(3_000_000)];
        pulses = [.. edges.SelectMany(stage.Push)];
        pulses.ShouldBe([new Pulse(2_000_000, 500_000)]);

        stage.UnpairedEdges.ShouldBe(1);
    }

    [Fact]
    public void Push_MissingEnteringEdge_CountsUnpairedAndKeepsPendingPulse()
    {
        // Arrange
        var stage = new PulseMeasurement();
        List<Edge> edges = [H(0), L(200_000), L(1_200_000)];

        // Act & Assert
        var pulses = edges.SelectMany(stage.Push).ToList();
        pulses.ShouldBeEmpty();
        stage.UnpairedEdges.ShouldBe(1);

        edges = [H(2_000_000)];
        pulses = [.. edges.SelectMany(stage.Push)];
        pulses.ShouldBe([new Pulse(0, 200_000)]);
        stage.UnpairedEdges.ShouldBe(1);
    }

    private static Edge H(ulong micros) => new(micros, PinLevel.High);

    private static Edge L(ulong micros) => new(micros, PinLevel.Low);
}
