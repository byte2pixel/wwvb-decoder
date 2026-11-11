using System.Collections.Immutable;
using WwvbDecoder.Core;

namespace WwvbDecoder.Host.Bridge;

/// <summary>
/// The host-to-page half of the bridge: turns every <see cref="DecoderPipeline"/> event into one
/// web message, batching edges, and numbers every message with a shared <c>seq</c>.
/// </summary>
/// <remarks>
/// Pipeline events arrive on the pump thread and the batch timer fires on a thread-pool thread.
/// Both only serialize and queue; the <c>invokeOnUi</c> delegate marshals the post to the
/// dispatcher, where <c>CoreWebView2.PostWebMessageAsJson</c> must run. The sequence number is
/// taken under the same lock that queues the message, and the dispatcher queue is first in,
/// first out, so <c>seq</c> order is posting order.
/// </remarks>
public sealed class WebMessageBridge : IMessagePoster, IDisposable
{
    private const int EdgeBatchSize = 200;
    private static readonly TimeSpan EdgeBatchInterval = TimeSpan.FromMilliseconds(100);

    private readonly DecoderPipeline _pipeline;
    private readonly Action<Action> _invokeOnUi;
    private readonly Action<string> _postJson;
    private readonly ITimer _edgeTimer;

    private readonly Lock _postLock = new();
    private int _sequence;

    private readonly Lock _edgeLock = new();
    private readonly List<Edge> _edgeBatch = [];

    private bool _disposed;

    /// <summary>
    /// Subscribes to the pipeline and starts the edge-batch timer.
    /// </summary>
    /// <param name="pipeline">The pipeline whose events become messages.</param>
    /// <param name="time">Drives the 100 ms edge-batch timer.</param>
    /// <param name="invokeOnUi">Runs an action on the dispatcher thread; may return before it runs.</param>
    /// <param name="postJson">Posts one serialized message to the page. Called only through <paramref name="invokeOnUi"/>.</param>
    public WebMessageBridge(
        DecoderPipeline pipeline,
        TimeProvider time,
        Action<Action> invokeOnUi,
        Action<string> postJson
    )
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(time);
        _pipeline = pipeline;
        _invokeOnUi = invokeOnUi;
        _postJson = postJson;

        _pipeline.EdgeReceived += OnEdge;
        _pipeline.PulseMeasured += OnPulse;
        _pipeline.SymbolClassified += OnSymbol;
        _pipeline.FrameSyncChanged += OnFrameSync;
        _pipeline.FrameDecoded += OnFrameDecoded;
        _pipeline.FrameValidated += OnFrameValidated;
        _pipeline.ClockUpdated += OnClock;
        _pipeline.InfoReceived += OnInfo;

        _edgeTimer = time.CreateTimer(
            _ => FlushEdges(),
            null,
            EdgeBatchInterval,
            EdgeBatchInterval
        );
    }

    /// <inheritdoc/>
    public void Post<TPayload>(string type, TPayload payload)
    {
        lock (_postLock)
        {
            if (_disposed)
            {
                return;
            }

            int seq = ++_sequence;
            string json = MessageJson.Serialize(type, seq, payload);
            _invokeOnUi(() => _postJson(json));
        }
    }

    /// <summary>
    /// Stops the timer and unsubscribes from the pipeline. Edges still in the batch are dropped.
    /// </summary>
    public void Dispose()
    {
        lock (_postLock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }

        _edgeTimer.Dispose();
        _pipeline.EdgeReceived -= OnEdge;
        _pipeline.PulseMeasured -= OnPulse;
        _pipeline.SymbolClassified -= OnSymbol;
        _pipeline.FrameSyncChanged -= OnFrameSync;
        _pipeline.FrameDecoded -= OnFrameDecoded;
        _pipeline.FrameValidated -= OnFrameValidated;
        _pipeline.ClockUpdated -= OnClock;
        _pipeline.InfoReceived -= OnInfo;
    }

    private void OnEdge(Edge edge)
    {
        lock (_edgeLock)
        {
            _edgeBatch.Add(edge);
            if (_edgeBatch.Count < EdgeBatchSize)
            {
                return;
            }
        }
        FlushEdges();
    }

    /// <summary>Posts the pending edges as one <c>edges</c> message. Never posts an empty batch.</summary>
    private void FlushEdges()
    {
        ImmutableArray<Edge> batch;
        lock (_edgeLock)
        {
            if (_edgeBatch.Count == 0)
            {
                return;
            }
            batch = [.. _edgeBatch];
            _edgeBatch.Clear();
        }
        Post("edges", new ItemsPayload<Edge>(batch));
    }

    private void OnPulse(Pulse pulse) => Post("pulse", pulse);

    private void OnSymbol(Symbol symbol) => Post("symbol", symbol);

    private void OnFrameSync(FrameSyncEvent frameSync) => Post("frameSync", frameSync);

    private void OnFrameDecoded(DecodedFrame frame) => Post("decodedFrame", frame);

    private void OnFrameValidated(ValidationResult validation) => Post("validation", validation);

    private void OnClock(ClockState clock) => Post("clock", clock);

    private void OnInfo(string line) => Post("info", new InfoPayload(line));
}
