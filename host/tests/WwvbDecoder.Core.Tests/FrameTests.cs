using System.Collections.Immutable;

namespace WwvbDecoder.Core.Tests;

public class FrameTests
{
    private const ulong StartMicros = 1_000_000;
    private const ulong EndMicros = 60_800_000;

    [Fact]
    public void Constructor_With60Symbols_KeepsValues()
    {
        // Arrange
        var symbols = Enumerable.Repeat(SymbolKind.Zero, 60).ToImmutableArray();

        // Act
        var frame = new Frame(StartMicros, EndMicros, symbols);

        // Assert
        frame.StartMicros.ShouldBe(StartMicros);
        frame.EndMicros.ShouldBe(EndMicros);
        frame.Symbols.ShouldBeEquivalentTo(symbols);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(59)]
    [InlineData(61)]
    public void Constructor_WrongSymbolCount_Throws(int symbolCount)
    {
        // Arrange
        var symbols = Enumerable.Repeat(SymbolKind.Zero, symbolCount).ToImmutableArray();

        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => new Frame(StartMicros, EndMicros, symbols));
    }

    [Fact]
    public void Constructor_DefaultArray_Throws()
    {
        // Arrange & Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => new Frame(0, 0, default));
    }
}
