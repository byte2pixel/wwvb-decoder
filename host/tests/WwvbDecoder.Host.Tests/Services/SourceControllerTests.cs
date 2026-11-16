using System.IO;
using WwvbDecoder.Host.Bridge;
using WwvbDecoder.Host.Services;

namespace WwvbDecoder.Host.Tests.Services;

public class SourceControllerTests
{
    private readonly FakeSourceEnvironment _environment = new();
    private readonly RecordingPoster _poster = new();
    private readonly DecoderPipeline _pipeline = new(TimeProvider.System);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SourceController CreateController() => new(_pipeline, _environment, _poster);

    [Theory]
    [InlineData(1)]
    [InlineData(99)] // a mismatch is logged, not refused
    public async Task Hello_AnswersStatusPortsCapturesInOrder(int protocol)
    {
        // Arrange
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Hello(protocol));

        // Assert
        var posted = _poster.Posted;
        posted.Select(p => p.Type).ShouldBe(["sourceStatus", "ports", "captures"]);
        posted[0].Payload.ShouldBe(new SourceStatus(SourceKind.None, "", SourceState.Stopped, ""));
        posted[1].Payload.ShouldBeOfType<ItemsPayload<string>>().Items.ShouldBe(["COM3", "COM5"]);
        posted[2]
            .Payload.ShouldBeOfType<ItemsPayload<CaptureEntry>>()
            .Items.ShouldBe(_environment.Captures);
    }

    [Theory]
    [InlineData("listPorts", "ports")]
    [InlineData("listCaptures", "captures")]
    public async Task ListCommand_AnswersOneMessage(string command, string expectedType)
    {
        // Arrange
        await using SourceController controller = CreateController();
        Command parsed = command switch
        {
            "listPorts" => new Command.ListPorts(),
            "listCaptures" => new Command.ListCaptures(),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };

        // Act
        await controller.HandleAsync(parsed);

        // Assert
        _poster.Posted.ShouldHaveSingleItem().Type.ShouldBe(expectedType);
    }

    [Fact]
    public async Task SelectSynthetic_StartsPipelineAndAnswersRunning()
    {
        // Arrange
        await using SourceController controller = CreateController();
        TaskCompletionSource<string> reset = new();
        _pipeline.SourceReset += reason => reset.TrySetResult(reason);

        // Act
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));

        // Assert
        _environment
            .Created.ShouldHaveSingleItem()
            .ShouldBeOfType<SyntheticEdgeSource>()
            .Noise.ShouldBe(0.0);
        var first = _poster.Posted[0];
        first.Type.ShouldBe("sourceStatus");
        first.Payload.ShouldBe(
            new SourceStatus(SourceKind.Synthetic, "synthetic", SourceState.Running, "")
        );
        controller.Status.State.ShouldBe(SourceState.Running);
        (await reset.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct)).ShouldBe("start");
    }

    [Fact]
    public async Task DisposeAsync_StopsTheRunSoThePipelineIsFreeAgain()
    {
        // Arrange
        SourceController controller = CreateController();
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));

        // Act
        await controller.DisposeAsync();

        // Assert: a second run on the same pipeline would throw "already running" if the first were still going
        await using SourceController next = CreateController();
        await next.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));
        next.Status.State.ShouldBe(SourceState.Running);
    }

    [Fact]
    public async Task SelectSynthetic_NoiseOutOfRange_AnswersErrorAndStartsNothing()
    {
        // Arrange
        await using SourceController controller = CreateController();

        // Act: the real source refuses a noise above 1 when it is created
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 2.0));

        // Assert
        var status = _poster.Posted.ShouldHaveSingleItem().Payload.ShouldBeOfType<SourceStatus>();
        status.Source.ShouldBe(SourceKind.Synthetic);
        status.Name.ShouldBe("synthetic");
        status.State.ShouldBe(SourceState.Error);
        status.Detail.ShouldStartWith("Noise must be a number between 0.0 and 1.0.");
        _environment.Created.ShouldBeEmpty();
    }

    [Fact]
    public async Task SelectSerial_StartsPipelineAndAnswersRunning()
    {
        // Arrange
        await using SourceController controller = CreateController();
        TaskCompletionSource<string> reset = new();
        _pipeline.SourceReset += reason => reset.TrySetResult(reason);

        // Act
        await controller.HandleAsync(new Command.SelectSerial(Port: "COM5"));

        // Assert
        _environment.SerialPorts.ShouldBe(["COM5"]);
        _poster
            .Posted[0]
            .Payload.ShouldBe(
                new SourceStatus(SourceKind.Serial, "serial COM5", SourceState.Running, "")
            );
        controller.Status.State.ShouldBe(SourceState.Running);
        (await reset.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct)).ShouldBe("start");
    }

    [Fact]
    public async Task SelectSerial_PortCannotOpen_AnswersErrorWithTheMessage()
    {
        // Arrange: a serial source opens its port when the run starts to read it
        _environment.Serial = port => new FakeEdgeSource
        {
            Name = $"serial {port}",
            Fault = new UnauthorizedAccessException("Access to the path 'COM5' is denied."),
        };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.SelectSerial(Port: "COM5"));

        // Assert
        var post = await _poster.WaitForAsync(p =>
            p.Payload is SourceStatus { State: SourceState.Error }
        );
        post.Payload.ShouldBe(
            new SourceStatus(
                SourceKind.Serial,
                "serial COM5",
                SourceState.Error,
                "Access to the path 'COM5' is denied."
            )
        );
    }

    [Fact]
    public async Task SelectSerial_BannerLine_BecomesTheStatusDetail()
    {
        // Arrange: another info line comes first and is not the banner
        const string banner = "# wwvb-edge-logger 0.1.0 pin=2";
        _environment.Serial = port => new FakeEdgeSource
        {
            Name = $"serial {port}",
            Script = [new EdgeEvent.Info("# dropped 1"), new EdgeEvent.Info(banner)],
        };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.SelectSerial(Port: "COM5"));

        // Assert
        var post = await _poster.WaitForAsync(p => p.Payload is SourceStatus { Detail: banner });
        post.Payload.ShouldBe(
            new SourceStatus(SourceKind.Serial, "serial COM5", SourceState.Running, banner)
        );
        _poster
            .Posted.Select(p => p.Payload)
            .OfType<SourceStatus>()
            .Select(s => s.Detail)
            .ShouldBe(["", "reset: start", banner]);
    }

    [Fact]
    public async Task BannerLine_FromASourceThatIsNotSerial_LeavesTheStatusAlone()
    {
        // Arrange: a capture holds the banner of the session it recorded
        _environment.Replay = (_, _) =>
            new FakeEdgeSource
            {
                Name = "replay",
                Script =
                [
                    new EdgeEvent.Info("# wwvb-edge-logger 0.1.0 pin=2"),
                    new EdgeEvent.Reset("loop"),
                ],
            };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(
            new Command.SelectReplay(Path: "captures/a.edges.txt", Speed: 1)
        );

        // Assert: the reset after the banner has been reported, and the banner was not
        await _poster.WaitForAsync(p => p.Payload is SourceStatus { Detail: "reset: loop" });
        _poster
            .Posted.Select(p => p.Payload)
            .OfType<SourceStatus>()
            .Select(s => s.Detail)
            .ShouldBe(["", "reset: start", "reset: loop"]);
    }

    [Fact]
    public async Task SelectReplay_StartsPipelineAndAnswersRunning()
    {
        // Arrange
        await using SourceController controller = CreateController();
        TaskCompletionSource<string> reset = new();
        _pipeline.SourceReset += reason => reset.TrySetResult(reason);

        // Act
        await controller.HandleAsync(
            new Command.SelectReplay(Path: "captures/a.edges.txt", Speed: 10)
        );

        // Assert
        _environment.ReplayPaths.ShouldBe(["captures/a.edges.txt"]);
        _environment
            .Created.ShouldHaveSingleItem()
            .ShouldBeOfType<ReplayEdgeSource>()
            .Speed.ShouldBe(10);
        _poster
            .Posted[0]
            .Payload.ShouldBe(
                new SourceStatus(SourceKind.Replay, "replay", SourceState.Running, "")
            );
        controller.Status.State.ShouldBe(SourceState.Running);
        (await reset.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct)).ShouldBe("start");
    }

    [Fact]
    public async Task SelectReplay_CaptureCannotBeRead_AnswersErrorWithTheMessage()
    {
        // Arrange
        _environment.Replay = (_, _) => throw new FileNotFoundException("no such capture");
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(
            new Command.SelectReplay(Path: "captures/missing.edges.txt", Speed: 1)
        );

        // Assert
        _poster
            .Posted.ShouldHaveSingleItem()
            .Payload.ShouldBe(
                new SourceStatus(SourceKind.Replay, "replay", SourceState.Error, "no such capture")
            );
        controller.Status.State.ShouldBe(SourceState.Error);
    }

    [Fact]
    public async Task SelectReplay_OnTheRealEnvironment_ReplaysTheFileAndLoops()
    {
        // Arrange: a capture of 20 ms in a captures directory of its own
        string directory = Path.Combine(
            Path.GetTempPath(),
            "wwvb-host-tests-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "2026-11-09T0434Z-t.edges.txt");
            string[] lines =
            [
                "# capture v1",
                "# recorded 2026-11-09T04:34:28Z",
                "# source test",
                "H 1000000",
                "L 1010000",
                "H 1020000",
            ];
            await File.WriteAllTextAsync(path, string.Join('\n', lines) + "\n", Ct);
            var environment = new HostEnvironment(TimeProvider.System, directory);
            await using SourceController controller = new(_pipeline, environment, _poster);

            // Act
            await controller.HandleAsync(new Command.SelectReplay(path, Speed: 1));

            // Assert: the file was read to its end, and the replay started over
            await _poster.WaitForAsync(p => p.Payload is SourceStatus { Detail: "reset: loop" });
            controller.Status.Source.ShouldBe(SourceKind.Replay);
            controller.Status.State.ShouldBe(SourceState.Running);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SelectSerial_WhileSyntheticRuns_StopsTheRunFirst()
    {
        // Arrange
        var synthetic = new FakeEdgeSource();
        _environment.Synthetic = _ => synthetic;
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));

        // Act
        await controller.HandleAsync(new Command.SelectSerial(Port: "COM5"));

        // Assert
        synthetic.Disposed.ShouldBeTrue();
        controller.Status.Source.ShouldBe(SourceKind.Serial);
        controller.Status.State.ShouldBe(SourceState.Running);
    }

    [Fact]
    public async Task SelectThatFails_WhileASourceRuns_StillStopsTheRun()
    {
        // Arrange
        var synthetic = new FakeEdgeSource();
        _environment.Synthetic = _ => synthetic;
        _environment.Replay = (_, _) => throw new FileNotFoundException("no such capture");
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));

        // Act
        await controller.HandleAsync(
            new Command.SelectReplay(Path: "captures/missing.edges.txt", Speed: 1)
        );

        // Assert: the old source is gone and the status says why nothing runs
        synthetic.Disposed.ShouldBeTrue();
        controller.Status.ShouldBe(
            new SourceStatus(SourceKind.Replay, "replay", SourceState.Error, "no such capture")
        );
    }

    [Fact]
    public async Task Stop_DisposesTheSourceAndAnswersNoneStopped()
    {
        // Arrange
        var fake = new FakeEdgeSource();
        _environment.Synthetic = _ => fake;
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));

        // Act
        await controller.HandleAsync(new Command.Stop());

        // Assert
        fake.Disposed.ShouldBeTrue();
        _poster
            .Posted[^1]
            .Payload.ShouldBe(new SourceStatus(SourceKind.None, "", SourceState.Stopped, ""));
        controller.Status.Source.ShouldBe(SourceKind.None);
    }

    [Fact]
    public async Task Stop_WhenNothingRuns_StillAnswersNoneStopped()
    {
        // Arrange
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.Stop());

        // Assert
        _poster
            .Posted.ShouldHaveSingleItem()
            .Payload.ShouldBe(new SourceStatus(SourceKind.None, "", SourceState.Stopped, ""));
    }

    [Fact]
    public async Task RunThatEndsOnItsOwn_AnswersStopped()
    {
        // Arrange
        _environment.Synthetic = _ => new FakeEdgeSource { EndsAfterReset = true };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));

        // Assert
        var post = await _poster.WaitForAsync(p =>
            p.Payload is SourceStatus { State: SourceState.Stopped }
        );
        post.Payload.ShouldBe(
            new SourceStatus(SourceKind.Synthetic, "fake", SourceState.Stopped, "")
        );
    }

    [Fact]
    public async Task RunThatFaults_AnswersErrorWithTheMessage()
    {
        // Arrange
        _environment.Synthetic = _ => new FakeEdgeSource { Fault = new IOException("port gone") };
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));

        // Assert
        var post = await _poster.WaitForAsync(p =>
            p.Payload is SourceStatus { State: SourceState.Error }
        );
        post.Payload.ShouldBe(
            new SourceStatus(SourceKind.Synthetic, "fake", SourceState.Error, "port gone")
        );
    }

    [Fact]
    public async Task SelectWhileRunning_DisposesTheOldSourceAndNeverReportsItAsStopped()
    {
        // Arrange
        var first = new FakeEdgeSource();
        _environment.Synthetic = _ => first;
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));
        _environment.Synthetic = _ => new FakeEdgeSource();

        // Act
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.1));

        // Assert
        first.Disposed.ShouldBeTrue();
        controller.Status.State.ShouldBe(SourceState.Running);
        _poster
            .Posted.Select(p => p.Payload)
            .OfType<SourceStatus>()
            .ShouldAllBe(s => s.State == SourceState.Running);
    }

    [Fact]
    public async Task SetNoise_ChangesTheRunningSynthetic()
    {
        // Arrange
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));

        // Act
        await controller.HandleAsync(new Command.SetNoise(Noise: 0.2));

        // Assert
        _environment
            .Created.ShouldHaveSingleItem()
            .ShouldBeOfType<SyntheticEdgeSource>()
            .Noise.ShouldBe(0.2);
    }

    [Fact]
    public async Task SetNoise_WhenNothingRuns_IsIgnored()
    {
        // Arrange
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.SetNoise(Noise: 0.2));

        // Assert
        _poster.Posted.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetReducedCarrierLevel_UpdatesStageTwo()
    {
        // Arrange
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.SetReducedCarrierLevel(PinLevel.Low));

        // Assert
        _pipeline.PulseMeasurement.ReducedCarrierLevel.ShouldBe(PinLevel.Low);
        _poster.Posted.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetReplaySpeed_ChangesTheRunningReplay()
    {
        // Arrange
        await using SourceController controller = CreateController();
        await controller.HandleAsync(
            new Command.SelectReplay(Path: "captures/a.edges.txt", Speed: 1)
        );

        // Act
        await controller.HandleAsync(new Command.SetReplaySpeed(Speed: 10));

        // Assert
        _environment
            .Created.ShouldHaveSingleItem()
            .ShouldBeOfType<ReplayEdgeSource>()
            .Speed.ShouldBe(10);
    }

    [Fact]
    public async Task SetReplaySpeed_WhenNothingRuns_IsIgnored()
    {
        // Arrange
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.SetReplaySpeed(Speed: 10));

        // Assert
        _poster.Posted.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetReplaySpeed_WhenTheSourceIsNotReplay_IsIgnored()
    {
        // Arrange
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));

        // Act
        await controller.HandleAsync(new Command.SetReplaySpeed(Speed: 10));

        // Assert: the synthetic source keeps its own speed
        _environment
            .Created.ShouldHaveSingleItem()
            .ShouldBeOfType<SyntheticEdgeSource>()
            .Speed.ShouldBe(1);
    }

    [Fact]
    public async Task SourceReset_AnswersStatusWithTheReason()
    {
        // Arrange
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));

        // Assert
        var post = await _poster.WaitForAsync(p =>
            p.Payload is SourceStatus { Detail: "reset: start" }
        );
        post.Payload.ShouldBe(
            new SourceStatus(SourceKind.Synthetic, "synthetic", SourceState.Running, "reset: start")
        );
        _poster
            .Posted.Select(p => p.Payload)
            .OfType<SourceStatus>()
            .Select(s => s.Detail)
            .ShouldBe(["", "reset: start"]);
    }
}
