using System.Text;
using Microsoft.Extensions.Time.Testing;

namespace WwvbDecoder.Adapters.Tests;

public sealed class CaptureFileTests : IDisposable
{
    // A fresh directory per test, deleted afterward, so tests never see each other's files.
    private readonly string _dir = Directory
        .CreateDirectory(
            Path.Combine(Path.GetTempPath(), "wwvb-tests", Guid.NewGuid().ToString("N"))
        )
        .FullName;

    private string PathTo(string name) => Path.Combine(_dir, name);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // ---- FileName ----

    // 1
    [Fact]
    public void FileName_UtcMinuteAndLabel()
    {
        // Arrange
        DateTimeOffset recorded = new(2026, 11, 2, 3, 10, 27, TimeSpan.Zero);

        // Act
        string name = CaptureFile.FileName(recorded, "home-west-window");

        // Assert
        name.ShouldBe("2026-11-02T0310Z-home-west-window.edges.txt");
    }

    // 2
    [Fact]
    public void FileName_ConvertsAnOffsetTimeToUtc()
    {
        // Arrange
        DateTimeOffset recorded = new(2026, 11, 2, 5, 10, 27, TimeSpan.FromHours(2));

        // Act
        string name = CaptureFile.FileName(recorded, "desk");

        // Assert
        name.ShouldBe("2026-11-02T0310Z-desk.edges.txt");
    }

    // 3
    [Fact]
    public void FileName_PadsSingleDigitFields()
    {
        // Arrange
        DateTimeOffset recorded = new(2026, 1, 5, 7, 8, 59, TimeSpan.Zero);

        // Act
        string name = CaptureFile.FileName(recorded, "a1");

        // Assert
        name.ShouldBe("2026-01-05T0708Z-a1.edges.txt");
    }

    // 4
    [Theory]
    [InlineData("")]
    [InlineData("Home")]
    [InlineData("west window")]
    [InlineData("west_window")]
    [InlineData("desk.rx2")]
    [InlineData("rx#2")]
    [InlineData("désk")]
    public void FileName_InvalidLabel_Throws(string label)
    {
        // Arrange
        DateTimeOffset recorded = new(2026, 11, 2, 3, 10, 27, TimeSpan.Zero);

        // Act & Assert
        Should.Throw<ArgumentException>(() => CaptureFile.FileName(recorded, label));
    }

    // 5
    [Fact]
    public void FileName_NullLabel_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            CaptureFile.FileName(DateTimeOffset.UnixEpoch, null!)
        );
    }

    // ---- Open ----

    // 6
    [Fact]
    public void Open_NullPath_Throws()
    {
        Should.Throw<ArgumentNullException>(() => CaptureFile.Open(null!));
    }

    // 7
    [Fact]
    public void Open_MissingFile_Throws()
    {
        Should.Throw<FileNotFoundException>(() => CaptureFile.Open(PathTo("missing.edges.txt")));
    }

    // 8
    [Theory]
    [InlineData("", new string[0])]
    [InlineData("a\nb\n", new[] { "a", "b" })]
    [InlineData("a\nb", new[] { "a", "b" })]
    [InlineData("a\n\nb\n", new[] { "a", "", "b" })]
    [InlineData("\n\n", new[] { "", "" })]
    [InlineData("H 1\r\nL 2\r\n", new[] { "H 1\r", "L 2\r" })]
    [InlineData(" H 1 \n", new[] { " H 1 " })]
    public void Open_SplitsOnLineFeedOnlyAndDropsTheFinalEmptyLine(
        string content,
        string[] expected
    )
    {
        // Arrange
        string path = PathTo("in.edges.txt");
        File.WriteAllText(path, content);

        // Act
        IReadOnlyList<string> lines = CaptureFile.Open(path);

        // Assert
        lines.ShouldBe(expected);
    }

    // 9
    [Fact]
    public void Open_StripsAUtf8ByteOrderMark()
    {
        // Arrange: an editor may have saved the hand-made header with a BOM
        string path = PathTo("bom.edges.txt");
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. "# capture v1\nH 1\n"u8]);

        // Act
        IReadOnlyList<string> lines = CaptureFile.Open(path);

        // Assert
        lines.ShouldBe(["# capture v1", "H 1"]);
    }

    // ---- Create ----

    // 10
    [Fact]
    public void Create_NullPath_Throws()
    {
        Should.Throw<ArgumentNullException>(() => CaptureFile.Create(null!));
    }

    // 11
    [Fact]
    public void Create_WritesUtf8WithoutBomAndLineFeedLineEndings()
    {
        // Arrange
        string path = PathTo("out.edges.txt");

        // Act
        using (TextWriter writer = CaptureFile.Create(path))
        {
            writer.WriteLine("# capture v1");
            writer.WriteLine("# source désk");
        }

        // Assert: exact bytes, so no BOM and no CR can hide
        File.ReadAllBytes(path).ShouldBe(Encoding.UTF8.GetBytes("# capture v1\n# source désk\n"));
    }

    // 12
    [Fact]
    public void Create_ExistingFile_ThrowsAndLeavesItUntouched()
    {
        // Arrange
        string path = PathTo("keep.edges.txt");
        File.WriteAllText(path, "keep me\n");

        // Act & Assert
        Should.Throw<IOException>(() => CaptureFile.Create(path));
        File.ReadAllText(path).ShouldBe("keep me\n");
    }

    // 13
    [Fact]
    public void Create_MissingDirectory_Throws()
    {
        Should.Throw<DirectoryNotFoundException>(() =>
            CaptureFile.Create(PathTo(Path.Combine("no-such-dir", "x.edges.txt")))
        );
    }

    // ---- Round trip ----

    // 14
    [Fact]
    public void CreateWriteOpen_RoundTripsTheCaptureFormatExample()
    {
        // Arrange: docs/design/capture-format.md, "Example", written through CaptureWriter
        DateTimeOffset start = new(2026, 11, 2, 3, 10, 27, TimeSpan.Zero);
        FakeTimeProvider time = new(start);
        string path = PathTo(CaptureFile.FileName(start, "home-west-window"));

        // Act
        using (TextWriter writer = CaptureFile.Create(path))
        {
            CaptureWriter capture = new(writer, time);
            capture.WriteHeader(
                start,
                "serial COM5, WVB-0860N-03A receiver #1, home west window on 5 m extension",
                PinLevel.High
            );
            foreach (
                string line in new[]
                {
                    "# session",
                    "# wwvb-edge-logger 0.1.0 pin=2",
                    "H 12000000",
                    "L 12200412",
                    "H 13000003",
                    "L 13800377",
                    "H 14000011",
                }
            )
            {
                capture.WriteLine(line);
            }

            time.Advance(TimeSpan.FromSeconds(33));
            capture.WriteLine("L 14500398");
            time.Advance(TimeSpan.FromSeconds(1));
            foreach (
                string line in new[] { "H 15000006", "L 15200401", "H 16000002", "L 16800390" }
            )
            {
                capture.WriteLine(line);
            }
        }

        IReadOnlyList<string> lines = CaptureFile.Open(path);

        // Assert
        Path.GetFileName(path).ShouldBe("2026-11-02T0310Z-home-west-window.edges.txt");
        lines.ShouldBe([
            "# capture v1",
            "# recorded 2026-11-02T03:10:27Z",
            "# source serial COM5, WVB-0860N-03A receiver #1, home west window on 5 m extension",
            "# reduced-carrier-level H",
            "# session",
            "# wwvb-edge-logger 0.1.0 pin=2",
            "H 12000000",
            "L 12200412",
            "H 13000003",
            "L 13800377",
            "H 14000011",
            "L 14500398",
            "# wall 2026-11-02T03:11:00Z 14500398",
            "H 15000006",
            "L 15200401",
            "H 16000002",
            "L 16800390",
        ]);
    }
}
