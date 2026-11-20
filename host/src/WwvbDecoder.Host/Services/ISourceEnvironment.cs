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

    /// <summary>A replay of a capture from <see cref="ListCaptures"/>, looping at its end.</summary>
    /// <param name="path">The capture's path, as <see cref="ListCaptures"/> gave it.</param>
    /// <param name="speed">0 for as fast as possible, otherwise a multiple of real time.</param>
    /// <returns>An edge source.</returns>
    IEdgeSource CreateReplay(string path, double speed);

    /// <summary>The edge logger on a serial port. The port opens when the source is read.</summary>
    /// <param name="port">A port name from <see cref="ListPorts"/>.</param>
    /// <returns>An edge source.</returns>
    IEdgeSource CreateSerial(string port);
}
