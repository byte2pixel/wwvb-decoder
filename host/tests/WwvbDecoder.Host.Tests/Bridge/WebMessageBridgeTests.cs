using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using WwvbDecoder.Host.Bridge;
using WwvbDecoder.Host.Tests.Services;

namespace WwvbDecoder.Host.Tests.Bridge;

public sealed class WebMessageBridgeTests : IDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);

    private readonly DecoderPipeline _pipeline = new(TimeProvider.System);
    private readonly FakeTimeProvider _time = new();
    private readonly List<string> _posted = [];
    private readonly WebMessageBridge _bridge;
    private Action<string>? _onPost;

    public WebMessageBridgeTests() =>
        _bridge = new WebMessageBridge(
            _pipeline,
            _time,
            invokeOnUi: a => a(),
            postJson: json =>
            {
                _posted.Add(json);
                _onPost?.Invoke(json);
            }
        );

    public void Dispose() => _bridge.Dispose();

    private IReadOnlyList<JsonNode> Messages => [.. _posted.Select(j => JsonNode.Parse(j)!)];

    private IEnumerable<string> Types => Messages.Select(m => (string)m["type"]!);

    /// <summary>The item counts of every <c>edges</c> message, in order.</summary>
    private IReadOnlyList<int> EdgeBatches =>
        [
            .. Messages
                .Where(m => (string)m["type"]! == "edges")
                .Select(m => m["payload"]!["items"]!.AsArray().Count),
        ];

    private Task RunScriptAsync(IReadOnlyList<EdgeEvent> script) =>
        _pipeline.RunAsync(
            new FakeEdgeSource { Script = script, EndsAfterReset = true },
            CancellationToken.None
        );

    /// <summary>
    /// The three edges of the doc's <c>edges</c> example, then an info line. Stage 2 releases
    /// the 200 ms pulse when the third edge opens the next one.
    /// </summary>
    private static readonly EdgeEvent[] OnePulse =
    [
        new EdgeEvent.Sample(new Edge(12_000_000, PinLevel.High)),
        new EdgeEvent.Sample(new Edge(12_200_412, PinLevel.Low)),
        new EdgeEvent.Sample(new Edge(13_000_003, PinLevel.High)),
        new EdgeEvent.Info("# wall 2026-11-02T03:11:00Z 14500398"),
    ];

    private static EdgeEvent[] Edges(int count) =>
        [
            .. Enumerable
                .Range(0, count)
                .Select(i => new EdgeEvent.Sample(
                    new Edge(
                        1_000_000UL + (ulong)i * 1_000,
                        i % 2 == 0 ? PinLevel.High : PinLevel.Low
                    )
                )),
        ];

    [Fact]
    public void Post_NumbersMessagesFromOneInPostingOrder()
    {
        // Act
        _bridge.Post("info", new InfoPayload("# a"));
        _bridge.Post("info", new InfoPayload("# b"));

        // Assert
        Messages.Select(m => (int)m["seq"]!).ShouldBe([1, 2]);
        Types.ShouldBe(["info", "info"]);
        Messages[1]["payload"]!["line"]!.GetValue<string>().ShouldBe("# b");
    }

    [Fact]
    public async Task PipelineEvents_ArePostedWithTheirTypes()
    {
        // Act
        await RunScriptAsync(OnePulse);

        // Assert
        Types.ShouldContain("pulse");
        Types.ShouldContain("symbol");
        Types.ShouldContain("frameSync");
        Types.ShouldContain("info");

        JsonNode pulse = Messages.Single(m => (string)m["type"]! == "pulse");
        JsonNode
            .DeepEquals(pulse["payload"], Examples.Read("pulse")["payload"])
            .ShouldBeTrue(pulse.ToJsonString());
        Messages.Single(m => (string)m["type"]! == "info")["payload"]!["line"]!
            .GetValue<string>()
            .ShouldBe("# wall 2026-11-02T03:11:00Z 14500398");
    }

    [Fact]
    public async Task EveryStageOutput_ReachesThePage()
    {
        // Arrange: an unpaced synthetic source, cancelled by the first clock message
        using CancellationTokenSource cts = new();
        _onPost = json =>
        {
            if (json.Contains("\"type\":\"clock\""))
            {
                cts.Cancel();
            }
        };
        var source = new SyntheticEdgeSource(TimeProvider.System)
        {
            StartUtc = new DateTimeOffset(2026, 12, 10, 19, 30, 0, TimeSpan.Zero),
            Speed = 0,
        };

        // Act
        await Should.ThrowAsync<OperationCanceledException>(() =>
            _pipeline.RunAsync(source, cts.Token)
        );
        _time.Advance(Tick); // the first frame is under 200 edges, so the batch flushes on the timer

        // Assert
        Types
            .Distinct()
            .ShouldBe(
                ["edges", "pulse", "symbol", "frameSync", "decodedFrame", "validation", "clock"],
                ignoreOrder: true
            );
        Messages.Select(m => (int)m["seq"]!).ShouldBe(Enumerable.Range(1, _posted.Count));
    }

    [Fact]
    public async Task Edges_FlushEveryHundredMilliseconds_NeverEmpty()
    {
        // Act
        await RunScriptAsync(Edges(3));

        // Assert
        EdgeBatches.ShouldBeEmpty();
        _time.Advance(Tick);
        EdgeBatches.ShouldBe([3]);
        _time.Advance(Tick);
        EdgeBatches.ShouldBe([3]);
    }

    [Fact]
    public async Task Edges_FlushAtTwoHundredItems()
    {
        // Act
        await RunScriptAsync(Edges(201));

        // Assert
        EdgeBatches.ShouldBe([200]);
        _time.Advance(Tick);
        EdgeBatches.ShouldBe([200, 1]);
    }

    [Fact]
    public async Task Edges_KeepArrivalOrder()
    {
        // Act
        await RunScriptAsync(Edges(3));
        _time.Advance(Tick);

        // Assert
        JsonNode items = Messages.Single(m => (string)m["type"]! == "edges")["payload"]!["items"]!;
        items
            .AsArray()
            .Select(e => (ulong)e!["micros"]!)
            .ShouldBe([1_000_000UL, 1_001_000UL, 1_002_000UL]);
        items[0]!["level"]!.GetValue<string>().ShouldBe("high");
    }

    [Fact]
    public async Task Dispose_StopsTheTimerAndUnsubscribes()
    {
        // Act
        _bridge.Dispose();
        await RunScriptAsync(OnePulse);
        _time.Advance(Tick);
        _bridge.Post("info", new InfoPayload("# late"));

        // Assert
        _posted.ShouldBeEmpty();
    }
}
