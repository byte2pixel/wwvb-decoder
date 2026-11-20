using Microsoft.Extensions.Time.Testing;
using WwvbDecoder.Host.Bridge;
using WwvbDecoder.Host.Services;

namespace WwvbDecoder.Host.Tests.Services;

internal sealed class FakeSourceEnvironment : ISourceEnvironment
{
    public FakeTimeProvider Time { get; } =
        new(new DateTimeOffset(2026, 12, 10, 19, 30, 0, TimeSpan.Zero));

    public IReadOnlyList<string> Ports { get; set; } = ["COM3", "COM5"];

    public IReadOnlyList<CaptureEntry> Captures { get; set; } =
    [
        new CaptureEntry(
            "captures/a.edges.txt",
            new DateTimeOffset(2026, 11, 2, 3, 10, 27, TimeSpan.Zero),
            487.5
        ),
    ];

    public List<IEdgeSource> Created { get; } = [];

    /// <summary>Swap in a throwing or completing source for the run-ending tests.</summary>
    public Func<double, IEdgeSource> Synthetic { get; set; }

    /// <summary>The paths asked for, in order.</summary>
    public List<string> ReplayPaths { get; } = [];

    /// <summary>Swap in a throwing factory for the cannot-read tests.</summary>
    public Func<string, double, IEdgeSource> Replay { get; set; }

    /// <summary>The ports asked for, in order.</summary>
    public List<string> SerialPorts { get; } = [];

    /// <summary>Swap in a faulting or scripted source for the serial tests.</summary>
    public Func<string, IEdgeSource> Serial { get; set; }

    private static readonly string[] Capture =
    [
        "# capture v1",
        "# recorded 2026-11-02T03:10:27Z",
        "# source test",
        "H 1000000",
        "L 1200000",
        "H 2000000",
    ];

    public FakeSourceEnvironment()
    {
        Synthetic = noise => new SyntheticEdgeSource(Time)
        {
            StartUtc = Time.GetUtcNow(),
            Noise = noise,
        };
        Replay = (_, speed) => new ReplayEdgeSource(Capture, Time) { Speed = speed, Loop = true };
        Serial = port => new FakeEdgeSource { Name = $"serial {port}" };
    }

    public IReadOnlyList<string> ListPorts() => Ports;

    public IReadOnlyList<CaptureEntry> ListCaptures() => Captures;

    public IEdgeSource CreateSynthetic(double noise)
    {
        IEdgeSource source = Synthetic(noise);
        Created.Add(source);
        return source;
    }

    public IEdgeSource CreateReplay(string path, double speed)
    {
        ReplayPaths.Add(path);
        IEdgeSource source = Replay(path, speed);
        Created.Add(source);
        return source;
    }

    public IEdgeSource CreateSerial(string port)
    {
        SerialPorts.Add(port);
        IEdgeSource source = Serial(port);
        Created.Add(source);
        return source;
    }
}
