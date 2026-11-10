namespace WwvbDecoder.Adapters;

/// <summary>
/// Represents a single capture file, including its path, recorded time, and duration.
/// </summary>
/// <param name="Path">The file path of the capture.</param>
/// <param name="Recorded">The time the capture was recorded.</param>
/// <param name="Duration">The duration of the capture.</param>
public sealed record CaptureEntry(string Path, DateTimeOffset Recorded, TimeSpan Duration);

/// <summary>
/// Represents a collection of capture files in a directory.
/// </summary>
public static class CaptureCatalog
{
    /// <summary>
    /// Returns a list of captures in the given directory, sorted latest recorded first.
    /// </summary>
    /// <param name="directory">The directory to search for captures.</param>
    /// <returns>
    /// A list of captures, sorted by recorded time in descending order.
    /// </returns>
    public static IReadOnlyList<CaptureEntry> List(string directory)
    {
        List<CaptureEntry> entries = [];
        foreach (string path in Directory.EnumerateFiles(directory, "*.edges.txt"))
        {
            var lines = CaptureFile.Open(path);
            try
            {
                var info = CaptureInfo.Parse(lines);

                TimeSpan? firstEdgeDuration = null;
                TimeSpan? lastEdgeDuration = null;
                foreach (var line in lines)
                {
                    if (
                        EdgeLineCodec.TryParse(line, out var edge)
                        && edge is EdgeEvent.Sample sample
                    )
                    {
                        firstEdgeDuration ??= TimeSpan.FromMicroseconds(sample.Edge.Micros);
                        lastEdgeDuration = TimeSpan.FromMicroseconds(sample.Edge.Micros);
                    }
                }
                TimeSpan duration = (lastEdgeDuration - firstEdgeDuration) ?? TimeSpan.Zero;
                entries.Add(new CaptureEntry(path, info.Recorded, duration));
            }
            catch (FormatException)
            {
                // Ignore files that don't parse correctly.
            }
        }

        var query = entries.OrderByDescending(e => e.Recorded);
        return [.. query];
    }
}
