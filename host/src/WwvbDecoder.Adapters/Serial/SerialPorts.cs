namespace WwvbDecoder.Adapters;

/// <summary>
/// Provides access to serial ports.
/// </summary>
public static class SerialPorts
{
    /// <summary>
    /// Gets the names of all available serial ports on the system
    /// </summary>
    /// <returns>
    /// A read-only list of the names of all available serial ports on the system.
    /// </returns>
    public static IReadOnlyList<string> ListPortNames()
    {
        return
        [
            .. System
                .IO.Ports.SerialPort.GetPortNames()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
        ];
    }
}
