namespace WwvbDecoder.Core;

public abstract record EdgeEvent
{
    private EdgeEvent() { }

    public sealed record Sample(Edge Edge) : EdgeEvent;

    public sealed record Reset(string Reason) : EdgeEvent;

    public sealed record Info(string Line) : EdgeEvent;
}
