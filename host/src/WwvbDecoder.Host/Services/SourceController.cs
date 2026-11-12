using System.Diagnostics;
using WwvbDecoder.Core;
using WwvbDecoder.Host.Bridge;

namespace WwvbDecoder.Host.Services;

/// <summary>
/// Controls the source.
/// </summary>
/// <param name="pipeline">The decoder pipeline.</param>
/// <param name="environment">The source environment.</param>
/// <param name="poster">The message poster.</param>
public sealed class SourceController(
#pragma warning disable CS9113 // Parameter is unread.
    DecoderPipeline pipeline,
#pragma warning restore CS9113 // Parameter is unread.
    ISourceEnvironment environment,
    IMessagePoster poster
) : IAsyncDisposable
{
    private static readonly SourceStatus Idle = new(SourceKind.None, "", SourceState.Stopped, "");

    /// <summary>
    /// The current source status.
    /// </summary>
    public SourceStatus Status { get; private set; } = Idle;

    /// <summary>
    /// Handles a command sent from the web page to the host.
    /// </summary>
    /// <param name="command">The command to handle.</param>
    public async Task HandleAsync(Command command)
    {
        switch (command)
        {
            case Command.Hello hello:
                if (hello.Protocol != 1)
                {
                    Trace.TraceWarning("Protocol mismatch: client {0} vs host 1", hello.Protocol);
                }
                // send the status, ports, and captures in order
                poster.Post("sourceStatus", Status);
                poster.Post("ports", new ItemsPayload<string>([.. environment.ListPorts()]));
                poster.Post(
                    "captures",
                    new ItemsPayload<CaptureEntry>([.. environment.ListCaptures()])
                );
                break;
            case Command.ListPorts:
                poster.Post("ports", new ItemsPayload<string>([.. environment.ListPorts()]));
                break;

            case Command.ListCaptures:
                poster.Post(
                    "captures",
                    new ItemsPayload<CaptureEntry>([.. environment.ListCaptures()])
                );
                break;
            case Command.SelectSynthetic synthetic:
                await StopRunAsync();
                StartRun(environment.CreateSynthetic(synthetic.Noise), SourceKind.Synthetic);
                break;
            case Command.SelectSerial serial:
                await StopRunAsync();
                SetStatus(
                    new SourceStatus(
                        SourceKind.Serial,
                        $"serial {serial.Port}",
                        SourceState.Error,
                        "not available yet"
                    )
                );
                break;
            case Command.SelectReplay:
                await StopRunAsync();
                SetStatus(
                    new SourceStatus(
                        SourceKind.Replay,
                        "replay",
                        SourceState.Error,
                        "not available yet"
                    )
                );
                break;
            case Command.SetNoise noise:
                if (_run?.Source is SyntheticEdgeSource running)
                {
                    running.Noise = noise.Noise;
                }
                break;
            case Command.SetReducedCarrierLevel level:
                pipeline.PulseMeasurement.ReducedCarrierLevel = level.Level;
                break;
            case Command.SetReplaySpeed:
                // Replay arrives later.
                break;
            case Command.Stop:
                await StopRunAsync();
                SetStatus(Idle);
                break;
            default:
                throw new UnreachableException("Unknown command type: " + command.GetType().Name);
        }
    }

    /// <summary>
    /// Disposes the source controller asynchronously.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // stops the run, so tests can `await using` it
        await StopRunAsync();
    }

    private sealed class Run(IEdgeSource source, CancellationTokenSource cts)
    {
        public IEdgeSource Source { get; } = source;
        public CancellationTokenSource Cts { get; } = cts;
        public Task Completion { get; set; } = Task.CompletedTask;
    }

    private volatile Run? _run;

    private void StartRun(IEdgeSource source, SourceKind kind)
    {
        var run = new Run(source, new CancellationTokenSource());
        _run = run;
        SetStatus(new SourceStatus(kind, source.Name, SourceState.Running, ""));
        run.Completion = Task.Run(() => RunAndReportAsync(run));
    }

    /// <summary>Runs the pipeline and reports how it ended, unless a newer run has replaced this one.</summary>
    private async Task RunAndReportAsync(Run run)
    {
        pipeline.SourceReset += OnReset;
        try
        {
            await pipeline.RunAsync(run.Source, run.Cts.Token);
            if (ReferenceEquals(_run, run))
            {
                SetStatus(Status with { State = SourceState.Stopped, Detail = "" });
            }
        }
        catch (OperationCanceledException) when (run.Cts.IsCancellationRequested)
        {
            // Canceled by StopRunAsync, which reports on its own terms.
        }
        catch (Exception e)
        {
            if (ReferenceEquals(_run, run))
            {
                SetStatus(Status with { State = SourceState.Error, Detail = e.Message });
            }
        }
        finally
        {
            pipeline.SourceReset -= OnReset;
        }

        return;

        void OnReset(string reason)
        {
            if (ReferenceEquals(_run, run))
            {
                SetStatus(Status with { Detail = $"reset: {reason}" });
            }
        }
    }

    private async Task StopRunAsync()
    {
        Run? run = _run;
        _run = null;
        if (run is null)
        {
            return;
        }

        await run.Cts.CancelAsync();
        await run.Completion;
        await run.Source.DisposeAsync();
        run.Cts.Dispose();
    }

    private void SetStatus(SourceStatus status)
    {
        Status = status;
        poster.Post("sourceStatus", status);
    }
}
