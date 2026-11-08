const string defaultFolder = "captures";
const string defaultLabel = "capture";

// parse args
if (args is ["-h"] or ["--help"] or ["help"] or [])
{
    Console.WriteLine("Usage: WwvbDecoder.Tools <command>");
    Console.WriteLine("  ports                                           List available serial ports.");
    Console.WriteLine("  record --port COM5 [--label desk] [--out <file>]  Record the edge logger to a capture file.");
    return 0;
}

if (args is ["ports"])
{
    var ports = SerialPorts.ListPortNames();
    Console.WriteLine("Available serial ports:");
    foreach (var port in ports)
    {
        Console.WriteLine($"  {port}");
    }
    return 0;
}

if (args is not ["record", "--port", _, ..])
{
    Console.Error.WriteLine("Invalid arguments. Use --help for usage information.");
    return 1;
}

string portName = args[2];
string label = defaultLabel;
string? outFile = null;

for (int i = 3; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--label" when i + 1 < args.Length:
            label = args[i + 1];
            break;
        case "--out" when i + 1 < args.Length:
            outFile = args[i + 1];
            break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 1;
    }

    i++;
}

long sampleCount = 0;
try
{
    outFile ??= Path.Combine(defaultFolder, CaptureFile.FileName(DateTimeOffset.UtcNow, label));

    var directory = Path.GetDirectoryName(outFile);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    using var writer = CaptureFile.Create(outFile);
    var capture = new CaptureWriter(writer, TimeProvider.System);
    capture.WriteHeader(DateTimeOffset.UtcNow, $"serial {portName}, label {label}", null);

    await using var serialSource = new SerialEdgeSource(portName);
    serialSource.RawLineReceived += capture.WriteLine;

    using CancellationTokenSource cts = new();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true; // let the loop end and the file close
        cts.Cancel();
    };

    Console.WriteLine($"Recording {portName} to {outFile}");
    Console.WriteLine("Press Ctrl+C to stop recording.");

    bool firstEvent = true;
    long resetCount = 0;
    ulong? lastMicros = null;
    DateTimeOffset nextStatus = NextMinute(DateTimeOffset.UtcNow);

    try
    {
        await foreach (var edgeEvent in serialSource.ReadAsync(cts.Token))
        {
            switch (edgeEvent)
            {
                case EdgeEvent.Reset reset:
                    if (firstEvent)
                    {
                        firstEvent = false;
                        Console.WriteLine("Port opened.");
                        break;
                    }

                    resetCount++;
                    Console.WriteLine($"{Clock()}  reset: {reset.Reason}");
                    break;
                case EdgeEvent.Sample sample:
                    sampleCount++;
                    lastMicros = sample.Edge.Micros;
                    break;
                case EdgeEvent.Info info:
                    if (info.Line.StartsWith("# wwvb-edge-logger"))
                    {
                        Console.WriteLine(info.Line);
                    }

                    break;
            }

            // One status line per minute, on the PC clock, and the file flushed with it so a
            // crash costs at most a minute of capture.
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now >= nextStatus)
            {
                Console.WriteLine(Status(now, sampleCount, resetCount, serialSource.MalformedLines, lastMicros));
                writer.Flush();
                nextStatus = NextMinute(now);
            }
        }

        Console.WriteLine("Port closed.");
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Stopped.");
    }

    Console.WriteLine(Status(DateTimeOffset.UtcNow, sampleCount, resetCount, serialSource.MalformedLines, lastMicros));
    Console.WriteLine($"Wrote {outFile}");
    return 0;
}
catch (Exception ex)
    when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
{
    Console.Error.WriteLine(ex.Message);
    if (sampleCount == 0 && outFile is not null && File.Exists(outFile))
    {
        // Nothing was recorded (the port never opened, for example): leave no header-only file behind.
        File.Delete(outFile);
    }

    return 1;
}

static string Clock() => DateTimeOffset.UtcNow.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

static string Status(DateTimeOffset now, long edges, long resets, long malformed, ulong? lastMicros) =>
    string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{now:HH:mm:ss}  edges {edges}  resets {resets}  malformed {malformed}  last {(lastMicros is { } m ? m.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")}"
    );

static DateTimeOffset NextMinute(DateTimeOffset time) =>
    new DateTimeOffset(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, TimeSpan.Zero).AddMinutes(1);
