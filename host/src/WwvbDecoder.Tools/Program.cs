const string defaultFolder = "captures";
const string defaultLabel = "capture";

string label = defaultLabel;
string? outFile = null;

switch (args)
{
    // parse args
    case ["-h"] or ["--help"] or ["help"] or []:
        Console.WriteLine("Usage: WwvbDecoder.Tools <command>");
        Console.WriteLine(
            "  ports                                           List available serial ports."
        );
        Console.WriteLine(
            "  record --port COM5 [--label desk] [--out <file>]  Record the edge logger to a capture file."
        );
        return 0;
    case ["ports"]:
    {
        var ports = SerialPorts.ListPortNames();
        Console.WriteLine("Available serial ports:");
        foreach (var port in ports)
        {
            Console.WriteLine($"  {port}");
        }
        return 0;
    }
}

if (args is not ["record", "--port", _, ..])
{
    await Console.Error.WriteLineAsync("Invalid arguments. Use --help for usage information.");
    return 1;
}

string portName = args[2];
string[] options = args[3..];
if (options.Length % 2 != 0)
{
    await Console.Error.WriteLineAsync($"Missing value for {options[^1]}");
    return 1;
}

for (int i = 0; i < options.Length; i += 2)
{
    string value = options[i + 1];
    switch (options[i])
    {
        case "--label":
            label = value;
            break;
        case "--out":
            outFile = value;
            break;
        default:
            await Console.Error.WriteLineAsync($"Unknown argument: {options[i]}");
            return 1;
    }
}

using CancellationTokenSource cts = new();
ConsoleCancelEventHandler onCancel = (_, e) =>
{
    e.Cancel = true; // let the loop end and the file close
    cts.Cancel();
};
Console.CancelKeyPress += onCancel;

long sampleCount = 0;
bool fileCreated = false;

try
{
    outFile ??= Path.Combine(defaultFolder, CaptureFile.FileName(DateTimeOffset.UtcNow, label));

    var directory = Path.GetDirectoryName(outFile);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    await using var writer = CaptureFile.Create(outFile);
    fileCreated = true;
    var capture = new CaptureWriter(writer, TimeProvider.System);
    capture.WriteHeader(DateTimeOffset.UtcNow, $"serial {portName}, label {label}", null);

    await using var serialSource = new SerialEdgeSource(portName);
    serialSource.RawLineReceived += capture.WriteLine;

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
                Console.WriteLine(
                    Status(now, sampleCount, resetCount, serialSource.MalformedLines, lastMicros)
                );
                await writer.FlushAsync(cts.Token);
                nextStatus = NextMinute(now);
            }
        }

        Console.WriteLine("Port closed.");
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Stopped.");
    }

    Console.WriteLine(
        Status(
            DateTimeOffset.UtcNow,
            sampleCount,
            resetCount,
            serialSource.MalformedLines,
            lastMicros
        )
    );
    Console.WriteLine($"Wrote {outFile}");
    return 0;
}
catch (Exception ex)
    when (ex
            is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or ArgumentException
    )
{
    await Console.Error.WriteLineAsync(ex.Message);
    if (sampleCount == 0 && outFile is not null && fileCreated)
    {
        // Nothing was recorded (the port never opened, for example): leave no header-only file behind.
        File.Delete(outFile);
    }

    return 1;
}
finally
{
    Console.CancelKeyPress -= onCancel;
}

static string Clock() =>
    DateTimeOffset.UtcNow.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

static string Status(
    DateTimeOffset now,
    long edges,
    long resets,
    long malformed,
    ulong? lastMicros
) =>
    string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{now:HH:mm:ss}  edges {edges}  resets {resets}  malformed {malformed}  last {(lastMicros is { } m ? m.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")}"
    );

static DateTimeOffset NextMinute(DateTimeOffset time) =>
    new DateTimeOffset(
        time.Year,
        time.Month,
        time.Day,
        time.Hour,
        time.Minute,
        0,
        TimeSpan.Zero
    ).AddMinutes(1);
