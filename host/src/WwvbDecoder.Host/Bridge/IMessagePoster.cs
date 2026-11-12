namespace WwvbDecoder.Host.Bridge;

/// <summary>
/// What the controller needs from the bridge.
/// </summary>
public interface IMessagePoster
{
    /// <summary>Thread-safe. The bridge assigns seq and marshals to the dispatcher.</summary>
    /// <typeparam name="TPayload">The type of the payload.</typeparam>
    /// <param name="type">The type of the message.</param>
    /// <param name="payload">The payload of the message.</param>
    void Post<TPayload>(string type, TPayload payload);
}
