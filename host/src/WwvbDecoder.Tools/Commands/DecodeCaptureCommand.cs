using System.Diagnostics.CodeAnalysis;
using ValidationResult = Spectre.Console.ValidationResult;

namespace WwvbDecoder.Tools;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
internal sealed class DecodeCaptureCommand : AsyncCommand<DecodeCaptureCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Path to the capture file to decode.")]
        public required string Path { get; set; }

        [CommandOption("--speed <speed>", isRequired: false)]
        [Description(
            "Speed multiplier for decoding. Default is 1.0 (real-time), 0 (unlimited speed)."
        )]
        [DefaultValue(1.0)]
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
        public double Speed { get; set; }
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        Console.WriteLine($"Decoding edges from file {settings.Path}...");
        try
        {
            var lines = CaptureFile.Open(settings.Path);
            await using ReplayEdgeSource source = new(lines, TimeProvider.System);
            source.Speed = settings.Speed;
            try
            {
                await DecodeRunner.RunAsync(
                    source,
                    source.Info.ReducedCarrierLevel,
                    source.Info.WallSamples,
                    flushEveryLine: settings.Speed > 0,
                    cancellationToken
                );
            }
            finally
            {
                Console.WriteLine($"Malformed lines: {source.MalformedLines}");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Decoding capture cancelled.");
        }
        return 0;
    }

    public override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (!File.Exists(settings.Path))
        {
            return ValidationResult.Error($"Input file '{settings.Path}' does not exist.");
        }

        return settings.Speed switch
        {
            < 0 => ValidationResult.Error("Speed must be a non-negative number."),
            Double.NaN => ValidationResult.Error("Speed must be a valid number."),
            _ => ValidationResult.Success(),
        };
    }
}
