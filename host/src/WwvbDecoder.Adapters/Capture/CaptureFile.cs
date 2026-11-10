using System.Text;

namespace WwvbDecoder.Adapters;

/// <summary>
/// Handles the capture file.
/// </summary>
public sealed class CaptureFile
{
    /// <summary>
    /// The whole file in memory. Split on `\n' only and drop a final empty line if present.
    /// </summary>
    /// <param name="path">The path to the capture file.</param>
    /// <returns>The lines of the capture file.</returns>
    public static IReadOnlyList<string> Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var lines = File.ReadAllText(path, Encoding.UTF8).Split('\n');
        if (lines.Length > 0 && lines[^1] == "")
        {
            lines = lines[..^1];
        }
        return lines;
    }

    /// <summary>
    /// Creates a new capture file for writing.
    /// </summary>
    /// <param name="path">The path to the capture file.</param>
    /// <returns>A <see cref="TextWriter"/> for writing to the capture file.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="path"/> is null.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown if the caller does not have the required permission.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown if the specified path is invalid.</exception>
    /// <exception cref="IOException">Thrown if the file already exists.</exception>
    public static TextWriter Create(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var fileStream = File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        return new StreamWriter(fileStream, new UTF8Encoding(false)) { NewLine = "\n" };
    }

    /// <summary>
    /// Creates a file name for the capture file.
    /// </summary>
    /// <param name="recordedUtc">The time the capture was recorded, in UTC.</param>
    /// <param name="label">The label for the capture file.</param>
    /// <returns>The file name for the capture file.</returns>
    public static string FileName(DateTimeOffset recordedUtc, string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (label.Length == 0)
        {
            throw new ArgumentException("Label cannot be empty.", nameof(label));
        }

        var utc = recordedUtc.ToUniversalTime();

        return !label.All(c =>
            (char.IsAsciiLetter(c) && char.IsLower(c)) || char.IsAsciiDigit(c) || c == '-'
        )
            ? throw new ArgumentException("Label contains invalid characters.", nameof(label))
            : $"{utc:yyyy-MM-ddTHHmmZ}-{label}.edges.txt";
    }
}
