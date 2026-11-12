using WwvbDecoder.Core;

namespace WwvbDecoder.Host.Bridge;

/// <summary>
/// Commands that can be sent to the host from the web page. These are parsed from JSON and dispatched to the host.
/// </summary>
public abstract record Command
{
    private Command() { }

    /// <summary>
    /// "hello" command.
    /// </summary>
    /// <param name="Protocol">the protocol version number.</param>
    public sealed record Hello(int Protocol) : Command;

    /// <summary>
    /// Command for selecting a serial source.
    /// </summary>
    /// <param name="Port">The serial port name.</param>
    public sealed record SelectSerial(string Port) : Command;

    /// <summary>
    /// Command for selecting a replay source.
    /// </summary>
    /// <param name="Path">The path to the replay file.</param>
    /// <param name="Speed">The speed multiplier for replaying the source.</param>
    public sealed record SelectReplay(string Path, double Speed) : Command;

    /// <summary>
    /// Command for selecting a synthetic source.
    /// </summary>
    /// <param name="Noise">the noise level for the synthetic source.</param>
    public sealed record SelectSynthetic(double Noise) : Command;

    /// <summary>
    /// Command for setting the replay speed.
    /// </summary>
    /// <param name="Speed">The speed multiplier for replaying the source.</param>
    public sealed record SetReplaySpeed(double Speed) : Command;

    /// <summary>
    /// Command for setting the noise level.
    /// </summary>
    /// <param name="Noise">The noise level.</param>
    public sealed record SetNoise(double Noise) : Command;

    /// <summary>
    /// Command for setting the reduced carrier level.
    /// </summary>
    /// <param name="Level">The pin level.</param>
    public sealed record SetReducedCarrierLevel(PinLevel Level) : Command;

    /// <summary>
    /// Command for listing available ports.
    /// </summary>
    public sealed record ListPorts : Command;

    /// <summary>
    /// Command for listing available captures.
    /// </summary>
    public sealed record ListCaptures : Command;

    /// <summary>
    /// Command for stopping the current operation.
    /// </summary>
    public sealed record Stop : Command;
}
