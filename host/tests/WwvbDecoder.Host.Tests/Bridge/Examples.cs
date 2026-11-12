using System.IO;
using System.Text.Json.Nodes;

namespace WwvbDecoder.Host.Tests.Bridge;

internal static class Examples
{
    public static JsonNode Read(string name) =>
        JsonNode.Parse(ReadText(name))
        ?? throw new InvalidOperationException($"Failed to read example: {name}");

    public static string ReadText(string name) =>
        File.ReadAllText(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "examples", name + ".json")
        );
}
