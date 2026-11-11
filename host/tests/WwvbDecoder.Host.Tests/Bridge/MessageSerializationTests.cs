using System.Text.Json.Nodes;
using WwvbDecoder.Host.Bridge;

namespace WwvbDecoder.Host.Tests.Bridge;

public class MessageSerializationTests
{
    /// <summary>The plan's test vector, 2026-12-10T19:<paramref name="minute"/>Z, on the synthetic timeline.</summary>
    private static Frame TestVectorFrame(int minute, ulong startMicros) =>
        WwvbFrameEncoder.Encode(
            new DateTimeOffset(2026, 12, 10, 19, minute, 0, TimeSpan.Zero),
            startMicros,
            Ut1Sign.Plus,
            ut1Tenths: 0,
            leapSecondPending: false,
            DstStatus.Standard
        );

    [Fact]
    public void Serialize_Edge_MatchesExample()
    {
        // Arrange
        ItemsPayload<Edge> edges = new([
            new Edge(Micros: 12_000_000, Level: PinLevel.High),
            new Edge(Micros: 12_200_412, Level: PinLevel.Low),
            new Edge(Micros: 13_000_003, Level: PinLevel.High),
        ]);

        // Act
        string json = MessageJson.Serialize("edges", seq: 42, edges);

        // Assert
        JsonNode.DeepEquals(JsonNode.Parse(json), Examples.Read("edges")).ShouldBeTrue(json);
    }

    [Fact]
    public void Serialize_Pulse_MatchesExample()
    {
        // Arrange
        var pulse = new Pulse(StartMicros: 12_000_000, DurationMicros: 200_412);

        // Act
        string json = MessageJson.Serialize("pulse", seq: 43, pulse);

        // Assert
        JsonNode.DeepEquals(JsonNode.Parse(json), Examples.Read("pulse")).ShouldBeTrue(json);
    }

    [Fact]
    public void Serialize_Symbol_MatchesExample()
    {
        // Arrange
        var symbol = new Symbol(
            StartMicros: 12_000_000,
            DurationMicros: 200_412,
            Kind: SymbolKind.Zero
        );

        // Act
        string json = MessageJson.Serialize("symbol", seq: 44, symbol);

        // Assert
        JsonNode.DeepEquals(JsonNode.Parse(json), Examples.Read("symbol")).ShouldBeTrue(json);
    }

    [Fact]
    public void Serialize_FrameSync_SlotFilled_MatchesExample()
    {
        // Arrange: declared as the base type, which is how the pipeline hands it to the bridge
        FrameSyncEvent frameSync = new FrameSyncEvent.SlotFilled(
            FrameStartMicros: 100_000_000,
            Second: 1,
            Kind: SymbolKind.Zero
        );

        // Act
        string json = MessageJson.Serialize("frameSync", seq: 45, frameSync);

        // Assert
        JsonNode.DeepEquals(JsonNode.Parse(json), Examples.Read("frameSync")).ShouldBeTrue(json);
    }

    [Theory]
    [InlineData("searching", """{ "kind": "searching", "consecutiveMarkers": 1 }""")]
    [InlineData("syncLost", """{ "kind": "syncLost", "reason": "gap of 3 s" }""")]
    public void Serialize_FrameSync_OtherKinds_CarryDiscriminator(
        string kind,
        string expectedPayload
    )
    {
        // Arrange
        FrameSyncEvent frameSync = kind switch
        {
            "searching" => new FrameSyncEvent.Searching(ConsecutiveMarkers: 1),
            "syncLost" => new FrameSyncEvent.SyncLost(Reason: "gap of 3 s"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        // Act
        string json = MessageJson.Serialize("frameSync", seq: 1, frameSync);

        // Assert
        JsonNode payload = JsonNode.Parse(json)!["payload"]!;
        JsonNode.DeepEquals(payload, JsonNode.Parse(expectedPayload)).ShouldBeTrue(json);
    }

    [Fact]
    public void Serialize_DecodedFrame_MatchesExample()
    {
        // Arrange
        var decoded = new DecodedFrame(
            Frame: TestVectorFrame(minute: 30, startMicros: 1_000_000),
            Minute: 30,
            Hour: 19,
            DayOfYear: 344,
            Year: 26,
            Ut1Sign: Ut1Sign.Plus,
            Ut1Tenths: 0,
            LeapYear: false,
            LeapSecondPending: false,
            Dst: DstStatus.Standard,
            ErrorSeconds: []
        );

        // Act
        string json = MessageJson.Serialize("decodedFrame", seq: 46, decoded);

        // Assert
        JsonNode
            .DeepEquals(JsonNode.Parse(json), Examples.Read("decodedFrame"))
            .ShouldBeTrue(json);
    }

    [Fact]
    public void Serialize_FrameSync_FrameComplete_CarriesFrame()
    {
        // Arrange
        FrameSyncEvent frameSync = new FrameSyncEvent.FrameComplete(
            TestVectorFrame(minute: 30, startMicros: 1_000_000)
        );

        // Act
        string json = MessageJson.Serialize("frameSync", seq: 1, frameSync);

        // Assert
        JsonNode payload = JsonNode.Parse(json)!["payload"]!;
        payload["kind"]!.GetValue<string>().ShouldBe("frameComplete");
        payload["frame"]!["startMicros"]!.GetValue<ulong>().ShouldBe(1_000_000UL);
        payload["frame"]!["symbols"]!.AsArray().Count.ShouldBe(60);
    }

    [Fact]
    public void Serialize_Validation_MatchesExample()
    {
        // Arrange: the frame after the test vector, with second 6 flipped by noise, so it reads 19:35
        Frame transmitted = TestVectorFrame(minute: 31, startMicros: 61_000_000);
        var received = new Frame(
            transmitted.StartMicros,
            transmitted.EndMicros,
            transmitted.Symbols.SetItem(6, SymbolKind.One)
        );
        var decoded = new DecodedFrame(
            Frame: received,
            Minute: 35,
            Hour: 19,
            DayOfYear: 344,
            Year: 26,
            Ut1Sign: Ut1Sign.Plus,
            Ut1Tenths: 0,
            LeapYear: false,
            LeapSecondPending: false,
            Dst: DstStatus.Standard,
            ErrorSeconds: []
        );
        var validation = new ValidationResult(
            Frame: decoded,
            Accepted: false,
            Failures:
            [
                new ValidationFailure(
                    ValidationCheck.Consecutive,
                    "expected 2026-12-10T19:31Z, got 2026-12-10T19:35Z"
                ),
            ]
        );

        // Act
        string json = MessageJson.Serialize("validation", seq: 47, validation);

        // Assert
        JsonNode.DeepEquals(JsonNode.Parse(json), Examples.Read("validation")).ShouldBeTrue(json);
    }

    [Fact]
    public void Serialize_Clock_MatchesExample()
    {
        // Arrange
        var clock = new ClockState(
            Status: ClockStatus.Locked,
            FrameUtc: new DateTimeOffset(2026, 12, 10, 19, 30, 0, TimeSpan.Zero),
            FrameStartMicros: 1_000_000,
            FrameEndMicros: 60_800_000,
            OffsetFromPc: TimeSpan.FromMilliseconds(37),
            ConsecutiveAccepted: 3,
            RejectedSinceLastAccepted: 0
        );

        // Act
        string json = MessageJson.Serialize("clock", seq: 48, clock);

        // Assert
        JsonNode.DeepEquals(JsonNode.Parse(json), Examples.Read("clock")).ShouldBeTrue(json);
    }

    [Fact]
    public void Serialize_Clock_NoSignal_WritesExplicitNulls()
    {
        // Arrange
        var clock = new ClockState(ClockStatus.NoSignal, null, null, null, null, 0, 0);
        const string expectedPayload = """
            {
              "status": "noSignal",
              "frameUtc": null,
              "frameStartMicros": null,
              "frameEndMicros": null,
              "offsetFromPcMs": null,
              "consecutiveAccepted": 0,
              "rejectedSinceLastAccepted": 0
            }
            """;

        // Act
        string json = MessageJson.Serialize("clock", seq: 1, clock);

        // Assert
        JsonNode payload = JsonNode.Parse(json)!["payload"]!;
        JsonNode.DeepEquals(payload, JsonNode.Parse(expectedPayload)).ShouldBeTrue(json);
    }

    [Fact]
    public void Serialize_Clock_OffsetIsFractionalSignedMilliseconds()
    {
        // Arrange
        var clock = new ClockState(
            ClockStatus.Acquiring,
            null,
            null,
            null,
            TimeSpan.FromMilliseconds(-12.5),
            1,
            0
        );

        // Act
        string json = MessageJson.Serialize("clock", seq: 1, clock);

        // Assert
        JsonNode.Parse(json)!["payload"]!["offsetFromPcMs"]!
            .GetValue<double>()
            .ShouldBe(-12.5);
    }

    [Fact]
    public void Serialize_SourceStatus_MatchesExample()
    {
        // Arrange
        var status = new SourceStatus(
            Source: SourceKind.Serial,
            Name: "serial COM5",
            State: SourceState.Running,
            Detail: "# wwvb-edge-logger 0.1.0 pin=2"
        );

        // Act
        string json = MessageJson.Serialize("sourceStatus", seq: 3, status);

        // Assert
        JsonNode
            .DeepEquals(JsonNode.Parse(json), Examples.Read("sourceStatus"))
            .ShouldBeTrue(json);
    }

    [Fact]
    public void Serialize_Ports_MatchesExample()
    {
        // Arrange
        ItemsPayload<string> ports = new(["COM3", "COM5"]);

        // Act
        string json = MessageJson.Serialize("ports", seq: 4, ports);

        // Assert
        JsonNode.DeepEquals(JsonNode.Parse(json), Examples.Read("ports")).ShouldBeTrue(json);
    }

    [Fact]
    public void Serialize_Captures_MatchesExample()
    {
        // Arrange
        ItemsPayload<CaptureEntry> captures = new([
            new CaptureEntry(
                Path: "captures/2026-11-02T0310Z-home-west-window.edges.txt",
                Recorded: new DateTimeOffset(2026, 11, 2, 3, 10, 27, TimeSpan.Zero),
                Minutes: 487.5
            ),
        ]);

        // Act
        string json = MessageJson.Serialize("captures", seq: 5, captures);

        // Assert
        JsonNode.DeepEquals(JsonNode.Parse(json), Examples.Read("captures")).ShouldBeTrue(json);
    }

    [Fact]
    public void Serialize_Info_MatchesExample()
    {
        // Arrange
        var info = new InfoPayload("# wall 2026-11-02T03:11:00Z 14500398");

        // Act
        string json = MessageJson.Serialize("info", seq: 49, info);

        // Assert
        JsonNode.DeepEquals(JsonNode.Parse(json), Examples.Read("info")).ShouldBeTrue(json);
    }
}
