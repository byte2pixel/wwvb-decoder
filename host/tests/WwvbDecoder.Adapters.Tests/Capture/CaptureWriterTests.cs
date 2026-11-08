using Microsoft.Extensions.Time.Testing;

namespace WwvbDecoder.Adapters.Tests;

public class CaptureWriterTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 3, 10, 27, TimeSpan.Zero);

    private const string Header =
        "# capture v1\n# recorded 2026-11-02T03:10:27Z\n# source serial COM5, receiver #1\n";

    // The writer's own NewLine is CRLF on purpose: the capture must still get "\n" only.
    private readonly StringWriter _out = new() { NewLine = "\r\n" };
    private readonly FakeTimeProvider _time = new(Start);
    private readonly CaptureWriter _writer;

    public CaptureWriterTests()
    {
        _writer = new CaptureWriter(_out, _time);
    }

    private string Text => _out.ToString();

    private string[] WallLines => Text.Split('\n').Where(l => l.StartsWith("# wall ")).ToArray();

    private void WriteHeader() => _writer.WriteHeader(Start, "serial COM5, receiver #1", null);

    // 1
    [Fact]
    public void Constructor_NullWriter_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new CaptureWriter(null!, _time));
    }

    // 2
    [Fact]
    public void Constructor_NullTimeProvider_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new CaptureWriter(_out, null!));
    }

    // 3
    [Fact]
    public void WriteHeader_WritesTheThreeRequiredLinesInOrder()
    {
        // Act
        WriteHeader();

        // Assert
        Text.ShouldBe(Header);
    }

    // 4
    [Theory]
    [InlineData(PinLevel.High, "H")]
    [InlineData(PinLevel.Low, "L")]
    public void WriteHeader_WithReducedCarrierLevel_AddsAFourthLine(PinLevel level, string letter)
    {
        // Act
        _writer.WriteHeader(Start, "serial COM5, receiver #1", level);

        // Assert
        Text.ShouldBe(Header + "# reduced-carrier-level " + letter + "\n");
    }

    // 5
    [Fact]
    public void WriteHeader_ConvertsRecordedToUtc()
    {
        // Arrange
        DateTimeOffset local = new(2026, 11, 2, 5, 10, 27, TimeSpan.FromHours(2));

        // Act
        _writer.WriteHeader(local, "x", null);

        // Assert
        Text.Split('\n')[1].ShouldBe("# recorded 2026-11-02T03:10:27Z");
    }

    // 6
    [Fact]
    public void WriteHeader_DropsFractionalSeconds()
    {
        // Act
        _writer.WriteHeader(Start.AddMilliseconds(789), "x", null);

        // Assert
        Text.Split('\n')[1].ShouldBe("# recorded 2026-11-02T03:10:27Z");
    }

    // 7
    [Fact]
    public void WriteHeader_Twice_ThrowsAndWritesNothingMore()
    {
        // Arrange
        WriteHeader();

        // Act & Assert
        Should.Throw<InvalidOperationException>(WriteHeader);
        Text.ShouldBe(Header);
    }

    // 8
    [Fact]
    public void WriteHeader_NullSource_Throws()
    {
        Should.Throw<ArgumentNullException>(() => _writer.WriteHeader(Start, null!, null));
    }

    // 9
    [Theory]
    [InlineData("two\nlines")]
    [InlineData("stray\rreturn")]
    public void WriteHeader_SourceWithLineBreak_ThrowsAndWritesNothing(string source)
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => _writer.WriteHeader(Start, source, null));
        Text.ShouldBeEmpty();
    }

    // 10
    [Fact]
    public void WriteLine_BeforeHeader_ThrowsAndWritesNothing()
    {
        // Act & Assert
        Should.Throw<InvalidOperationException>(() => _writer.WriteLine("H 12000000"));
        Text.ShouldBeEmpty();
    }

    // 11
    [Theory]
    [InlineData("H 12000000")]
    [InlineData("# dropped 3")]
    [InlineData("X 12 ")]
    [InlineData("H  12")]
    [InlineData("")]
    public void WriteLine_WritesTheLineVerbatimWithALineFeedOnly(string line)
    {
        // Arrange
        WriteHeader();

        // Act
        _writer.WriteLine(line);

        // Assert
        Text.ShouldBe(Header + line + "\n");
    }

    // 12
    [Fact]
    public void WriteLine_NullLine_Throws()
    {
        // Arrange
        WriteHeader();

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => _writer.WriteLine(null!));
    }

    // 13
    [Theory]
    [InlineData("H 1\nL 2")]
    [InlineData("H 1\r")]
    public void WriteLine_LineWithLineBreak_ThrowsAndWritesNothing(string line)
    {
        // Arrange
        WriteHeader();

        // Act & Assert
        Should.Throw<ArgumentException>(() => _writer.WriteLine(line));
        Text.ShouldBe(Header);
    }

    // 14
    [Fact]
    public void WriteLine_NoEdgeYet_WritesNoWallLineHoweverLongItWaits()
    {
        // Arrange
        WriteHeader();
        _writer.WriteLine("# session");
        _writer.WriteLine("# wwvb-edge-logger 0.1.0 pin=2");

        // Act
        _time.Advance(TimeSpan.FromMinutes(5));
        _writer.WriteLine("# dropped 1");

        // Assert
        WallLines.ShouldBeEmpty();
    }

    // 15
    [Fact]
    public void WriteLine_MalformedLinesAreNotEdges()
    {
        // Arrange
        WriteHeader();
        _writer.WriteLine("X 12");
        _writer.WriteLine("H 1.5");
        _writer.WriteLine("H  12"); // two spaces: the codec rejects it, a bare integer parse would not
        _writer.WriteLine("H +1");
        _writer.WriteLine("H 12 ");
        _writer.WriteLine("H");

        // Act
        _time.Advance(TimeSpan.FromMinutes(2));
        _writer.WriteLine("# dropped 1");

        // Assert
        WallLines.ShouldBeEmpty();
    }

    // 16
    [Fact]
    public void WriteLine_BoundaryReached_WritesTheWallLineAfterTheLine()
    {
        // Arrange: 03:10:27, first edge; the first boundary is 03:11:00
        WriteHeader();
        _writer.WriteLine("H 12000000");

        // Act
        _time.Advance(TimeSpan.FromSeconds(33));
        _writer.WriteLine("L 12200412");

        // Assert
        Text.ShouldBe(Header + "H 12000000\nL 12200412\n# wall 2026-11-02T03:11:00Z 12200412\n");
    }

    // 17
    [Fact]
    public void WriteLine_JustBeforeTheBoundary_WritesNoWallLine()
    {
        // Arrange
        WriteHeader();
        _writer.WriteLine("H 12000000");

        // Act: 03:10:59.900
        _time.Advance(TimeSpan.FromSeconds(32.9));
        _writer.WriteLine("L 12200412");

        // Assert
        WallLines.ShouldBeEmpty();
    }

    // 18
    [Fact]
    public void WriteLine_WallLineCarriesNowAndTheLastEdgeMicros()
    {
        // Arrange
        WriteHeader();
        _writer.WriteLine("H 12000000");
        _writer.WriteLine("H 1.5"); // malformed: must not replace the edge micros

        // Act: a # line at 03:11:07 triggers the wall line; its micros are the edge's
        _time.Advance(TimeSpan.FromSeconds(40));
        _writer.WriteLine("# dropped 3");

        // Assert
        Text.Split('\n')[^2].ShouldBe("# wall 2026-11-02T03:11:07Z 12000000");
        Text.Split('\n')[^3].ShouldBe("# dropped 3");
    }

    // 19
    [Fact]
    public void WriteLine_WallLineIsWrittenOnlyOnceAtEachBoundary()
    {
        // Arrange
        WriteHeader();
        _writer.WriteLine("H 12000000");

        // Act
        _time.Advance(TimeSpan.FromSeconds(33)); // 03:11:00 -> wall
        _writer.WriteLine("L 12200412");
        _time.Advance(TimeSpan.FromSeconds(1)); // 03:11:01 -> no wall
        _writer.WriteLine("H 13000003");

        // Assert
        WallLines.ShouldBe(["# wall 2026-11-02T03:11:00Z 12200412"]);
    }

    // 20
    [Fact]
    public void WriteLine_OneEdgePerSecondForThreeMinutes_WritesThreeWallLines()
    {
        // Arrange
        WriteHeader();

        // Act
        for (int i = 0; i < 180; i++)
        {
            _writer.WriteLine($"H {12_000_000 + i * 1_000_000}");
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        // Assert: boundaries at 03:11:00 (i = 33), 03:12:00 (i = 93), 03:13:00 (i = 153)
        WallLines.ShouldBe(
            [
                "# wall 2026-11-02T03:11:00Z 45000000",
                "# wall 2026-11-02T03:12:00Z 105000000",
                "# wall 2026-11-02T03:13:00Z 165000000",
            ]
        );
    }

    // 21
    [Fact]
    public void WriteLine_GapOverSeveralBoundaries_WritesOneWallLineThenResumesAtTheNextBoundaryAfterNow()
    {
        // Arrange
        WriteHeader();
        _writer.WriteLine("H 12000000");

        // Act
        _time.Advance(TimeSpan.FromSeconds(190)); // 03:13:37: three boundaries passed
        _writer.WriteLine("L 12200412");
        _time.Advance(TimeSpan.FromSeconds(20)); // 03:13:57: before the next boundary
        _writer.WriteLine("H 13000003");
        _time.Advance(TimeSpan.FromSeconds(3)); // 03:14:00: next boundary
        _writer.WriteLine("L 13800377");

        // Assert
        WallLines.ShouldBe(
            ["# wall 2026-11-02T03:13:37Z 12200412", "# wall 2026-11-02T03:14:00Z 13800377"]
        );
    }

    // 22
    [Fact]
    public void WriteLine_FirstEdgeExactlyOnABoundary_NextWallIsTheFollowingMinute()
    {
        // Arrange: 03:11:00.000 exactly
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 2, 3, 11, 0, TimeSpan.Zero));
        WriteHeader();

        // Act
        _writer.WriteLine("H 12000000"); // first edge, on the boundary: no wall
        _time.Advance(TimeSpan.FromSeconds(59));
        _writer.WriteLine("L 12200412"); // 03:11:59: no wall
        _time.Advance(TimeSpan.FromSeconds(1));
        _writer.WriteLine("H 13000003"); // 03:12:00: wall

        // Assert
        WallLines.ShouldBe(["# wall 2026-11-02T03:12:00Z 13000003"]);
    }

    // 22b
    [Fact]
    public void WriteLine_WallScheduleStartsAtTheFirstEdgeNotTheFirstLine()
    {
        // Arrange: # lines at 03:10:27, then the first edge at 03:11:07, past the 03:11:00 boundary
        WriteHeader();
        _writer.WriteLine("# session");
        _writer.WriteLine("# wwvb-edge-logger 0.1.0 pin=2");
        _time.Advance(TimeSpan.FromSeconds(40));

        // Act
        _writer.WriteLine("H 12000000"); // first edge: schedule starts, next wall is 03:12:00
        _time.Advance(TimeSpan.FromSeconds(52));
        _writer.WriteLine("L 12200412"); // 03:11:59: no wall
        _time.Advance(TimeSpan.FromSeconds(1));
        _writer.WriteLine("H 13000003"); // 03:12:00: wall

        // Assert
        WallLines.ShouldBe(["# wall 2026-11-02T03:12:00Z 13000003"]);
    }

    // 23
    [Fact]
    public void WriteLine_WallClockIsTakenInUtc()
    {
        // Arrange: a time provider whose local zone is not UTC; the wall line must still say Z
        FakeTimeProvider time = new(Start);
        time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("plus2", TimeSpan.FromHours(2), "p2", "p2"));
        CaptureWriter writer = new(_out, time);
        writer.WriteHeader(Start, "x", null);
        writer.WriteLine("H 12000000");

        // Act
        time.Advance(TimeSpan.FromSeconds(33));
        writer.WriteLine("L 12200412");

        // Assert
        WallLines.ShouldBe(["# wall 2026-11-02T03:11:00Z 12200412"]);
    }

    // 24
    [Fact]
    public void WritesTheCaptureFormatExampleVerbatim()
    {
        // Arrange: docs/design/capture-format.md, "Example"
        const string expected = """
            # capture v1
            # recorded 2026-11-02T03:10:27Z
            # source serial COM5, WVB-0860N-03A receiver #1, home west window on 5 m extension
            # reduced-carrier-level H
            # session
            # wwvb-edge-logger 0.1.0 pin=2
            H 12000000
            L 12200412
            H 13000003
            L 13800377
            H 14000011
            L 14500398
            # wall 2026-11-02T03:11:00Z 14500398
            H 15000006
            L 15200401
            H 16000002
            L 16800390

            """;
        _writer.WriteHeader(
            Start,
            "serial COM5, WVB-0860N-03A receiver #1, home west window on 5 m extension",
            PinLevel.High
        );

        // Act
        _writer.WriteLine("# session");
        _writer.WriteLine("# wwvb-edge-logger 0.1.0 pin=2");
        _writer.WriteLine("H 12000000");
        _writer.WriteLine("L 12200412");
        _writer.WriteLine("H 13000003");
        _writer.WriteLine("L 13800377");
        _writer.WriteLine("H 14000011");
        _time.Advance(TimeSpan.FromSeconds(33)); // 03:11:00
        _writer.WriteLine("L 14500398");
        _time.Advance(TimeSpan.FromSeconds(1));
        _writer.WriteLine("H 15000006");
        _writer.WriteLine("L 15200401");
        _writer.WriteLine("H 16000002");
        _writer.WriteLine("L 16800390");

        // Assert
        Text.ShouldBe(expected.ReplaceLineEndings("\n"));
    }
}
