namespace WwvbDecoder.Adapters.Tests;

public sealed class CaptureCatalogTests : IDisposable
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    // A fresh directory per test, deleted afterward, so tests never see each other's files.
    private readonly string _dir = Directory
        .CreateDirectory(
            Path.Combine(Path.GetTempPath(), "wwvb-tests", Guid.NewGuid().ToString("N"))
        )
        .FullName;

    private string PathTo(string name) => Path.Combine(_dir, name);

    private async Task Write(string path, params string[] lines)
    {
        await File.WriteAllTextAsync(path, string.Join('\n', lines) + "\n", Ct);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private readonly string[] _goodLines =
    [
        "# capture v1",
        "# recorded 2026-11-09T04:34:28Z",
        "# source serial COM3, label t",
        "H 1000000",
        "L 1200000",
        "H 61000000",
    ];

    [Fact]
    public void List_EmptyDirectory_ReturnsEmpty()
    {
        // Act
        var list = CaptureCatalog.List(_dir);

        // Assert
        list.ShouldBeEmpty();
    }

    [Fact]
    public async Task List_OneCapture_PathRecordedAndDuration()
    {
        // Arrange
        string path = PathTo("list-one.edges.txt");
        await Write(path, _goodLines);

        // Act
        var list = CaptureCatalog.List(_dir);

        // Assert
        var entry = list.ShouldHaveSingleItem();
        entry.Path.ShouldBe(path);
        entry.Recorded.ShouldBe(new DateTimeOffset(2026, 11, 9, 4, 34, 28, TimeSpan.Zero));
        entry.Duration.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task List_IgnoresBodyLinesThatAreNotEdges()
    {
        // Arrange
        string path = PathTo("ignores.edges.txt");
        await Write(
            path,
            "# capture v1",
            "# recorded 2026-11-09T04:34:28Z",
            "# source serial COM3, label t",
            "# session",
            "# wwvb-edge-logger 0.1.0 pin=2",
            "H not-an-edge",
            "H 1000000",
            "L 1200000",
            "# wall 2026-11-09T04:35:00Z 1200000",
            "H 61000000",
            "# wall 2026-11-09T04:36:00Z 99000000",
            "L 12x",
            "# dropped 1"
        );

        // Act
        var list = CaptureCatalog.List(_dir);

        // Assert
        list.ShouldHaveSingleItem().Duration.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Theory]
    [InlineData("H 1000000")]
    [InlineData("L 1200000")]
    [InlineData]
    public async Task List_FewerThanTwoEdges_DurationZero(params string[] edges)
    {
        // Arrange
        string path = PathTo("fewer-than-two.edges.txt");

        List<string> lines =
        [
            "# capture v1",
            "# recorded 2026-11-09T04:34:28Z",
            "# source serial COM3, label t",
            .. edges,
        ];

        await Write(path, [.. lines]);

        // Act
        var list = CaptureCatalog.List(_dir);

        // Assert
        list.ShouldHaveSingleItem().Duration.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task List_NewestRecordedFirst()
    {
        // Arrange
        string fileA = PathTo("a.edges.txt");
        await Write(fileA, _goodLines);

        string fileZ = PathTo("z.edges.txt");
        await Write(
            fileZ,
            "# capture v1",
            "# recorded 2026-11-09T04:35:28Z",
            "# source serial COM3, label t",
            "H 1000000",
            "L 1200000",
            "H 61000000"
        );

        // Act
        var list = CaptureCatalog.List(_dir);

        // Assert
        list.ShouldBe([
            new CaptureEntry(
                fileZ,
                new DateTimeOffset(2026, 11, 9, 4, 35, 28, TimeSpan.Zero),
                TimeSpan.FromSeconds(60)
            ),
            new CaptureEntry(
                fileA,
                new DateTimeOffset(2026, 11, 9, 4, 34, 28, TimeSpan.Zero),
                TimeSpan.FromSeconds(60)
            ),
        ]);
    }

    [Fact]
    public async Task List_UnparseableHeader_LeftOut()
    {
        // Arrange
        string goodHeader = PathTo("parseable-header.edges.txt");
        await Write(goodHeader, _goodLines);

        string badHeader1 = PathTo("unparseable-recorded.edges.txt");
        await Write(
            badHeader1,
            "# capture v1",
            "# source serial COM3, label t",
            "H 1000000",
            "L 1200000",
            "H 61000000"
        );
        string badHeader2 = PathTo("unparseable-capturev1.edges.txt");
        await Write(
            badHeader1,
            "# recorded not-a-date",
            "# source serial COM3, label t",
            "H 1000000",
            "L 1200000",
            "H 61000000"
        );

        // Act
        var list = CaptureCatalog.List(_dir);

        // Assert
        list.ShouldHaveSingleItem().Path.ShouldBe(goodHeader);
    }

    [Fact]
    public async Task List_OnlyEdgesTxtFiles()
    {
        string[] files =
        [
            PathTo("edges.edges.txt"),
            PathTo("other.txt"),
            PathTo("x.edges.txtx"), // extra x not .txt
            PathTo("x.edges.txt.bak"), // extra .bak not .txt
        ];

        // Arrange
        foreach (string file in files)
        {
            await Write(file, _goodLines);
        }

        // Act
        var list = CaptureCatalog.List(_dir);

        // Assert
        list.ShouldHaveSingleItem().Path.ShouldBe(PathTo("edges.edges.txt"));
    }
}
