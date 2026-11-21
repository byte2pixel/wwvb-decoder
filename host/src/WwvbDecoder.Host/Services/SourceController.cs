using System.Diagnostics;
using System.IO;
using WwvbDecoder.Core;
using WwvbDecoder.Host.Bridge;

namespace WwvbDecoder.Host.Services;

/// <summary>
/// Controls the source.
/// </summary>
/// <param name="pipeline">The decoder pipeline.</param>
/// <param name="environment">The source environment.</param>
/// <param name="poster">The message poster.</param>
/// <param name="settings">Where the page's choices are kept between runs.</param>
public sealed class SourceController(
    DecoderPipeline pipeline,
    ISourceEnvironment environment,
    IMessagePoster poster,
    ISettingsStore settings
) : IAsyncDisposable
{
    // Read once, when the host starts. The level goes to stage 2 before any source runs.
    // Only commands touch what is remembered, and they arrive on one thread.
    private HostSettings _remembered = LoadInto(pipeline, settings);

    // True until the first hello, or until a command chooses a source before it.
    private bool _launchPending = true;

    // The capture that replays in place of a remembered source that was not found at launch.
    // The settings file does not know of it, so the selection has to.
    private string? _replacementCapture;

    private static HostSettings LoadInto(DecoderPipeline pipeline, ISettingsStore settings)
    {
        HostSettings loaded = settings.Load();
        pipeline.PulseMeasurement.ReducedCarrierLevel = loaded.ReducedCarrierLevel;
        return loaded;
    }

    private static readonly SourceStatus Idle = new(SourceKind.None, "", SourceState.Stopped, "");

    // How the edge logger's banner line starts: "# wwvb-edge-logger 0.1.0 pin=2".
    private const string BannerPrefix = "# wwvb-edge-logger ";

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
                Greet();
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
                await SelectAsync(
                    SourceKind.Synthetic,
                    "synthetic",
                    () => environment.CreateSynthetic(synthetic.Noise),
                    remembered => remembered with { Noise = synthetic.Noise }
                );
                break;
            case Command.SelectSerial serial:
                await SelectAsync(
                    SourceKind.Serial,
                    $"serial {serial.Port}",
                    () => environment.CreateSerial(serial.Port),
                    remembered => remembered with { Port = serial.Port }
                );
                break;
            case Command.SelectReplay replay:
                await SelectAsync(
                    SourceKind.Replay,
                    "replay",
                    () => environment.CreateReplay(replay.Path, replay.Speed),
                    remembered =>
                        remembered with
                        {
                            Capture = Path.GetFileName(replay.Path),
                            Speed = replay.Speed,
                        }
                );
                break;
            case Command.SetNoise noise:
                if (_run?.Source is SyntheticEdgeSource running)
                {
                    running.Noise = noise.Noise;
                    Remember(_remembered with { Noise = noise.Noise });
                }
                break;
            case Command.SetReducedCarrierLevel level:
                pipeline.PulseMeasurement.ReducedCarrierLevel = level.Level;
                Remember(_remembered with { ReducedCarrierLevel = level.Level });
                break;
            case Command.SetReplaySpeed speed:
                if (_run?.Source is ReplayEdgeSource replaying)
                {
                    replaying.Speed = speed.Speed;
                    Remember(_remembered with { Speed = speed.Speed });
                    SetStatus(Status with { Detail = "" });
                }
                break;
            case Command.Stop:
                ChosenByCommand();
                await StopRunAsync();
                SetStatus(Idle);
                Remember(_remembered with { Running = false });
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

    /// <summary>
    /// What the host does after its reply to the first hello. <paramref name="Create"/> is null
    /// when nothing can be started, and then only the warning is sent.
    /// </summary>
    private sealed record Launch(
        SourceKind Kind,
        string Name,
        Func<IEdgeSource>? Create,
        string Warning = "",
        string? ReplacementCapture = null
    );

    /// <summary>
    /// Answers hello with the status, the ports, the captures, the frame layout, and the
    /// selection, in that order. After the first hello it then starts the remembered source,
    /// so that the page is there to see the run from its first edge.
    /// </summary>
    private void Greet()
    {
        IReadOnlyList<string> ports = environment.ListPorts();
        IReadOnlyList<CaptureEntry> captures = environment.ListCaptures();

        // Planned before the reply, because the selection names what is about to start.
        Launch? launch = _launchPending ? PlanLaunch(ports, captures) : null;
        _launchPending = false;
        _replacementCapture = launch?.ReplacementCapture ?? _replacementCapture;

        poster.Post("sourceStatus", Status);
        poster.Post("ports", new ItemsPayload<string>([.. ports]));
        poster.Post("captures", new ItemsPayload<CaptureEntry>([.. captures]));
        poster.Post("frameLayout", FrameLayoutPayload.Create());
        poster.Post("selection", CurrentSelection(captures));

        if (launch is null)
        {
            return;
        }
        if (launch.Create is null)
        {
            SetStatus(Idle with { Warning = launch.Warning });
        }
        else if (!StartOrReport(launch.Kind, launch.Name, launch.Create, launch.Warning))
        {
            _replacementCapture = null;
        }
    }

    /// <summary>
    /// The remembered source, if one was running. A port or a capture that is not found is
    /// replaced by the newest capture, the first of <paramref name="captures"/>. Nothing here is
    /// written to the settings, so the next launch looks for the remembered source again.
    /// </summary>
    private Launch? PlanLaunch(IReadOnlyList<string> ports, IReadOnlyList<CaptureEntry> captures)
    {
        HostSettings remembered = _remembered;
        if (!remembered.Running)
        {
            return null;
        }

        string notFound;
        switch (remembered.Source)
        {
            case SourceKind.Synthetic:
                return new Launch(
                    SourceKind.Synthetic,
                    "synthetic",
                    () => environment.CreateSynthetic(remembered.Noise)
                );
            case SourceKind.Serial:
                if (
                    remembered.Port is { } port
                    && ports.Contains(port, StringComparer.OrdinalIgnoreCase)
                )
                {
                    return new Launch(
                        SourceKind.Serial,
                        $"serial {port}",
                        () => environment.CreateSerial(port)
                    );
                }
                notFound = remembered.Port ?? "The port";
                break;
            case SourceKind.Replay:
                if (FindCapture(captures, remembered.Capture) is { } capture)
                {
                    return ReplayOf(capture, remembered.Speed);
                }
                notFound = remembered.Capture ?? "The capture";
                break;
            default:
                return null;
        }

        return captures.Count > 0
            ? ReplayOf(captures[0], remembered.Speed) with
            {
                Warning = $"{notFound} was not found. Replaying the newest capture.",
                ReplacementCapture = captures[0].Path,
            }
            : new Launch(
                SourceKind.None,
                "",
                Create: null,
                Warning: $"{notFound} was not found, and there is no capture to replay."
            );
    }

    private Launch ReplayOf(CaptureEntry capture, double speed) =>
        new(SourceKind.Replay, "replay", () => environment.CreateReplay(capture.Path, speed));

    /// <summary>
    /// Handles selectSource: stops the run, starts the source, and remembers the choice when the
    /// answer is "running". A choice that is refused changes nothing that is remembered.
    /// </summary>
    private async Task SelectAsync(
        SourceKind kind,
        string name,
        Func<IEdgeSource> create,
        Func<HostSettings, HostSettings> remember
    )
    {
        ChosenByCommand();
        await StopRunAsync();
        if (StartOrReport(kind, name, create))
        {
            Remember(remember(_remembered) with { Source = kind, Running = true });
        }
    }

    /// <summary>A command has the say now: nothing is left to start at launch or to stand in.</summary>
    private void ChosenByCommand()
    {
        _launchPending = false;
        _replacementCapture = null;
    }

    /// <summary>
    /// Creates the source and starts a run on it. A source that cannot be created, such as a
    /// capture that cannot be read, is answered with an error status, and nothing runs.
    /// </summary>
    /// <returns>Whether the run started, which is whether the answer was "running".</returns>
    private bool StartOrReport(
        SourceKind kind,
        string name,
        Func<IEdgeSource> create,
        string warning = ""
    )
    {
        IEdgeSource source;
        try
        {
            source = create();
        }
        catch (Exception e)
            when (e
                    is IOException
                        or UnauthorizedAccessException
                        or FormatException
                        or ArgumentException
            )
        {
            SetStatus(new SourceStatus(kind, name, SourceState.Error, e.Message));
            return false;
        }

        StartRun(source, kind, warning);
        return true;
    }

    private void Remember(HostSettings changed)
    {
        _remembered = changed;
        settings.Save(changed);
    }

    /// <summary>
    /// What the app bar of a page that has just loaded should show: what is remembered, or the
    /// capture that replays in its place.
    /// </summary>
    private Selection CurrentSelection(IReadOnlyList<CaptureEntry> captures) =>
        new(
            _replacementCapture is null ? _remembered.Source : SourceKind.Replay,
            _remembered.Port,
            _replacementCapture ?? FindCapture(captures, _remembered.Capture)?.Path,
            _remembered.Speed,
            _remembered.Noise,
            pipeline.PulseMeasurement.ReducedCarrierLevel
        );

    // The settings hold the file's name. The page wants the path as the captures list gives it.
    private static CaptureEntry? FindCapture(IReadOnlyList<CaptureEntry> captures, string? name) =>
        name is null
            ? null
            : captures.FirstOrDefault(entry =>
                string.Equals(
                    Path.GetFileName(entry.Path),
                    name,
                    StringComparison.OrdinalIgnoreCase
                )
            );

    private void StartRun(IEdgeSource source, SourceKind kind, string warning)
    {
        var run = new Run(source, new CancellationTokenSource());
        _run = run;

        // The warning belongs to the run: every later status of it is made from this one.
        SetStatus(
            new SourceStatus(kind, source.Name, SourceState.Running, "") { Warning = warning }
        );
        run.Completion = Task.Run(() => RunAndReportAsync(run));
    }

    /// <summary>Runs the pipeline and reports how it ended, unless a newer run has replaced this one.</summary>
    private async Task RunAndReportAsync(Run run)
    {
        pipeline.SourceReset += OnReset;
        pipeline.InfoReceived += OnInfo;
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
            pipeline.InfoReceived -= OnInfo;
        }

        return;

        void OnReset(string reason)
        {
            if (ReferenceEquals(_run, run))
            {
                SetStatus(Status with { Detail = $"reset: {reason}" });
            }
        }

        // The banner is the logger's own word that the port is the right device, so the page shows it.
        void OnInfo(string line)
        {
            if (
                ReferenceEquals(_run, run)
                && Status.Source == SourceKind.Serial
                && line.StartsWith(BannerPrefix, StringComparison.Ordinal)
            )
            {
                SetStatus(Status with { Detail = line });
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

    private readonly Lock _statusGate = new();

    private void SetStatus(SourceStatus status)
    {
        lock (_statusGate)
        {
            Status = status with
            {
                Speed = _run?.Source is ReplayEdgeSource replay ? replay.Speed : 1.0,
            };
            poster.Post("sourceStatus", Status);
        }
    }
}
