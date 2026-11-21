using System.IO;
using WwvbDecoder.Host.Bridge;
using WwvbDecoder.Host.Services;

namespace WwvbDecoder.Host.Tests.Services;

/// <summary>
/// What the controller remembers, and the <c>selection</c> it reports: "What the host remembers"
/// and <c>selection</c> in docs/design/webview2-messages.md. The start at launch is not here.
/// </summary>
public class SourceControllerSettingsTests
{
    private readonly FakeSourceEnvironment _environment = new();
    private readonly RecordingPoster _poster = new();
    private readonly FakeSettingsStore _settings = new();
    private readonly DecoderPipeline _pipeline = new(TimeProvider.System);

    // Nothing in it is a default, so a value that was lost shows. Nothing was running, so the
    // start at launch stays out of these tests.
    private static readonly HostSettings Earlier = new(
        Source: SourceKind.Serial,
        Port: "COM7",
        Capture: "earlier.edges.txt",
        Speed: 60,
        Noise: 0.3,
        ReducedCarrierLevel: PinLevel.Low,
        Running: false
    );

    private SourceController CreateController() => new(_pipeline, _environment, _poster, _settings);

    private Selection SelectionPosted() =>
        _poster.Posted.Last(p => p.Type == "selection").Payload.ShouldBeOfType<Selection>();

    [Fact]
    public async Task Constructor_ReadsTheSettingsOnce()
    {
        // Arrange
        await using SourceController controller = CreateController();

        // Act: commands of every kind
        await controller.HandleAsync(new Command.Hello(1));
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));
        await controller.HandleAsync(new Command.SetNoise(Noise: 0.1));
        await controller.HandleAsync(new Command.Stop());
        await controller.HandleAsync(new Command.Hello(1));

        // Assert
        _settings.Loads.ShouldBe(1);
    }

    [Theory]
    [InlineData(PinLevel.Low)]
    [InlineData(PinLevel.High)]
    public async Task Constructor_GivesTheRememberedLevelToStageTwo(PinLevel level)
    {
        // Arrange: stage 2 holds the other level
        _pipeline.PulseMeasurement.ReducedCarrierLevel =
            level == PinLevel.Low ? PinLevel.High : PinLevel.Low;
        _settings.Remembered = HostSettings.Default with { ReducedCarrierLevel = level };

        // Act: no command at all
        await using SourceController controller = CreateController();

        // Assert
        _pipeline.PulseMeasurement.ReducedCarrierLevel.ShouldBe(level);
    }

    [Fact]
    public async Task Hello_NothingRemembered_SelectionIsTheDefaults()
    {
        // Arrange
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert: last in the reply
        _poster.Posted[^1].Type.ShouldBe("selection");
        SelectionPosted()
            .ShouldBe(new Selection(SourceKind.Synthetic, null, null, 1, 0, PinLevel.High));
    }

    [Fact]
    public async Task Hello_SelectionCarriesWhatIsRemembered()
    {
        // Arrange: the capture is in the list, though not first
        _environment.Captures =
        [
            new CaptureEntry("captures/newest.edges.txt", DateTimeOffset.UnixEpoch, 1),
            new CaptureEntry("captures/earlier.edges.txt", DateTimeOffset.UnixEpoch, 1),
        ];
        _settings.Remembered = Earlier;
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert: the capture as the list gives it, a path and not the name
        SelectionPosted()
            .ShouldBe(
                new Selection(
                    SourceKind.Serial,
                    "COM7",
                    "captures/earlier.edges.txt",
                    60,
                    0.3,
                    PinLevel.Low
                )
            );
    }

    [Theory]
    [InlineData(@"C:\demo\captures\earlier.edges.txt")]
    [InlineData("D:/somewhere/else/earlier.edges.txt")]
    public async Task Hello_SelectionFindsTheCaptureByItsFileName(string listedPath)
    {
        // Arrange: the folder was copied, so only the name still fits
        _environment.Captures = [new CaptureEntry(listedPath, DateTimeOffset.UnixEpoch, 1)];
        _settings.Remembered = Earlier;
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert
        SelectionPosted().Capture.ShouldBe(listedPath);
    }

    [Theory]
    [InlineData("captures/another.edges.txt")] // another file
    [InlineData("captures/earlier.edges.txt.bak")] // a longer name that starts the same
    [InlineData("captures/not-earlier.edges.txt")] // a longer name that ends the same
    [InlineData(null)] // no capture at all
    public async Task Hello_SelectionOfACaptureThatIsGone_IsNull(string? listedPath)
    {
        // Arrange
        _environment.Captures = listedPath is null
            ? []
            : [new CaptureEntry(listedPath, DateTimeOffset.UnixEpoch, 1)];
        _settings.Remembered = Earlier;
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert: the rest is still what was remembered
        SelectionPosted()
            .ShouldBe(new Selection(SourceKind.Serial, "COM7", null, 60, 0.3, PinLevel.Low));
    }

    [Fact]
    public async Task Hello_AfterCommands_SelectionFollowsThem()
    {
        // Arrange
        _settings.Remembered = Earlier;
        await using SourceController controller = CreateController();
        await controller.HandleAsync(
            new Command.SelectReplay(Path: "captures/a.edges.txt", Speed: 1)
        );
        await controller.HandleAsync(new Command.SetReplaySpeed(Speed: 10));
        await controller.HandleAsync(new Command.SetReducedCarrierLevel(PinLevel.High));
        await controller.HandleAsync(new Command.Stop());

        // Act: a page that reloads now
        await controller.HandleAsync(new Command.Hello(1));

        // Assert: what was last started, though nothing runs
        SelectionPosted()
            .ShouldBe(
                new Selection(
                    SourceKind.Replay,
                    "COM7",
                    "captures/a.edges.txt",
                    10,
                    0.3,
                    PinLevel.High
                )
            );
    }

    [Fact]
    public async Task Hello_SelectionLevelIsWhatStageTwoUses()
    {
        // Arrange
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.SetReducedCarrierLevel(PinLevel.Low));

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert
        SelectionPosted().ReducedCarrierLevel.ShouldBe(PinLevel.Low);
        _pipeline.PulseMeasurement.ReducedCarrierLevel.ShouldBe(PinLevel.Low);
    }

    [Fact]
    public async Task SelectSerial_RemembersTheSourceAndThePort()
    {
        // Arrange
        _settings.Remembered = Earlier;
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.SelectSerial(Port: "COM5"));

        // Assert: saved once, in the command, and the other values are kept
        _settings
            .Saved.ShouldHaveSingleItem()
            .ShouldBe(Earlier with { Source = SourceKind.Serial, Port = "COM5", Running = true });
    }

    [Theory]
    [InlineData("captures/a.edges.txt")]
    [InlineData(@"C:\demo\captures\a.edges.txt")]
    [InlineData("a.edges.txt")]
    public async Task SelectReplay_RemembersTheFileNameAndTheSpeed(string path)
    {
        // Arrange
        _settings.Remembered = Earlier;
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.SelectReplay(path, Speed: 10));

        // Assert: the name, so the settings still fit when the folder is copied
        _settings
            .Saved.ShouldHaveSingleItem()
            .ShouldBe(
                Earlier with
                {
                    Source = SourceKind.Replay,
                    Capture = "a.edges.txt",
                    Speed = 10,
                    Running = true,
                }
            );
    }

    [Fact]
    public async Task SelectSynthetic_RemembersTheSourceAndTheNoise()
    {
        // Arrange
        _settings.Remembered = Earlier;
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.2));

        // Assert
        _settings
            .Saved.ShouldHaveSingleItem()
            .ShouldBe(Earlier with { Source = SourceKind.Synthetic, Noise = 0.2, Running = true });
    }

    [Fact]
    public async Task SelectSynthetic_NoiseOutOfRange_RemembersNothing()
    {
        // Arrange
        _settings.Remembered = Earlier;
        await using SourceController controller = CreateController();

        // Act: the real source refuses a noise above 1 when it is created
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 2.0));

        // Assert
        _poster.Posted.ShouldHaveSingleItem().Payload.ShouldBeOfType<SourceStatus>();
        _settings.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task SelectReplay_CaptureCannotBeRead_RemembersNothing()
    {
        // Arrange
        _environment.Replay = (_, _) => throw new FileNotFoundException("no such capture");
        _settings.Remembered = Earlier;
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(
            new Command.SelectReplay(Path: "captures/missing.edges.txt", Speed: 1)
        );

        // Assert
        _settings.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task SelectSerial_SourceCannotBeCreated_RemembersNothing()
    {
        // Arrange
        _environment.Serial = _ => throw new ArgumentException("no such port");
        _settings.Remembered = Earlier;
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.SelectSerial(Port: "COM99"));

        // Assert: the answer was an error, not "running"
        _poster
            .Posted.ShouldHaveSingleItem()
            .Payload.ShouldBeOfType<SourceStatus>()
            .State.ShouldBe(SourceState.Error);
        _settings.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task SelectThatIsRefused_WhileASourceRuns_KeepsWhatWasRemembered()
    {
        // Arrange: a replay runs and is remembered
        await using SourceController controller = CreateController();
        await controller.HandleAsync(
            new Command.SelectReplay(Path: "captures/a.edges.txt", Speed: 10)
        );
        HostSettings remembered = _settings.Saved.ShouldHaveSingleItem();

        // Act: the next choice is refused, and the replay was stopped for it
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 2.0));

        // Assert: nothing more was saved, so the replay is what the next launch starts
        _settings.Saved.ShouldBe([remembered]);
        remembered.Running.ShouldBeTrue();
    }

    [Fact]
    public async Task SelectSerial_PortCannotOpen_StaysRemembered()
    {
        // Arrange: the port fails when the run starts to read it, after the answer "running"
        _environment.Serial = port => new FakeEdgeSource
        {
            Name = $"serial {port}",
            Fault = new UnauthorizedAccessException("Access to the path 'COM5' is denied."),
        };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.SelectSerial(Port: "COM5"));
        await _poster.WaitForAsync(p => p.Payload is SourceStatus { State: SourceState.Error });

        // Assert: a logger that is unplugged today may be back tomorrow
        _settings
            .Saved.ShouldHaveSingleItem()
            .ShouldBe(
                HostSettings.Default with
                {
                    Source = SourceKind.Serial,
                    Port = "COM5",
                    Running = true,
                }
            );
    }

    [Fact]
    public async Task RunThatEndsOnItsOwn_ChangesNothingRemembered()
    {
        // Arrange
        _environment.Synthetic = _ => new FakeEdgeSource { EndsAfterReset = true };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));
        await _poster.WaitForAsync(p => p.Payload is SourceStatus { State: SourceState.Stopped });

        // Assert: still the one save of the select, and it says running
        _settings.Saved.ShouldHaveSingleItem().Running.ShouldBeTrue();
    }

    [Fact]
    public async Task SetReplaySpeed_WhileAReplayRuns_RemembersTheSpeed()
    {
        // Arrange
        _settings.Remembered = Earlier;
        await using SourceController controller = CreateController();
        await controller.HandleAsync(
            new Command.SelectReplay(Path: "captures/a.edges.txt", Speed: 1)
        );

        // Act
        await controller.HandleAsync(new Command.SetReplaySpeed(Speed: 10));

        // Assert
        _settings.Saved.Count.ShouldBe(2);
        _settings.Saved[^1].ShouldBe(_settings.Saved[0] with { Speed = 10 });
    }

    [Theory]
    [InlineData(false)] // nothing runs
    [InlineData(true)] // the synthetic source runs
    public async Task SetReplaySpeed_WhenItIsIgnored_RemembersNothing(bool syntheticRuns)
    {
        // Arrange
        _settings.Remembered = Earlier;
        await using SourceController controller = CreateController();
        if (syntheticRuns)
        {
            await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));
        }
        int saves = _settings.Saved.Count;

        // Act
        await controller.HandleAsync(new Command.SetReplaySpeed(Speed: 10));

        // Assert
        _settings.Saved.Count.ShouldBe(saves);
    }

    [Fact]
    public async Task SetNoise_WhileSyntheticRuns_RemembersTheNoise()
    {
        // Arrange
        _settings.Remembered = Earlier;
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));

        // Act
        await controller.HandleAsync(new Command.SetNoise(Noise: 0.4));

        // Assert
        _settings.Saved.Count.ShouldBe(2);
        _settings.Saved[^1].ShouldBe(_settings.Saved[0] with { Noise = 0.4 });
    }

    [Theory]
    [InlineData(false)] // nothing runs
    [InlineData(true)] // a replay runs
    public async Task SetNoise_WhenItIsIgnored_RemembersNothing(bool replayRuns)
    {
        // Arrange
        _settings.Remembered = Earlier;
        await using SourceController controller = CreateController();
        if (replayRuns)
        {
            await controller.HandleAsync(
                new Command.SelectReplay(Path: "captures/a.edges.txt", Speed: 1)
            );
        }
        int saves = _settings.Saved.Count;

        // Act
        await controller.HandleAsync(new Command.SetNoise(Noise: 0.4));

        // Assert
        _settings.Saved.Count.ShouldBe(saves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetReducedCarrierLevel_RemembersTheLevel(bool aSourceRuns)
    {
        // Arrange: it applies to every source, and with none
        await using SourceController controller = CreateController();
        if (aSourceRuns)
        {
            await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));
        }
        HostSettings before = _settings.Saved.LastOrDefault() ?? HostSettings.Default;

        // Act
        await controller.HandleAsync(new Command.SetReducedCarrierLevel(PinLevel.Low));

        // Assert
        _settings.Saved[^1].ShouldBe(before with { ReducedCarrierLevel = PinLevel.Low });
    }

    [Fact]
    public async Task Stop_RemembersThatNothingRunsAndKeepsTheSixValues()
    {
        // Arrange
        _settings.Remembered = Earlier;
        await using SourceController controller = CreateController();
        await controller.HandleAsync(
            new Command.SelectReplay(Path: "captures/a.edges.txt", Speed: 10)
        );

        // Act
        await controller.HandleAsync(new Command.Stop());

        // Assert
        _settings.Saved.Count.ShouldBe(2);
        _settings.Saved[0].Running.ShouldBeTrue();
        _settings.Saved[^1].ShouldBe(_settings.Saved[0] with { Running = false });
    }

    [Fact]
    public async Task CommandsThatChangeNothing_SaveNothing()
    {
        // Arrange
        _settings.Remembered = Earlier;
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));
        await controller.HandleAsync(new Command.ListPorts());
        await controller.HandleAsync(new Command.ListCaptures());

        // Assert
        _settings.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task EachCommand_BuildsOnTheOneBefore()
    {
        // Arrange
        await using SourceController controller = CreateController();

        // Act: a session
        await controller.HandleAsync(new Command.SetReducedCarrierLevel(PinLevel.Low));
        await controller.HandleAsync(new Command.SelectSerial(Port: "COM5"));
        await controller.HandleAsync(
            new Command.SelectReplay(Path: "captures/a.edges.txt", Speed: 10)
        );
        await controller.HandleAsync(new Command.SetReplaySpeed(Speed: 60));
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.1));
        await controller.HandleAsync(new Command.SetNoise(Noise: 0.2));
        await controller.HandleAsync(new Command.Stop());

        // Assert: every value of the session is in the last save
        _settings.Saved.Count.ShouldBe(7);
        _settings
            .Saved[^1]
            .ShouldBe(
                new HostSettings(
                    Source: SourceKind.Synthetic,
                    Port: "COM5",
                    Capture: "a.edges.txt",
                    Speed: 60,
                    Noise: 0.2,
                    ReducedCarrierLevel: PinLevel.Low,
                    Running: false
                )
            );
    }
}
