using System.Diagnostics.CodeAnalysis;

namespace WwvbDecoder.Tools;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
internal sealed class RecordCommand : AsyncCommand<RecordCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--port <port>", isRequired: true)]
        [Description("Serial port to read edges from.")]
        public string Port { get; set; } = string.Empty;

        [CommandOption("--label <label>", isRequired: false)]
        [DefaultValue("capture")]
        [Description(
            "Label for the capture, used in the output file name and inside the file header."
        )]
        public required string Label { get; set; }

        [CommandOption("--out <file>", isRequired: false)]
        [Description("Output file path. Defaults to 'captures/<timestamp>-<label>.edges.txt'")]
        public string? OutFile { get; set; }
    }

    const string DefaultFolder = "captures";

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        var tally = new Tally();
        bool fileCreated = false;
        settings.OutFile ??= Path.Combine(
            DefaultFolder,
            CaptureFile.FileName(DateTimeOffset.UtcNow, settings.Label)
        );
        try
        {
            var directory = Path.GetDirectoryName(settings.OutFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var writer = CaptureFile.Create(settings.OutFile);
            fileCreated = true;
            var capture = new CaptureWriter(writer, TimeProvider.System);
            capture.WriteHeader(
                DateTimeOffset.UtcNow,
                $"serial {settings.Port}, label {settings.Label}",
                null
            );

            await using var serialSource = new SerialEdgeSource(settings.Port);
            serialSource.RawLineReceived += capture.WriteLine;

            Console.WriteLine($"Recording {settings.Port} to {settings.OutFile}");
            Console.WriteLine("Press Ctrl+C to stop recording.");

            await RecordAsync(serialSource, writer, tally, cancellationToken);

            Console.WriteLine(
                Status(
                    DateTimeOffset.UtcNow,
                    tally.Samples,
                    tally.Resets,
                    serialSource.MalformedLines,
                    tally.LastMicros
                )
            );
            Console.WriteLine($"Wrote {settings.OutFile} with {tally.Samples} samples.");
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
            if (tally.Samples == 0 && fileCreated)
            {
                // Nothing was recorded (the port never opened, for example): leave no header-only file behind.
                File.Delete(settings.OutFile);
            }

            return 1;
        }
    }

    static async Task RecordAsync(
        SerialEdgeSource serialSource,
        TextWriter writer,
        Tally tally,
        CancellationToken cancellationToken
    )
    {
        DateTimeOffset nextStatus = NextMinute(DateTimeOffset.UtcNow);
        try
        {
            await foreach (var edgeEvent in serialSource.ReadAsync(cancellationToken))
            {
                tally.Count(edgeEvent);

                // One status line per minute, on the PC clock, and the file flushed with it so a
                // crash costs at most a minute of capture.
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (now >= nextStatus)
                {
                    Console.WriteLine(
                        Status(
                            now,
                            tally.Samples,
                            tally.Resets,
                            serialSource.MalformedLines,
                            tally.LastMicros
                        )
                    );
                    await writer.FlushAsync(cancellationToken);
                    nextStatus = NextMinute(now);
                }
            }

            Console.WriteLine("Port closed.");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Stopped.");
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await Console.Error.WriteLineAsync(
                $"Error occurred check that the port is available and not in use."
            );
            throw;
        }
    }

    // Counts what the source sends and echoes the lines worth seeing. The first Reset is the
    // source announcing the port opened, so it is not counted.
    sealed class Tally
    {
        bool _portOpened;

        public long Samples { get; private set; }
        public long Resets { get; private set; }
        public ulong? LastMicros { get; private set; }

        public void Count(EdgeEvent edgeEvent)
        {
            switch (edgeEvent)
            {
                case EdgeEvent.Reset when !_portOpened:
                    _portOpened = true;
                    Console.WriteLine("Port opened.");
                    break;
                case EdgeEvent.Reset reset:
                    Resets++;
                    Console.WriteLine($"{Clock()}  reset: {reset.Reason}");
                    break;
                case EdgeEvent.Sample sample:
                    Samples++;
                    LastMicros = sample.Edge.Micros;
                    break;
                case EdgeEvent.Info info
                    when info.Line.StartsWith("# wwvb-edge-logger", StringComparison.Ordinal):
                    Console.WriteLine(info.Line);
                    break;
            }
        }

        static string Clock() =>
            DateTimeOffset.UtcNow.ToString(
                "HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture
            );
    }

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
}
