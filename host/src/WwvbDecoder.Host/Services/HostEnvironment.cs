using System.IO;
using WwvbDecoder.Core;
using WwvbDecoder.Host.Bridge;

namespace WwvbDecoder.Host.Services;

/// <summary>
/// The real machine behind <see cref="ISourceEnvironment"/>: serial ports, the captures
/// directory, and a synthetic source on the given clock.
/// </summary>
/// <param name="timeProvider">The clock the synthetic source starts from and paces on.</param>
/// <param name="capturesDirectory">Where <c>*.edges.txt</c> files live. May not exist yet.</param>
public sealed class HostEnvironment(TimeProvider timeProvider, string capturesDirectory)
    : ISourceEnvironment
{
    private const string CapturesFolderName = "captures";
    private const string CapturesOption = "--captures";

    /// <inheritdoc/>
    public IReadOnlyList<string> ListPorts() => Adapters.SerialPorts.ListPortNames();

    /// <inheritdoc/>
    public IReadOnlyList<CaptureEntry> ListCaptures() =>
        Directory.Exists(capturesDirectory)
            ?
            [
                .. Adapters
                    .CaptureCatalog.List(capturesDirectory)
                    .Select(e => new CaptureEntry(e.Path, e.Recorded, e.Duration.TotalMinutes)),
            ]
            : [];

    /// <inheritdoc/>
    public IEdgeSource CreateSynthetic(double noise)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        long minuteTicks = now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMinute;
        return new SyntheticEdgeSource(timeProvider)
        {
            StartUtc = new DateTimeOffset(minuteTicks, TimeSpan.Zero),
            Noise = noise,
        };
    }

    /// <inheritdoc/>
    public IEdgeSource CreateReplay(string path, double speed)
    {
        string directory = Path.GetFullPath(capturesDirectory) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(directory, StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("Not a capture in the captures directory.", path);
        }

        return new ReplayEdgeSource(Adapters.CaptureFile.Open(path), timeProvider)
        {
            Speed = speed,
            Loop = true,
        };
    }

    /// <inheritdoc/>
    public IEdgeSource CreateSerial(string port) => new Adapters.SerialEdgeSource(port);

    /// <summary>
    /// The captures directory for a command line: the one named after <c>--captures</c> when
    /// that is given, and otherwise the one <see cref="FindCapturesDirectory()"/> finds.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    public static string FindCapturesDirectory(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], CapturesOption, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(args[i + 1]);
            }
        }
        return FindCapturesDirectory();
    }

    /// <summary>
    /// The nearest <c>captures</c> directory at or above the executable: the repo root under
    /// F5, the install folder when published. Falls back to <c>captures</c> beside the
    /// executable when none exists, which lists as empty.
    /// </summary>
    public static string FindCapturesDirectory()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, CapturesFolderName);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        return Path.Combine(AppContext.BaseDirectory, CapturesFolderName);
    }
}
