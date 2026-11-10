namespace WwvbDecoder.Tools;

internal sealed class DecodePortCommand : AsyncCommand<DecodePortCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<port>")]
        [Description("The serial port to decode.")]
        public required string Port { get; set; }
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using SerialEdgeSource serialSource = new(settings.Port);
            Console.WriteLine($"Decoding edges from serial port {settings.Port}...");
            try
            {
                await DecodeRunner.RunAsync(
                    serialSource,
                    reducedCarrierLevel: null,
                    wallSamples: [],
                    flushEveryLine: true,
                    cancellationToken
                );
            }
            finally
            {
                Console.WriteLine($"Malformed lines: {serialSource.MalformedLines}");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Decoding port cancelled.");
        }
        return 0;
    }
}
