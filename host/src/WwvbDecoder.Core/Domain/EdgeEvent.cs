namespace WwvbDecoder.Core;

/// <summary>
/// Represents an event related to an edge, which can be a sample, a reset, or an informational message.
/// </summary>
public abstract record EdgeEvent
{
    private EdgeEvent() { }

    /// <summary>
    /// Represents a sample event with the associated edge.
    /// </summary>
    public sealed record Sample(Edge Edge) : EdgeEvent;

    /// <summary>
    /// Represents a reset event with the reason for the reset.
    /// </summary>
    public sealed record Reset(string Reason) : EdgeEvent;

    /// <summary>
    /// Represents an informational event with a message line.
    /// </summary>
    public sealed record Info(string Line) : EdgeEvent;
}
