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

    public FakeSourceEnvironment() =>
        Synthetic = noise => new SyntheticEdgeSource(Time)
        {
            StartUtc = Time.GetUtcNow(),
            Noise = noise,
        };

    public IReadOnlyList<string> ListPorts() => Ports;

    public IReadOnlyList<CaptureEntry> ListCaptures() => Captures;

    public IEdgeSource CreateSynthetic(double noise)
    {
        IEdgeSource source = Synthetic(noise);
        Created.Add(source);
        return source;
    }
}
