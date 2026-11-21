using System.IO;
using WwvbDecoder.Host.Bridge;
using WwvbDecoder.Host.Services;

namespace WwvbDecoder.Host.Tests.Services;

/// <summary>
/// What the controller starts after the first <c>hello</c>: "At launch" in
/// docs/design/webview2-messages.md.
/// </summary>
public class SourceControllerLaunchTests
{
    private const string Newest = "captures/newest.edges.txt";
    private const string Older = "captures/older.edges.txt";

    private readonly FakeSourceEnvironment _environment = new()
    {
        Ports = ["COM3", "COM5"],
        Captures =
        [
            new CaptureEntry(Newest, new DateTimeOffset(2026, 11, 9, 4, 34, 0, TimeSpan.Zero), 525),
            new CaptureEntry(Older, new DateTimeOffset(2026, 11, 8, 23, 0, 0, TimeSpan.Zero), 10),
        ],
    };
    private readonly RecordingPoster _poster = new();
    private readonly FakeSettingsStore _settings = new();
    private readonly DecoderPipeline _pipeline = new(TimeProvider.System);

    private static readonly SourceStatus Idle = new(SourceKind.None, "", SourceState.Stopped, "");

    // A source was running, and nothing in it is a default.
    private static readonly HostSettings WasRunning = new(
        Source: SourceKind.Serial,
        Port: "COM5",
        Capture: "older.edges.txt",
        Speed: 10,
        Noise: 0.3,
        ReducedCarrierLevel: PinLevel.Low,
        Running: true
    );

    private SourceController CreateController() => new(_pipeline, _environment, _poster, _settings);

    /// <summary>The reply to <c>hello</c> is five messages. What follows them is the launch.</summary>
    private IReadOnlyList<(string Type, object? Payload)> AfterTheReply()
    {
        var posted = _poster.Posted;
        posted
            .Take(5)
            .Select(p => p.Type)
            .ShouldBe(["sourceStatus", "ports", "captures", "frameLayout", "selection"]);
        posted[0].Payload.ShouldBe(Idle, "the reply comes before the start");
        return [.. posted.Skip(5)];
    }

    private Selection SelectionPosted() =>
        _poster.Posted.Last(p => p.Type == "selection").Payload.ShouldBeOfType<Selection>();

    [Fact]
    public async Task FirstHello_NothingWasRunning_StartsNothing()
    {
        // Arrange: a source is remembered, and it was stopped
        _settings.Remembered = WasRunning with
        {
            Running = false,
        };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert
        AfterTheReply().ShouldBeEmpty();
        _environment.Created.ShouldBeEmpty();
        controller.Status.ShouldBe(Idle);
    }

    [Fact]
    public async Task FirstHello_NoSourceInTheSettings_StartsNothing()
    {
        // Arrange: the real store refuses such a file, so this is a store that did not
        _settings.Remembered = WasRunning with
        {
            Source = SourceKind.None,
        };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert
        AfterTheReply().ShouldBeEmpty();
        _environment.Created.ShouldBeEmpty();
    }

    [Fact]
    public async Task FirstHello_Synthetic_StartsItAtTheRememberedNoise()
    {
        // Arrange
        _settings.Remembered = WasRunning with
        {
            Source = SourceKind.Synthetic,
        };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert
        _environment
            .Created.ShouldHaveSingleItem()
            .ShouldBeOfType<SyntheticEdgeSource>()
            .Noise.ShouldBe(0.3);
        AfterTheReply()[0]
            .Payload.ShouldBe(
                new SourceStatus(SourceKind.Synthetic, "synthetic", SourceState.Running, "")
            );
        _settings.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task FirstHello_Serial_PortIsListed_StartsIt()
    {
        // Arrange
        _settings.Remembered = WasRunning;
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert
        _environment.SerialPorts.ShouldBe(["COM5"]);
        _environment.ReplayPaths.ShouldBeEmpty();
        AfterTheReply()[0]
            .Payload.ShouldBe(
                new SourceStatus(SourceKind.Serial, "serial COM5", SourceState.Running, "")
            );
        SelectionPosted().Source.ShouldBe(SourceKind.Serial);
        _settings.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task FirstHello_Serial_PortIsListedInAnotherCase_StartsIt()
    {
        // Arrange: Windows port names do not tell upper case from lower
        _settings.Remembered = WasRunning with
        {
            Port = "com5",
        };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert
        _environment.SerialPorts.ShouldBe(["com5"]);
        _environment.ReplayPaths.ShouldBeEmpty();
    }

    [Fact]
    public async Task FirstHello_Serial_PortIsGone_ReplaysTheNewestCaptureAndSaysSo()
    {
        // Arrange: the logger is unplugged
        _settings.Remembered = WasRunning with
        {
            Port = "COM9",
        };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert: the newest capture, at the remembered speed
        _environment.SerialPorts.ShouldBeEmpty();
        _environment.ReplayPaths.ShouldBe([Newest]);
        _environment
            .Created.ShouldHaveSingleItem()
            .ShouldBeOfType<ReplayEdgeSource>()
            .Speed.ShouldBe(10);
        AfterTheReply()[0]
            .Payload.ShouldBe(
                new SourceStatus(SourceKind.Replay, "replay", SourceState.Running, "", Speed: 10)
                {
                    Warning = "COM9 was not found. Replaying the newest capture.",
                }
            );

        // The selection names what is about to start, and keeps the port
        SelectionPosted()
            .ShouldBe(new Selection(SourceKind.Replay, "COM9", Newest, 10, 0.3, PinLevel.Low));

        // The file still says the logger, so the next launch is live again
        _settings.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task FirstHello_Serial_PortIsGone_OneCaptureIsEnough()
    {
        // Arrange: the folder holds a single capture
        _environment.Captures = [new CaptureEntry(Older, DateTimeOffset.UnixEpoch, 10)];
        _settings.Remembered = WasRunning with { Port = "COM9", Capture = null };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert
        _environment.ReplayPaths.ShouldBe([Older]);
        AfterTheReply()[0]
            .Payload.ShouldBeOfType<SourceStatus>()
            .Warning.ShouldBe("COM9 was not found. Replaying the newest capture.");
    }

    [Fact]
    public async Task FirstHello_Serial_PortIsGoneAndThereIsNoCapture_StartsNothingAndSaysSo()
    {
        // Arrange
        _environment.Captures = [];
        _settings.Remembered = WasRunning with { Port = "COM9" };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert
        _environment.Created.ShouldBeEmpty();
        AfterTheReply()
            .ShouldHaveSingleItem()
            .Payload.ShouldBe(
                Idle with
                {
                    Warning = "COM9 was not found, and there is no capture to replay.",
                }
            );
        SelectionPosted()
            .ShouldBe(new Selection(SourceKind.Serial, "COM9", null, 10, 0.3, PinLevel.Low));
        _settings.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task FirstHello_Replay_CaptureIsThere_ReplaysItAtTheRememberedSpeed()
    {
        // Arrange: not the newest
        _settings.Remembered = WasRunning with
        {
            Source = SourceKind.Replay,
        };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert
        _environment.ReplayPaths.ShouldBe([Older]);
        AfterTheReply()[0]
            .Payload.ShouldBe(
                new SourceStatus(SourceKind.Replay, "replay", SourceState.Running, "", Speed: 10)
            );
        SelectionPosted().Capture.ShouldBe(Older);
        _settings.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task FirstHello_Replay_CaptureIsGone_ReplaysTheNewestAndSaysSo()
    {
        // Arrange
        _settings.Remembered = WasRunning with
        {
            Source = SourceKind.Replay,
            Capture = "gone.edges.txt",
        };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert
        _environment.ReplayPaths.ShouldBe([Newest]);
        AfterTheReply()[0]
            .Payload.ShouldBe(
                new SourceStatus(SourceKind.Replay, "replay", SourceState.Running, "", Speed: 10)
                {
                    Warning = "gone.edges.txt was not found. Replaying the newest capture.",
                }
            );
        SelectionPosted()
            .ShouldBe(new Selection(SourceKind.Replay, "COM5", Newest, 10, 0.3, PinLevel.Low));
        _settings.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task FirstHello_Replay_ThereIsNoCapture_StartsNothingAndSaysSo()
    {
        // Arrange
        _environment.Captures = [];
        _settings.Remembered = WasRunning with
        {
            Source = SourceKind.Replay,
            Capture = "gone.edges.txt",
        };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert
        _environment.Created.ShouldBeEmpty();
        AfterTheReply()
            .ShouldHaveSingleItem()
            .Payload.ShouldBe(
                Idle with
                {
                    Warning = "gone.edges.txt was not found, and there is no capture to replay.",
                }
            );
        SelectionPosted().Source.ShouldBe(SourceKind.Replay);
        SelectionPosted().Capture.ShouldBeNull();
    }

    [Theory]
    [InlineData(SourceKind.Serial, "The port was not found. Replaying the newest capture.")]
    [InlineData(SourceKind.Replay, "The capture was not found. Replaying the newest capture.")]
    public async Task FirstHello_NoPortOrCaptureInTheFile_CountsAsNotFound(
        SourceKind source,
        string warning
    )
    {
        // Arrange: a file edited by hand can say serial with no port, or replay with no capture
        _settings.Remembered = WasRunning with
        {
            Source = source,
            Port = null,
            Capture = null,
        };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert
        _environment.ReplayPaths.ShouldBe([Newest]);
        AfterTheReply()[0].Payload.ShouldBeOfType<SourceStatus>().Warning.ShouldBe(warning);
    }

    [Fact]
    public async Task FirstHello_StartThatIsRefused_AnswersTheErrorOfASelect()
    {
        // Arrange: the capture is listed and cannot be read
        _environment.Replay = (_, _) => throw new FileNotFoundException("no such capture");
        _settings.Remembered = WasRunning with { Source = SourceKind.Replay };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert: the same answer as selectSource gives, and no warning beside it
        AfterTheReply()
            .ShouldHaveSingleItem()
            .Payload.ShouldBe(
                new SourceStatus(SourceKind.Replay, "replay", SourceState.Error, "no such capture")
            );
        _settings.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task SecondHello_StartsNothingMore()
    {
        // Arrange
        _settings.Remembered = WasRunning with
        {
            Source = SourceKind.Synthetic,
        };
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.Hello(1));

        // Act: the page reloads
        await controller.HandleAsync(new Command.Hello(1));

        // Assert: the one run goes on, and the second reply says so
        _environment.Created.ShouldHaveSingleItem();
        var status = _poster
            .Posted.Last(p => p.Type == "sourceStatus")
            .Payload.ShouldBeOfType<SourceStatus>();
        status.Source.ShouldBe(SourceKind.Synthetic);
        status.State.ShouldBe(SourceState.Running);
    }

    [Fact]
    public async Task Hello_AfterStop_StartsNothing()
    {
        // Arrange: launched, then stopped by the presenter
        _settings.Remembered = WasRunning with
        {
            Source = SourceKind.Synthetic,
        };
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.Hello(1));
        await controller.HandleAsync(new Command.Stop());

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert
        _environment.Created.ShouldHaveSingleItem();
        controller.Status.ShouldBe(Idle);
    }

    [Fact]
    public async Task FirstHello_AfterASelect_DoesNotStartTheRememberedSource()
    {
        // Arrange: a command chose a source before any hello
        _settings.Remembered = WasRunning with
        {
            Source = SourceKind.Synthetic,
        };
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.SelectSerial(Port: "COM3"));

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert: the chosen source runs on
        _environment.Created.ShouldHaveSingleItem();
        _environment.SerialPorts.ShouldBe(["COM3"]);
        controller.Status.Source.ShouldBe(SourceKind.Serial);
        controller.Status.State.ShouldBe(SourceState.Running);
    }

    [Fact]
    public async Task FirstHello_AfterAStop_DoesNotStartTheRememberedSource()
    {
        // Arrange
        _settings.Remembered = WasRunning with
        {
            Source = SourceKind.Synthetic,
        };
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.Stop());

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert
        _environment.Created.ShouldBeEmpty();
    }

    [Fact]
    public async Task Warning_LastsThroughAResetAndAChangeOfSpeed()
    {
        // Arrange: the newest capture replays in place of the logger
        const string warning = "COM9 was not found. Replaying the newest capture.";
        _settings.Remembered = WasRunning with { Port = "COM9" };
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.Hello(1));

        // Act: the source's first event is a reset, and then the presenter changes the speed
        var reset = await _poster.WaitForAsync(p =>
            p.Payload is SourceStatus { Detail: "reset: start" }
        );
        await controller.HandleAsync(new Command.SetReplaySpeed(Speed: 60));

        // Assert: detail is replaced each time, the warning stays
        reset.Payload.ShouldBeOfType<SourceStatus>().Warning.ShouldBe(warning);
        controller.Status.ShouldBe(
            new SourceStatus(SourceKind.Replay, "replay", SourceState.Running, "", Speed: 60)
            {
                Warning = warning,
            }
        );
    }

    [Fact]
    public async Task Warning_IsGoneAfterStop()
    {
        // Arrange
        _settings.Remembered = WasRunning with
        {
            Port = "COM9",
        };
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.Hello(1));

        // Act
        await controller.HandleAsync(new Command.Stop());

        // Assert
        controller.Status.ShouldBe(Idle);
        _poster.Posted[^1].Payload.ShouldBe(Idle);
    }

    [Theory]
    [InlineData(true)] // the newest capture replays in place of the logger
    [InlineData(false)] // nothing could be started
    public async Task Warning_IsGoneWhenASourceIsSelected(bool thereIsACapture)
    {
        // Arrange
        if (!thereIsACapture)
        {
            _environment.Captures = [];
        }
        _settings.Remembered = WasRunning with { Port = "COM9" };
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.Hello(1));
        controller.Status.Warning.ShouldNotBeEmpty();

        // Act
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));

        // Assert
        controller.Status.Warning.ShouldBeEmpty();
        controller.Status.Source.ShouldBe(SourceKind.Synthetic);
    }

    [Fact]
    public async Task Replacement_ALaterHello_StillNamesWhatRuns()
    {
        // Arrange
        _settings.Remembered = WasRunning with
        {
            Port = "COM9",
        };
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.Hello(1));

        // Act: the page reloads while the capture replays
        await controller.HandleAsync(new Command.Hello(1));

        // Assert
        _poster.Posted.Count(p => p.Type == "selection").ShouldBe(2);
        SelectionPosted()
            .ShouldBe(new Selection(SourceKind.Replay, "COM9", Newest, 10, 0.3, PinLevel.Low));
        _environment.Created.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Replacement_AfterStop_ALaterHelloNamesWhatIsRemembered()
    {
        // Arrange
        _settings.Remembered = WasRunning with
        {
            Port = "COM9",
        };
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.Hello(1));
        await controller.HandleAsync(new Command.Stop());

        // Act
        await controller.HandleAsync(new Command.Hello(1));

        // Assert: the logger again, and the capture that was remembered with it
        SelectionPosted()
            .ShouldBe(new Selection(SourceKind.Serial, "COM9", Older, 10, 0.3, PinLevel.Low));
    }

    [Fact]
    public async Task Replacement_ThatIsRefused_AnswersTheErrorAndALaterHelloNamesWhatIsRemembered()
    {
        // Arrange: the logger is unplugged, and the newest capture cannot be read
        _environment.Replay = (_, _) => throw new FileNotFoundException("no such capture");
        _settings.Remembered = WasRunning with { Port = "COM9" };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(1));
        await controller.HandleAsync(new Command.Hello(1));

        // Assert: the error of a select, with no word of a replay that did not start
        AfterTheReply()[0]
            .Payload.ShouldBe(
                new SourceStatus(SourceKind.Replay, "replay", SourceState.Error, "no such capture")
            );
        SelectionPosted()
            .ShouldBe(new Selection(SourceKind.Serial, "COM9", Older, 10, 0.3, PinLevel.Low));
        _settings.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task Replacement_CommandsWhileItRuns_KeepTheRememberedSourceInTheFile()
    {
        // Arrange
        HostSettings remembered = WasRunning with
        {
            Port = "COM9",
        };
        _settings.Remembered = remembered;
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.Hello(1));

        // Act: the presenter speeds the capture up, then stops it
        await controller.HandleAsync(new Command.SetReplaySpeed(Speed: 60));
        await controller.HandleAsync(new Command.Stop());

        // Assert: the speed and the stop are remembered, and the source is still the logger
        _settings.Saved.ShouldBe([
            remembered with
            {
                Speed = 60,
            },
            remembered with
            {
                Speed = 60,
                Running = false,
            },
        ]);
    }

    [Fact]
    public async Task Replacement_ThenASelect_IsRememberedAsAnySelect()
    {
        // Arrange
        HostSettings remembered = WasRunning with
        {
            Port = "COM9",
        };
        _settings.Remembered = remembered;
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.Hello(1));

        // Act: the presenter starts the capture on show, which makes it the choice
        await controller.HandleAsync(new Command.SelectReplay(Newest, Speed: 10));
        await controller.HandleAsync(new Command.Hello(1));

        // Assert
        _settings
            .Saved.ShouldHaveSingleItem()
            .ShouldBe(remembered with { Source = SourceKind.Replay, Capture = "newest.edges.txt" });
        SelectionPosted()
            .ShouldBe(new Selection(SourceKind.Replay, "COM9", Newest, 10, 0.3, PinLevel.Low));
    }
}
