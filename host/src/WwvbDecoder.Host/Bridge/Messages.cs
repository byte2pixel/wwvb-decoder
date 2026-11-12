using System.Collections.Immutable;

namespace WwvbDecoder.Host.Bridge;

/// <summary>
/// Represents a message envelope that contains a type, sequence number, and payload.
/// The bridge from the host to the client.
/// </summary>
/// <param name="Type">The type of the message.</param>
/// <param name="Seq">The sequence number of the message.</param>
/// <param name="Payload">The payload of the message.</param>
/// <typeparam name="T">The type of the payload.</typeparam>
public sealed record Envelope<T>(string Type, int Seq, T Payload);

/// <summary>
/// Represents a payload that contains an immutable array of items.
/// </summary>
/// <param name="Items">The immutable array of items.</param>
/// <typeparam name="T">The type of the items.</typeparam>
public sealed record ItemsPayload<T>(ImmutableArray<T> Items);

/// <summary>Which kind of source is selected. <see cref="None"/> before the first selection and after stop.</summary>
public enum SourceKind
{
    /// <summary>Live capture from a serial port.</summary>
    Serial,

    /// <summary>Replay from a previously recorded source.</summary>
    Replay,

    /// <summary>Synthetic source generated programmatically.</summary>
    Synthetic,

    /// <summary>No source selected.</summary>
    None,
}

/// <summary>Whether the pipeline is pumping, idle, or failed to start.</summary>
public enum SourceState
{
    /// <summary>The pipeline is stopped.</summary>
    Stopped,

    /// <summary>The pipeline is running.</summary>
    Running,

    /// <summary>The pipeline encountered an error.</summary>
    Error,
}

/// <summary>Posted whenever the source changes state. <paramref name="Detail"/> is display text.</summary>
public sealed record SourceStatus(SourceKind Source, string Name, SourceState State, string Detail);

/// <summary>One capture file. <paramref name="Path"/> is what <c>selectSource</c> takes back.</summary>
public sealed record CaptureEntry(string Path, DateTimeOffset Recorded, double Minutes);

/// <summary>A <c>#</c> line from the source, verbatim.</summary>
public sealed record InfoPayload(string Line);
