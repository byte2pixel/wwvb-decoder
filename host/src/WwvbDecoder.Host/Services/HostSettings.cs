using WwvbDecoder.Core;
using WwvbDecoder.Host.Bridge;

namespace WwvbDecoder.Host.Services;

/// <summary>
/// The settings of the host.
/// </summary>
/// <param name="Source">The source kind.</param>
/// <param name="Port">The port.</param>
/// <param name="Capture">The capture filename.</param>
/// <param name="Speed">The speed.</param>
/// <param name="Noise">The noise.</param>
/// <param name="ReducedCarrierLevel">The reduced carrier level.</param>
/// <param name="Running">Whether the host is running.</param>
public sealed record HostSettings(
    SourceKind Source,
    string? Port,
    string? Capture,
    double Speed,
    double Noise,
    PinLevel ReducedCarrierLevel,
    bool Running
)
{
    /// <summary>
    /// The default settings.
    /// </summary>
    public static HostSettings Default =>
        new(SourceKind.Synthetic, null, null, 1.0, 0.0, PinLevel.High, false);
}
