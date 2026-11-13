namespace WwvbDecoder.Core;

/// <summary>
/// Stage 7: Pipeline handles the stages and events.
/// </summary>
/// <param name="timeProvider"></param>
public sealed class DecoderPipeline(TimeProvider timeProvider)
{
    private int _running;

    // Stages stage 1 is the fw capture.
    /// <summary>
    /// Stage 2: Pulse measurement stage.
    /// </summary>
    public PulseMeasurement PulseMeasurement { get; } = new();

    /// <summary>
    /// Stage 3: Symbol classification stage.
    /// </summary>
    public SymbolClassifier SymbolClassifier { get; } = new();

    /// <summary>
    /// Stage 4: Frame synchronization stage.
    /// </summary>
    public FrameSync FrameSync { get; } = new();

    /// <summary>
    /// Stage 5: Field decoding stage.
    /// </summary>
    public FieldDecoder FieldDecoder { get; } = new();

    /// <summary>
    /// Stage 6: Frame validation stage.
    /// </summary>
    public FrameValidator FrameValidator { get; } = new();

    /// <summary>
    /// Stage 7: WWVB clock stage.
    /// </summary>
    public WwvbClock Clock { get; } = new(timeProvider);

    // Events
    /// <summary>
    /// Event raised when an Edge is received from the source.
    /// </summary>
    public event Action<Edge>? EdgeReceived;

    /// <summary>
    /// Event raised when a Pulse is measured from the Edge.
    /// </summary>
    public event Action<Pulse>? PulseMeasured;

    /// <summary>
    /// Event raised when a Symbol is classified from the Pulse.
    /// </summary>
    public event Action<Symbol>? SymbolClassified;

    /// <summary>
    /// Event raised when a FrameSync is detected from the Symbols.
    /// </summary>
    public event Action<FrameSyncEvent>? FrameSyncChanged;

    /// <summary>
    /// Event raised when a Frame is decoded from the FrameSync.
    /// </summary>
    public event Action<DecodedFrame>? FrameDecoded;

    /// <summary>
    /// Event raised when a Frame is validated from the DecodedFrame.
    /// </summary>
    public event Action<ValidationResult>? FrameValidated;

    /// <summary>
    /// Event raised when the WWVB clock is updated from the ValidationResult.
    /// </summary>
    public event Action<ClockState>? ClockUpdated;

    /// <summary>
    /// Event raised when a Reset event is received from the source.
    /// </summary>
    public event Action<string>? SourceReset; // The Reset reason

    /// <summary>
    /// Event raised when an Info line is received from the source.
    /// </summary>
    public event Action<string>? InfoReceived; // The Info line

    /// <summary>
    /// Runs the decoder pipeline asynchronously, processing events from the provided source until cancellation is requested.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public Task RunAsync(IEdgeSource source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Interlocked.CompareExchange(ref _running, 1, 0) != 0
            ? throw new InvalidOperationException("Pipeline is already running.")
            : RunCoreAsync(source, ct);
    }

    private async Task RunCoreAsync(IEdgeSource source, CancellationToken ct)
    {
        try
        {
            await foreach (var edgeEvent in source.ReadAsync(ct))
            {
                switch (edgeEvent)
                {
                    case EdgeEvent.Sample sample:
                        OnEdge(sample.Edge);
                        break;
                    case EdgeEvent.Info info:
                        InfoReceived?.Invoke(info.Line);
                        break;
                    case EdgeEvent.Reset reset:
                        OnReset(reset.Reason);
                        break;
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    // One method per stage keeps the depth-first push order of the nested loops
    // without the nesting: each stage raises its event, then feeds the next stage.
    private void OnEdge(Edge edge)
    {
        EdgeReceived?.Invoke(edge);
        foreach (var pulse in PulseMeasurement.Push(edge))
        {
            OnPulse(pulse);
        }
    }

    private void OnPulse(Pulse pulse)
    {
        PulseMeasured?.Invoke(pulse);
        foreach (var symbol in SymbolClassifier.Push(pulse))
        {
            OnSymbol(symbol);
        }
    }

    private void OnSymbol(Symbol symbol)
    {
        SymbolClassified?.Invoke(symbol);
        foreach (var frameSyncEvent in FrameSync.Push(symbol))
        {
            OnFrameSyncEvent(frameSyncEvent);
        }
    }

    private void OnFrameSyncEvent(FrameSyncEvent frameSyncEvent)
    {
        FrameSyncChanged?.Invoke(frameSyncEvent);
        if (frameSyncEvent is not FrameSyncEvent.FrameComplete completed)
        {
            return;
        }

        foreach (var decodedFrame in FieldDecoder.Push(completed.Frame))
        {
            OnDecodedFrame(decodedFrame);
        }
    }

    private void OnDecodedFrame(DecodedFrame decodedFrame)
    {
        FrameDecoded?.Invoke(decodedFrame);
        foreach (var validationResult in FrameValidator.Push(decodedFrame))
        {
            OnValidationResult(validationResult);
        }
    }

    private void OnValidationResult(ValidationResult validationResult)
    {
        FrameValidated?.Invoke(validationResult);
        foreach (var state in Clock.Push(validationResult))
        {
            ClockUpdated?.Invoke(state);
        }
    }

    private void OnReset(string reason)
    {
        PulseMeasurement.Reset();
        SymbolClassifier.Reset();
        FrameSync.Reset();
        FieldDecoder.Reset();
        FrameValidator.Reset();
        Clock.Reset();
        SourceReset?.Invoke(reason);
    }
}
