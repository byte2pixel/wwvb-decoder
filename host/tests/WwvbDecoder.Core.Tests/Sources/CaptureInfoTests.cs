namespace WwvbDecoder.Core.Tests;

public class CaptureInfoTests
{
    [Fact]
    public void Parse_MinimalHeader_ReadsRecordedAndSource()
    {
        // Arrange
        string[] lines =
        [
            "# capture v1",
            "# recorded 2026-11-02T03:10:27Z",
            "# source serial COM5, receiver #1",
        ];

        // Act
        CaptureInfo info = CaptureInfo.Parse(lines);

        // Assert
        info.Recorded.ShouldBe(new DateTimeOffset(2026, 11, 02, 03, 10, 27, TimeSpan.Zero));
        info.Source.ShouldBe("serial COM5, receiver #1");
        info.ReducedCarrierLevel.ShouldBeNull();
        info.WallSamples.ShouldBeEmpty();
    }

    [Theory]
    [InlineData]
    [InlineData("# capture v2", "# recorded 2026-11-02T03:10:27Z", "# source x")]
    [InlineData("H 12000000", "# capture v1", "# recorded 2026-11-02T03:10:27Z", "# source x")]
    [InlineData("# recorded 2026-11-02T03:10:27Z", "# source x", "# capture v1")]
    public void Parse_FirstLineNotCaptureV1_Throws(params string[] lines)
    {
        // Act
        Action act = () => CaptureInfo.Parse(lines);

        // Assert
        act.ShouldThrow<FormatException>().Message.ShouldContain("first line");
    }

    [Theory]
    [InlineData("recorded", "# capture v1", "# source x")]
    [InlineData("source", "# capture v1", "# recorded 2026-11-02T03:10:27Z")]
    [InlineData("recorded", "# capture v1")]
    public void Parse_MissingRequiredLine_Throws(string missing, params string[] lines)
    {
        // Act
        Action act = () => CaptureInfo.Parse(lines);

        // Assert
        act.ShouldThrow<FormatException>().Message.ShouldContain($"missing a '# {missing}");
    }

    [Theory]
    [InlineData("# recorded yesterday")]
    [InlineData("# recorded 2026-11-02 03:10:27")]
    [InlineData("# recorded 2026-11-02T03:10:27")]
    [InlineData("# recorded ")]
    public void Parse_UnparseableRecorded_Throws(string recordedLine)
    {
        // Arrange
        string[] lines = ["# capture v1", recordedLine, "# source x"];

        // Act
        Action act = () => CaptureInfo.Parse(lines);

        // Assert
        act.ShouldThrow<FormatException>();
    }

    [Theory]
    [InlineData("H", PinLevel.High)]
    [InlineData("L", PinLevel.Low)]
    public void Parse_ReducedCarrierLevel_ReadsPinLevel(string letter, PinLevel expected)
    {
        // Arrange
        string[] lines =
        [
            "# capture v1",
            "# recorded 2026-11-02T03:10:27Z",
            "# source x",
            $"# reduced-carrier-level {letter}",
        ];

        // Act
        CaptureInfo info = CaptureInfo.Parse(lines);

        // Assert
        info.ReducedCarrierLevel.ShouldBe(expected);
    }

    [Theory]
    [InlineData("X")]
    [InlineData("h")]
    [InlineData("High")]
    [InlineData("")]
    public void Parse_ReducedCarrierLevelInvalid_Throws(string value)
    {
        // Arrange
        string[] lines =
        [
            "# capture v1",
            "# recorded 2026-11-02T03:10:27Z",
            "# source x",
            $"# reduced-carrier-level {value}",
        ];

        // Act
        Action act = () => CaptureInfo.Parse(lines);

        // Assert
        act.ShouldThrow<FormatException>();
    }

    [Fact]
    public void Parse_WallLines_CollectedInFileOrder()
    {
        // Arrange
        string[] lines =
        [
            "# capture v1",
            "# recorded 2026-11-08T23:00:15Z",
            "# source serial COM3, label desk",
            "# wall 2026-11-08T23:01:00Z 452260588100",
            "# wall 2026-11-08T23:02:00Z 452320594681",
        ];

        // Act
        CaptureInfo info = CaptureInfo.Parse(lines);

        // Assert
        info.WallSamples.ShouldBe([
            new WallSample(
                new DateTimeOffset(2026, 11, 08, 23, 01, 00, TimeSpan.Zero),
                452260588100UL
            ),
            new WallSample(
                new DateTimeOffset(2026, 11, 08, 23, 02, 00, TimeSpan.Zero),
                452320594681UL
            ),
        ]);
    }

    [Theory]
    [InlineData("# wall yesterday 452260588100")]
    [InlineData("# wall 2026-11-08T23:01:00Z")]
    [InlineData("# wall 2026-11-08T23:01:00Z abc")]
    [InlineData("# wall 2026-11-08T23:01:00Z -5")]
    [InlineData("# wall 2026-11-08T23:01:00Z 1 extra")]
    [InlineData("# wall ")]
    public void Parse_MalformedWallLine_SkippedWithoutThrowing(string badLine)
    {
        // Arrange
        string[] lines =
        [
            "# capture v1",
            "# recorded 2026-11-08T23:00:15Z",
            "# source x",
            badLine,
            "# wall 2026-11-08T23:02:00Z 452320594681",
        ];

        // Act
        CaptureInfo info = CaptureInfo.Parse(lines);

        // Assert
        info.WallSamples.ShouldBe([
            new WallSample(
                new DateTimeOffset(2026, 11, 08, 23, 02, 00, TimeSpan.Zero),
                452320594681UL
            ),
        ]);
    }

    [Fact]
    public void Parse_RealCaptureHead_IgnoresBodyLines()
    {
        // Arrange
        string[] lines =
        [
            "# capture v1",
            "# recorded 2026-11-08T23:00:15Z",
            "# source serial COM3, label desk",
            "# session",
            "# wwvb-edge-logger 0.1.0 pin=2",
            "H 452215787289",
            "L 452216000765",
            "H 452216789159",
            "garbage line",
            "",
            "# wall 2026-11-08T23:01:00Z 452260588100",
            "L 452260800000",
        ];

        // Act
        CaptureInfo info = CaptureInfo.Parse(lines);

        // Assert
        info.Recorded.ShouldBe(new DateTimeOffset(2026, 11, 08, 23, 00, 15, TimeSpan.Zero));
        info.Source.ShouldBe("serial COM3, label desk");
        info.ReducedCarrierLevel.ShouldBeNull();
        info.WallSamples.Length.ShouldBe(1);
        info.WallSamples[0].Micros.ShouldBe(452260588100UL);
    }

    [Fact]
    public void Parse_ExtraHeaderLinesAndAnyOrder_StillFindsRequiredLines()
    {
        // Arrange
        string[] lines =
        [
            "# capture v1",
            "# firmware wwvb-edge-logger 0.1.0",
            "# source terminal log (PuTTY), header added by hand",
            "# reduced-carrier-level H",
            "# recorded 2026-11-02T03:10:27Z",
            "# recorded 2030-01-01T00:00:00Z",
            "# source second one is ignored",
        ];

        // Act
        CaptureInfo info = CaptureInfo.Parse(lines);

        // Assert
        info.Recorded.ShouldBe(new DateTimeOffset(2026, 11, 02, 03, 10, 27, TimeSpan.Zero));
        info.Source.ShouldBe("terminal log (PuTTY), header added by hand");
        info.ReducedCarrierLevel.ShouldBe(PinLevel.High);
    }
}
