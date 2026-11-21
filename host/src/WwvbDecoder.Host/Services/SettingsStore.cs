using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WwvbDecoder.Host.Bridge;

namespace WwvbDecoder.Host.Services;

/// <inheritdoc/>
public sealed class SettingsStore(string path) : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
        },
    };

    /// <inheritdoc/>
    public HostSettings Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return HostSettings.Default;
            }

            string json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<HostSettings>(json, JsonOptions);
            if (settings == null)
            {
                return HostSettings.Default;
            }
            bool valid =
                settings.Source != SourceKind.None
                && double.IsFinite(settings.Speed)
                && settings is { Speed: >= 0, Noise: >= 0 and <= 1 };
            return !valid ? HostSettings.Default : settings;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.TraceWarning("Settings in {0} cannot be read: {1}", path, e.Message);
            return HostSettings.Default;
        }
    }

    /// <inheritdoc/>
    public void Save(HostSettings settings)
    {
        try
        {
            string tempPath = path + ".tmp";
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Settings in {0} cannot be written: {1}", path, e.Message);
        }
    }

    /// <summary>
    /// Finds the path to the settings file from <c>--settings</c>.
    /// </summary>
    /// <param name="args">The command-line arguments</param>
    /// <returns>The path to the settings file.</returns>
    public static string FindPath(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (
                args[i].Equals("--settings", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Count
            )
            {
                return Path.GetFullPath(args[i + 1]);
            }
        }
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "WwvbDecoder", "settings.json");
    }
}
