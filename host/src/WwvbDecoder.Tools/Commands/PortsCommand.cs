namespace WwvbDecoder.Tools;

internal sealed class PortsCommand : AsyncCommand
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        CancellationToken cancellationToken
    )
    {
        var ports = SerialPorts.ListPortNames();
        Console.WriteLine("Available serial ports:");
        foreach (var port in ports)
        {
            Console.WriteLine($"  {port}");
        }
        return Task.FromResult(0);
    }
}
