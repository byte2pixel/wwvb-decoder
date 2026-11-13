using System.Collections.Immutable;
using System.Globalization;

namespace WwvbDecoder.Core;

/// <summary>
/// Holds every well-formed <c># wall &lt;ISO-8601 UTC&gt; &lt;micros&gt;</c> line in a capture file.
/// </summary>
/// <param name="Utc">The UTC time of the sample.</param>
/// <param name="Micros">Microseconds of the last edge before the wall line.</param>
public readonly record struct WallSample(DateTimeOffset Utc, ulong Micros);

/// <summary>
/// Holds the information from a capture file.
/// </summary>
/// <param name="Recorded">The time the capture was recorded.</param>
/// <param name="Source">The source of the capture.</param>
/// <param name="ReducedCarrierLevel">The reduced carrier level.</param>
/// <param name="WallSamples">The wall samples.</param>
public sealed record CaptureInfo(
    DateTimeOffset Recorded,
    string Source,
    PinLevel? ReducedCarrierLevel,
    ImmutableArray<WallSample> WallSamples
)
{
    private const string RecordedPrefix = "# recorded ";
    private const string SourcePrefix = "# source ";
    private const string ReducedCarrierLevelPrefix = "# reduced-carrier-level ";
    private const string WallPrefix = "# wall ";

    /// <summary>
    /// Parses the capture file lines into a <see cref="CaptureInfo"/> object.
    /// </summary>
    /// <param name="lines">The lines of the capture file.</param>
    /// <returns>A <see cref="CaptureInfo"/> object representing the parsed capture file.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="lines"/> is null.</exception>
    public static CaptureInfo Parse(IReadOnlyList<string> lines)
    {
        DateTimeOffset? recorded = null;
        string? source = null;
        PinLevel? reducedCarrierLevel = null;
        List<WallSample> wallSamples = [];
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0 || lines[0] != "# capture v1")
        {
            throw new FormatException("The first line must be '# capture v1'.");
        }

        foreach (string line in lines)
        {
            switch (line)
            {
                case not null when line.StartsWith(RecordedPrefix, StringComparison.Ordinal):
                    recorded ??= ParseTimestamp(line[RecordedPrefix.Length..]);
                    continue;
                case not null when line.StartsWith(SourcePrefix, StringComparison.Ordinal):
                    source ??= line[SourcePrefix.Length..];
                    continue;
                case not null
                    when line.StartsWith(ReducedCarrierLevelPrefix, StringComparison.Ordinal):
                    reducedCarrierLevel = ParsePinLevel(line[ReducedCarrierLevelPrefix.Length..]);
                    continue;
                case not null when line.StartsWith(WallPrefix, StringComparison.Ordinal):
                    if (
                        TryParseWallSample(
                            line[WallPrefix.Length..].AsSpan(),
                            out WallSample sample
                        )
                    )
                    {
                        wallSamples.Add(sample);
                    }
                    continue;
            }
        }

        if (recorded == null)
        {
            throw new FormatException(
                "The capture file is missing a '# recorded <ISO-8601 UTC>' line."
            );
        }
        return new CaptureInfo(
            recorded.Value,
            source
                ?? throw new FormatException(
                    "The capture file is missing a '# source <source>' line."
                ),
            reducedCarrierLevel,
            [.. wallSamples]
        );
    }

    private static bool TryParseWallSample(ReadOnlySpan<char> wallSample, out WallSample sample)
    {
        sample = default;
        int spaceIndex = wallSample.IndexOf(' ');
        if (spaceIndex == -1)
        {
            return false;
        }

        string timestampPart = wallSample[..spaceIndex].ToString();
        string microsPart = wallSample[(spaceIndex + 1)..].ToString();

        if (
            !DateTimeOffset.TryParseExact(
                timestampPart,
                "yyyy-MM-ddTHH:mm:ssZ",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset timestamp
            )
            || !ulong.TryParse(
                microsPart,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out ulong micros
            )
        )
        {
            return false;
        }

        sample = new WallSample(timestamp, micros);
        return true;
    }

    private static PinLevel ParsePinLevel(string reducedCarrierLevel)
    {
        return reducedCarrierLevel switch
        {
            "H" => PinLevel.High,
            "L" => PinLevel.Low,
            _ => throw new FormatException($"Invalid pin level: {reducedCarrierLevel}"),
        };
    }

    private static DateTimeOffset ParseTimestamp(string timestamp)
    {
        return DateTimeOffset.TryParseExact(
            timestamp,
            "yyyy-MM-ddTHH:mm:ssZ",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset parsed
        )
            ? parsed
            : throw new FormatException($"Invalid timestamp: {timestamp}");
    }
};
