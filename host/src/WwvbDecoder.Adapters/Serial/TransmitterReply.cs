namespace WwvbDecoder.Adapters;

/// <summary>
/// The transmitter's answer to one command.
/// </summary>
/// <param name="Line">The reply line as the transmitter wrote it.</param>
public sealed record TransmitterReply(string Line)
{
    private const string OkStr = "OK";

    /// <summary>
    /// Whether the command succeeded: the first word of the line is exactly <c>OK</c>.
    /// </summary>
    public bool Ok => Line == OkStr || Line.StartsWith(OkStr + " ", StringComparison.Ordinal);
}
