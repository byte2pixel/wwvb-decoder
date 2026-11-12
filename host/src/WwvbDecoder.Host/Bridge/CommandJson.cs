using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using WwvbDecoder.Core;

namespace WwvbDecoder.Host.Bridge;

/// <summary>
/// Provides methods for parsing commands from JSON format.
/// </summary>
public static class CommandJson
{
    /// <summary>
    /// Parses one command from the page. False for malformed JSON, an unknown command, a missing
    /// or mistyped argument; the caller logs the raw text and moves on, as the contract requires.
    /// </summary>
    public static bool TryParse(string json, [NotNullWhen(true)] out Command? command)
    {
        command = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            command = root.GetProperty("command").GetString() switch
            {
                "hello" => new Command.Hello(root.GetProperty("protocol").GetInt32()),
                "selectSource" => ParseSelectSource(root),
                "setReplaySpeed" => new Command.SetReplaySpeed(
                    root.GetProperty("speed").GetDouble()
                ),
                "setNoise" => new Command.SetNoise(root.GetProperty("noise").GetDouble()),
                "setReducedCarrierLevel" => new Command.SetReducedCarrierLevel(
                    ParsePinLevel(root.GetProperty("level").GetString())
                ),
                "listPorts" => new Command.ListPorts(),
                "listCaptures" => new Command.ListCaptures(),
                "stop" => new Command.Stop(),
                _ => null,
            };
            return command is not null;
        }
        catch (Exception e)
            when (e
                    is JsonException
                        or KeyNotFoundException
                        or InvalidOperationException
                        or FormatException
            )
        {
            command = null;
            return false;
        }
    }

    private static Command? ParseSelectSource(JsonElement root) =>
        root.GetProperty("source").GetString() switch
        {
            "serial" => new Command.SelectSerial(
                root.GetProperty("port").GetString() ?? throw new FormatException("port")
            ),
            "replay" => new Command.SelectReplay(
                root.GetProperty("path").GetString() ?? throw new FormatException("path"),
                root.GetProperty("speed").GetDouble()
            ),
            "synthetic" => new Command.SelectSynthetic(root.GetProperty("noise").GetDouble()),
            _ => null,
        };

    private static PinLevel ParsePinLevel(string? level) =>
        level switch
        {
            "low" => PinLevel.Low,
            "high" => PinLevel.High,
            _ => throw new FormatException($"Unknown pin level '{level}'."),
        };
}
