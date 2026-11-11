using WwvbDecoder.Host.Bridge;

namespace WwvbDecoder.Host.Tests.Bridge;

public class CommandParsingTests
{
    [Fact]
    public void TryParse_Hello_CarriesProtocol()
    {
        CommandJson
            .TryParse(Examples.ReadText("command-hello"), out Command? command)
            .ShouldBeTrue();
        command.ShouldBeOfType<Command.Hello>().Protocol.ShouldBe(1);
    }

    [Fact]
    public void TryParse_SelectSourceSerial_CarriesPort()
    {
        CommandJson
            .TryParse(Examples.ReadText("command-selectSource"), out Command? command)
            .ShouldBeTrue();
        command.ShouldBeOfType<Command.SelectSerial>().Port.ShouldBe("COM5");
    }

    [Fact]
    public void TryParse_SelectSourceReplay_CarriesPathAndSpeed()
    {
        const string json =
            """{ "command": "selectSource", "source": "replay", "path": "captures/a.edges.txt", "speed": 1 }""";
        CommandJson.TryParse(json, out Command? command).ShouldBeTrue();
        var replay = command.ShouldBeOfType<Command.SelectReplay>();
        replay.Path.ShouldBe("captures/a.edges.txt");
        replay.Speed.ShouldBe(1);
    }

    [Fact]
    public void TryParse_SelectSourceSynthetic_CarriesNoise()
    {
        const string json =
            """{ "command": "selectSource", "source": "synthetic", "noise": 0.0 }""";
        CommandJson.TryParse(json, out Command? command).ShouldBeTrue();
        command.ShouldBeOfType<Command.SelectSynthetic>().Noise.ShouldBe(0);
    }

    [Fact]
    public void TryParse_SetReplaySpeed_CarriesSpeed()
    {
        CommandJson
            .TryParse(Examples.ReadText("command-setReplaySpeed"), out Command? command)
            .ShouldBeTrue();
        command.ShouldBeOfType<Command.SetReplaySpeed>().Speed.ShouldBe(10);
    }

    [Fact]
    public void TryParse_SetNoise_CarriesNoise()
    {
        CommandJson
            .TryParse(Examples.ReadText("command-setNoise"), out Command? command)
            .ShouldBeTrue();
        command.ShouldBeOfType<Command.SetNoise>().Noise.ShouldBe(0.2);
    }

    [Fact]
    public void TryParse_SetReducedCarrierLevel_CarriesPinLevel()
    {
        CommandJson
            .TryParse(Examples.ReadText("command-setReducedCarrierLevel"), out Command? command)
            .ShouldBeTrue();
        command.ShouldBeOfType<Command.SetReducedCarrierLevel>().Level.ShouldBe(PinLevel.High);
    }

    [Theory]
    [InlineData("command-listPorts", typeof(Command.ListPorts))]
    [InlineData("command-listCaptures", typeof(Command.ListCaptures))]
    [InlineData("command-stop", typeof(Command.Stop))]
    public void TryParse_ParameterlessCommand_ReturnsItsCase(string example, Type expected)
    {
        CommandJson.TryParse(Examples.ReadText(example), out Command? command).ShouldBeTrue();
        command.ShouldBeOfType(expected);
    }

    [Theory]
    [InlineData("""{ "command": "reboot" }""")]
    [InlineData("""{ "command": "selectSource", "source": "tape" }""")]
    [InlineData("""{ "command": "setNoise" }""")]
    [InlineData("""{ "command": "hello", "protocol": "one" }""")]
    [InlineData("""{ "command": "setReducedCarrierLevel", "level": "up" }""")]
    [InlineData("""{ "protocol": 1 }""")]
    [InlineData("""[1, 2]""")]
    [InlineData("not json")]
    public void TryParse_UnknownOrMalformed_ReturnsFalse(string json)
    {
        CommandJson.TryParse(json, out Command? command).ShouldBeFalse();
        command.ShouldBeNull();
    }
}
