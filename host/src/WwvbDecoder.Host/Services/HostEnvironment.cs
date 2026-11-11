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
