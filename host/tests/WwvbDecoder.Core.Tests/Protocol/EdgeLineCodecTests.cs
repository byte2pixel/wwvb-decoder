namespace WwvbDecoder.Core.Tests;

public class EdgeLineCodecTests
{
    [Theory]
    [InlineData("H 1234567", PinLevel.High, 1234567UL)]
    [InlineData("L 0", PinLevel.Low, 0UL)]
    [InlineData("H 007", PinLevel.High, 7UL)]
    [InlineData("L 18446744073709551615", PinLevel.Low, ulong.MaxValue)]
    public void TryParse_EdgeLine_ReturnsSample(
        string line,
        PinLevel expectedLevel,
        ulong expectedMicros
    )
    {
        // Arrange & Act
        bool result = EdgeLineCodec.TryParse(line, out EdgeEvent? edgeLine);

        // Assert
        result.ShouldBeTrue();
        edgeLine
            .ShouldNotBeNull()
            .ShouldBeOfType<EdgeEvent.Sample>()
            .Edge.ShouldBe(new Edge(expectedMicros, expectedLevel));
    }

    [Fact]
    public void TryParse_SessionLine_ReturnsReset()
    {
        // Arrange & Act
        bool result = EdgeLineCodec.TryParse("# session", out EdgeEvent? edgeLine);

        // Assert
        result.ShouldBeTrue();
        edgeLine.ShouldNotBeNull().ShouldBeOfType<EdgeEvent.Reset>().Reason.ShouldBe("session");
    }

    [Theory]
    [InlineData("# some info")]
    [InlineData("# dropped 3")]
    [InlineData("# session now")]
    [InlineData("#session")]
    [InlineData("#")]
    public void TryParse_OtherHashLine_ReturnsInfoWithWholeLine(string line)
    {
        // Arrange & Act
        bool result = EdgeLineCodec.TryParse(line, out EdgeEvent? edgeLine);

        // Assert
        result.ShouldBeTrue();
        edgeLine.ShouldNotBeNull().ShouldBeOfType<EdgeEvent.Info>().Line.ShouldBe(line);
    }

    [Theory]
    // Shape
    [InlineData("")]
    [InlineData("X 12")]
    [InlineData("H")]
    [InlineData("H ")]
    [InlineData("H12")]
    [InlineData("h 12")]
    // Spaces
    [InlineData("H  12")]
    [InlineData("H 12 ")]
    [InlineData(" H 12")]
    // Number Format
    [InlineData("H -1")]
    [InlineData("H +1")]
    [InlineData("H 1.5")]
    // Carriage return
    [InlineData("H 12\r")]
    [InlineData("# session\r")]
    [InlineData("# a\rb")]
    // Range
    [InlineData("H 18446744073709551616")] // ulong.MaxValue + 1
    [InlineData("L 000000000000000000001")] // a value small but too long (over 20 digits)
    // Sneaky characters
    [InlineData("H 12\0")] // null character
    [InlineData("H ١٢")] // Arabic-Indic digit
    public void TryParse_Malformed_ReturnsFalse(string line)
    {
        // Arrange & Act
        bool result = EdgeLineCodec.TryParse(line, out EdgeEvent? edgeLine);

        // Assert
        result.ShouldBeFalse();
        edgeLine.ShouldBeNull();
    }

    [Theory]
    [InlineData(64)]
    [InlineData(100)]
    public void TryParse_LineOver63Characters_ReturnsFalse(int length)
    {
        // Arrange
        string line = new('#', length);

        // Act
        bool result = EdgeLineCodec.TryParse(line, out EdgeEvent? edgeLine);

        // Assert
        result.ShouldBeFalse();
        edgeLine.ShouldBeNull();
    }

    [Fact]
    public void TryParse_63CharacterHashLine_ReturnsInfo()
    {
        // Arrange
        string line = new('#', 63);

        // Act
        bool result = EdgeLineCodec.TryParse(line, out EdgeEvent? edgeLine);

        // Assert
        result.ShouldBeTrue();
        edgeLine.ShouldNotBeNull().ShouldBeOfType<EdgeEvent.Info>().Line.ShouldBe(line);
    }

    [Theory]
    [InlineData(PinLevel.High, 13, "H 13")]
    [InlineData(PinLevel.Low, 0, "L 0")]
    public void Format_WritesLevelSpaceMicros(PinLevel level, ulong micros, string expected)
    {
        // Arrange
        Edge edge = new(micros, level);

        // Act
        string result = EdgeLineCodec.Format(edge);

        // Assert
        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData(PinLevel.Low, 0)]
    [InlineData(PinLevel.High, 123456789)]
    [InlineData(PinLevel.High, ulong.MaxValue)]
    public void Format_RoundTripsThroughTryParse(PinLevel level, ulong micros)
    {
        // Arrange
        Edge edge = new(micros, level);
        string line = EdgeLineCodec.Format(edge);

        // Act
        bool result = EdgeLineCodec.TryParse(line, out EdgeEvent? edgeLine);

        // Assert
        result.ShouldBeTrue();
        edgeLine.ShouldNotBeNull().ShouldBeOfType<EdgeEvent.Sample>().Edge.ShouldBe(edge);
    }
}
