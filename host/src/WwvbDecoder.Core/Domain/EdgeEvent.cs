namespace WwvbDecoder.Core;

/// <summary>
/// What a source yields.
/// </summary>
public abstract record EdgeEvent
{
    private EdgeEvent() { }

    /// <summary>
    /// One edge.
    /// </summary>
    public sealed record Sample(Edge Edge) : EdgeEvent;

    /// <summary>
    /// Stages must discard their state. The reasons are provided in the <see cref="Reason"/> property.
    /// <c>start</c>, <c>open</c>, <c>session</c>, <c>loop</c>, and <c>timestamp went backwards</c>.
    /// </summary>
    public sealed record Reset(string Reason) : EdgeEvent;

    /// <summary>
    /// The whole <c>#</c> line verbatim.
    /// </summary>
    public sealed record Info(string Line) : EdgeEvent;
}
