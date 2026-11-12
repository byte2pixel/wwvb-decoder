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

    [Theory]
    [InlineData("serial", SourceKind.Serial, "serial COM5")]
    [InlineData("replay", SourceKind.Replay, "replay")]
    public async Task SelectSerialOrReplay_AnswersNotAvailableYet(
        string source,
        SourceKind kind,
        string name
    )
    {
        // Arrange
        await using SourceController controller = CreateController();
        Command select = source switch
        {
            "serial" => new Command.SelectSerial(Port: "COM5"),
            "replay" => new Command.SelectReplay(Path: "captures/a.edges.txt", Speed: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

        // Act
        await controller.HandleAsync(select);

        // Assert
        _poster
            .Posted.ShouldHaveSingleItem()
            .Payload.ShouldBe(new SourceStatus(kind, name, SourceState.Error, "not available yet"));
        _environment.Created.ShouldBeEmpty();
    }

    [Fact]
    public async Task SelectSerial_WhileSyntheticRuns_StopsTheRunFirst()
    {
        // Arrange
        await using SourceController controller = CreateController();
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));

        // Act
        await controller.HandleAsync(new Command.SelectSerial(Port: "COM5"));

        // Assert
        controller.Status.State.ShouldBe(SourceState.Error);
        // the pipeline is free again, so a new synthetic run starts instead of throwing "already running"
        await controller.HandleAsync(new Command.SelectSynthetic(Noise: 0.0));
        controller.Status.State.ShouldBe(SourceState.Running);
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
    public async Task SetReplaySpeed_IsIgnoredUntilReplayArrives()
    {
        // Arrange
        await using SourceController controller = CreateController();

        // Act
        await controller.HandleAsync(new Command.SetReplaySpeed(Speed: 10));

        // Assert
        _poster.Posted.ShouldBeEmpty();
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
