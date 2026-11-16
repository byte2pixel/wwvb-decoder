using System.IO;
using Microsoft.Extensions.Time.Testing;
using WwvbDecoder.Host.Services;

namespace WwvbDecoder.Host.Tests.Services;

public sealed class HostEnvironmentTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(
        new DateTimeOffset(2026, 12, 10, 19, 30, 27, TimeSpan.Zero)
    );

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "wwvb-host-tests-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void CreateSynthetic_StartsAtTheCurrentUtcMinute()
    {
        // Arrange
        var environment = new HostEnvironment(_time, _directory);

        // Act
        var source = environment.CreateSynthetic(noise: 0.2).ShouldBeOfType<SyntheticEdgeSource>();

        // Assert
        source.StartUtc.ShouldBe(new DateTimeOffset(2026, 12, 10, 19, 30, 0, TimeSpan.Zero));
        source.Noise.ShouldBe(0.2);
    }

    [Fact]
    public async Task ListCaptures_MapsDurationToMinutes()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "2026-11-09T0434Z-t.edges.txt");
        string[] lines =
        [
            "# capture v1",
            "# recorded 2026-11-09T04:34:28Z",
            "# source synthetic",
            "H 1000000",
            "L 1200000",
            "H 91000000",
        ];
        await File.WriteAllTextAsync(
            path,
            string.Join('\n', lines) + "\n",
            TestContext.Current.CancellationToken
        );
        var environment = new HostEnvironment(_time, _directory);

        // Act
        var entry = environment.ListCaptures().ShouldHaveSingleItem();

        // Assert
        entry.Path.ShouldBe(path);
        entry.Recorded.ShouldBe(new DateTimeOffset(2026, 11, 9, 4, 34, 28, TimeSpan.Zero));
        entry.Minutes.ShouldBe(1.5);
    }

    [Fact]
    public void ListCaptures_WhenTheDirectoryIsMissing_IsEmpty()
    {
        // Arrange
        var environment = new HostEnvironment(_time, _directory);

        // Act & Assert
        environment.ListCaptures().ShouldBeEmpty();
    }

    private static readonly string[] Capture =
    [
        "# capture v1",
        "# recorded 2026-11-09T04:34:28Z",
        "# source synthetic",
        "H 1000000",
        "L 1200000",
        "H 91000000",
    ];

    private static async Task<string> WriteCaptureAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "2026-11-09T0434Z-t.edges.txt");
        await File.WriteAllTextAsync(
            path,
            string.Join('\n', Capture) + "\n",
            TestContext.Current.CancellationToken
        );
        return path;
    }

    [Fact]
    public async Task CreateReplay_ReadsTheCaptureAtTheGivenSpeedAndLoops()
    {
        // Arrange
        string path = await WriteCaptureAsync(_directory);
        var environment = new HostEnvironment(_time, _directory);

        // Act
        var source = environment.CreateReplay(path, speed: 10).ShouldBeOfType<ReplayEdgeSource>();

        // Assert
        source.Info.Recorded.ShouldBe(new DateTimeOffset(2026, 11, 9, 4, 34, 28, TimeSpan.Zero));
        source.Speed.ShouldBe(10);
        source.Loop.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateReplay_PathOutsideTheCapturesDirectory_Throws()
    {
        // Arrange: a real capture in a folder whose name only starts like the captures directory's.
        string elsewhere = _directory + "-elsewhere";
        try
        {
            string path = await WriteCaptureAsync(elsewhere);
            var environment = new HostEnvironment(_time, _directory);

            // Act & Assert
            Should
                .Throw<FileNotFoundException>(() => environment.CreateReplay(path, speed: 1))
                .Message.ShouldBe("Not a capture in the captures directory.");
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    [Fact]
    public void FindCapturesDirectory_WalksUpToAnExistingCapturesFolder()
    {
        // Act: from the test output folder, the walk reaches the repo's captures/
        string found = HostEnvironment.FindCapturesDirectory();

        // Assert
        Path.GetFileName(found).ShouldBe("captures");
        Directory.Exists(found).ShouldBeTrue(found);
    }

    [Fact]
    public void FindCapturesDirectory_CapturesArgument_NamesTheDirectory()
    {
        // Act: the option is found among others, and its directory need not exist yet
        string found = HostEnvironment.FindCapturesDirectory(["--other", "--captures", _directory]);

        // Assert
        found.ShouldBe(_directory);
    }

    [Fact]
    public void FindCapturesDirectory_RelativeCapturesArgument_IsMadeAbsolute()
    {
        // Act
        string found = HostEnvironment.FindCapturesDirectory(["--captures", "my-captures"]);

        // Assert
        found.ShouldBe(Path.Combine(Directory.GetCurrentDirectory(), "my-captures"));
    }

    [Theory]
    [InlineData]
    [InlineData("--verbose")]
    [InlineData("--captures")] // the option with no directory after it
    public void FindCapturesDirectory_NoCapturesArgument_WalksUp(params string[] args)
    {
        // Act
        string found = HostEnvironment.FindCapturesDirectory(args);

        // Assert
        found.ShouldBe(HostEnvironment.FindCapturesDirectory());
    }

    [Fact]
    public void CreateSerial_NamesTheSourceAfterThePort()
    {
        // Arrange
        var environment = new HostEnvironment(_time, _directory);

        // Act: nothing opens the port until the source is read
        var source = environment.CreateSerial("COM5").ShouldBeOfType<Adapters.SerialEdgeSource>();

        // Assert
        source.Name.ShouldBe("serial COM5");
    }
}
