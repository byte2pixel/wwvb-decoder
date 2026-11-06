namespace WwvbDecoder.Core.Tests;

public class SymbolClassifierTests
{
    [Fact]
    public void Options_HaveContractDefaults()
    {
        // Arrange & Act
        var stage = new SymbolClassifier();

        // Assert
        stage.MinValidMicros.ShouldBe(100_000UL);
        stage.ZeroOneThresholdMicros.ShouldBe(350_000UL);
        stage.OneMarkerThresholdMicros.ShouldBe(650_000UL);
        stage.MaxValidMicros.ShouldBe(900_000UL);
    }

    [Theory]
    [InlineData(99_999, SymbolKind.Error)]
    [InlineData(100_000, SymbolKind.Zero)]
    [InlineData(349_999, SymbolKind.Zero)]
    [InlineData(350_000, SymbolKind.One)]
    [InlineData(649_999, SymbolKind.One)]
    [InlineData(650_000, SymbolKind.Marker)]
    [InlineData(900_000, SymbolKind.Marker)]
    [InlineData(900_001, SymbolKind.Error)]
    public void Push_Duration_ClassifiesPerContract(ulong duration, SymbolKind kind)
    {
        // Arrange
        var stage = new SymbolClassifier();

        // Act
        var symbols = stage.Push(new Pulse(5_000, duration));

        // Assert
        symbols.ShouldBe([new Symbol(5_000, duration, kind)]);
    }

    [Fact]
    public void Push_CustomThresholds_AreUsed()
    {
        // Arrange
        var stage = new SymbolClassifier
        {
            MinValidMicros = 10,
            ZeroOneThresholdMicros = 20,
            OneMarkerThresholdMicros = 30,
            MaxValidMicros = 40,
        };

        // Act & Assert
        stage.Push(new Pulse(0, 9)).ShouldBe([new Symbol(0, 9, SymbolKind.Error)]);
        stage.Push(new Pulse(0, 10)).ShouldBe([new Symbol(0, 10, SymbolKind.Zero)]);
        stage.Push(new Pulse(0, 20)).ShouldBe([new Symbol(0, 20, SymbolKind.One)]);
        stage.Push(new Pulse(0, 30)).ShouldBe([new Symbol(0, 30, SymbolKind.Marker)]);
        stage.Push(new Pulse(0, 41)).ShouldBe([new Symbol(0, 41, SymbolKind.Error)]);
    }
}
