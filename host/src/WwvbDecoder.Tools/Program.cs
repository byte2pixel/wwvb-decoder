using WwvbDecoder.Tools;

// Create a cancellation token source to handle Ctrl+C
using var cancellationTokenSource = new CancellationTokenSource();

// Wire up Console.CancelKeyPress to trigger cancellation
ConsoleCancelEventHandler Cancel()
{
    return (_, e) =>
    {
        e.Cancel = true; // Prevent immediate process termination
        cancellationTokenSource.Cancel();
        Console.WriteLine("Cancellation requested...");
    };
}

var cancelHandler = Cancel();

Console.CancelKeyPress += cancelHandler;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("WwvbDecoder.Tools");
    config
        .AddCommand<RecordCommand>("record")
        .WithDescription("Record edges from a serial port to a capture file.");
    config.AddCommand<PortsCommand>("ports").WithDescription("Lists all serial port ports.");
    config.SetExceptionHandler(
        (ex, _) =>
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    );
});

// Pass the cancellation token to RunAsync
try
{
    return await app.RunAsync(args, cancellationTokenSource.Token);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
