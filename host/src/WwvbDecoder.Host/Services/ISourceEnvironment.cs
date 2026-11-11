using WwvbDecoder.Core;
using WwvbDecoder.Host.Bridge;

namespace WwvbDecoder.Host.Services;

/// <summary>
/// What the controller needs from the machine.
/// </summary>
public interface ISourceEnvironment
{
    /// <summary>
    /// Lists the available serial ports.
    /// </summary>
    /// <returns>A read-only list of port names.</returns>
    IReadOnlyList<string> ListPorts();

    /// <summary>
    /// Lists the available capture files.
    /// </summary>
    /// <returns>A read-only list of capture entries.</returns>
    IReadOnlyList<CaptureEntry> ListCaptures();

    /// <summary>A synthetic source starting at the current UTC minute.</summary>
    /// <param name="noise">The amount of noise to apply.</param>
    /// <returns>An edge source.</returns>
    IEdgeSource CreateSynthetic(double noise);
}
