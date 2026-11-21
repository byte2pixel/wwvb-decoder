using System.Diagnostics.CodeAnalysis;
using System.IO.Ports;

namespace WwvbDecoder.Tools;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
internal sealed class TxSyncCommand : AsyncCommand<TxSyncCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<port>")]
        [Description("The transmitter's serial port, for example COM5.")]
        public required string Port { get; set; }

        [CommandOption("-o|--offset <offset>")]
        [Description("Seconds to add to the PC's time; 86400 sends tomorrow's.")]
        [DefaultValue(0)]
        public int Offset { get; set; } = 0; // 86,400 = 1 day
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        using var port = new SerialPort(settings.Port, 115200, Parity.None, 8, StopBits.One);
        port.DtrEnable = true;
        port.Open();
        using var reader = new StreamReader(port.BaseStream);
        await using var writer = new StreamWriter(port.BaseStream);
        var session = new TransmitterSession(reader, writer, TimeProvider.System);
        try
        {
            var banner = await session.ReadBannerAsync(cancellationToken);
            Console.WriteLine(banner);
            var reply = await session.SetTimeAsync(settings.Offset, cancellationToken);
            Console.WriteLine($"Time set: {reply.Line}");
            if (!reply.Ok)
            {
                return 1;
            }

            var status = await session.StatusAsync(cancellationToken);
            Console.WriteLine($"Status: {status.Line}");
            return status.Ok ? 0 : 1;
        }
        catch (TimeoutException)
        {
            await Console.Error.WriteLineAsync(
                $"Timeout waiting for reply from transmitter on port {settings.Port}.".AsMemory(),
                cancellationToken
            );
            return 1;
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Tx sync cancelled.");
            return 1;
        }
    }
}
