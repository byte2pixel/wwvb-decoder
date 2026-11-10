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
        long sampleCount = 0;
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

            bool firstEvent = true;
            long resetCount = 0;
            ulong? lastMicros = null;
            DateTimeOffset nextStatus = NextMinute(DateTimeOffset.UtcNow);

            try
            {
                await foreach (var edgeEvent in serialSource.ReadAsync(cancellationToken))
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
                            Status(
                                now,
                                sampleCount,
                                resetCount,
                                serialSource.MalformedLines,
                                lastMicros
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

            Console.WriteLine(
                Status(
                    DateTimeOffset.UtcNow,
                    sampleCount,
                    resetCount,
                    serialSource.MalformedLines,
                    lastMicros
                )
            );
            Console.WriteLine($"Wrote {settings.OutFile} with {sampleCount} samples.");
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
            if (sampleCount == 0 && fileCreated)
            {
                // Nothing was recorded (the port never opened, for example): leave no header-only file behind.
                File.Delete(settings.OutFile);
            }

            return 1;
        }
    }

    static string Clock() =>
        DateTimeOffset.UtcNow.ToString(
            "HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture
        );

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
