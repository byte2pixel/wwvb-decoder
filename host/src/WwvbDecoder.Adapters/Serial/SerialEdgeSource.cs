using System.IO.Ports;
using System.Runtime.CompilerServices;
using WwvbDecoder.Core;

namespace WwvbDecoder.Adapters;

/// <summary>
/// An <see cref="IEdgeSource"/> that reads edges from a serial port.
/// </summary>
public sealed class SerialEdgeSource : IEdgeSource
{
    // Exactly one of these is set by the constructors: the injected reader, or the port name.
    private readonly TextReader? _textReader;
    private readonly string? _portName;

    // The port of the enumeration in progress, so DisposeAsync can close it if the
    // enumeration was abandoned. Null between enumerations.
    private SerialPort? _port;

    /// <summary>
    /// Initializes a new instance of the <see cref="SerialEdgeSource"/> class. Each enumeration
    /// of <see cref="ReadAsync"/> opens the port and closes it when the enumeration ends.
    /// </summary>
    /// <param name="portName">Name of the serial port to read from.</param>
    public SerialEdgeSource(string portName)
    {
        ArgumentNullException.ThrowIfNull(portName);
        _portName = portName;
        Name = $"serial {portName}";
    }

    /// <summary>
    /// Any line source. Tests use a <see cref="StringReader"/> to provide a line source.
    /// </summary>
    /// <param name="name">Name of the line source.</param>
    /// <param name="textReader">The <see cref="TextReader"/> to read lines from.</param>
    public SerialEdgeSource(string name, TextReader textReader)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(textReader);
        Name = name;
        _textReader = textReader;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>
    /// The number of malformed lines received from the serial port
    /// </summary>
    public long MalformedLines { get; private set; }

    /// <summary>
    /// An event that is raised when a raw line is received from the serial port.
    /// </summary>
    public event Action<string>? RawLineReceived;

    /// <inheritdoc/>
    public async IAsyncEnumerable<EdgeEvent> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        TextReader reader;
        SerialPort? port = null;
        if (_textReader is not null)
        {
            reader = _textReader;
        }
        else
        {
            // The port constructor was used, so _portName is set.
            port = new SerialPort(_portName!, 115200, Parity.None, 8, StopBits.One)
            {
                DtrEnable = true,
            };
            port.Open();
            reader = new StreamReader(port.BaseStream);
            _port = port;
        }

        try
        {
            ulong? baseline = null;
            yield return new EdgeEvent.Reset("open");

            while (true)
            {
                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                RawLineReceived?.Invoke(line);

                if (!EdgeLineCodec.TryParse(line, out var edgeEvent))
                {
                    MalformedLines++;
                    continue;
                }

                switch (edgeEvent)
                {
                    case EdgeEvent.Reset:
                        baseline = null;
                        break;
                    case EdgeEvent.Sample sample:
                        if (sample.Edge.Micros < baseline)
                        {
                            yield return new EdgeEvent.Reset("timestamp went backwards");
                        }
                        baseline = sample.Edge.Micros;
                        break;
                    case EdgeEvent.Info:
                        break;
                }

                yield return edgeEvent;
            }
        }
        finally
        {
            // A port opened by this enumeration closes with it.
            if (port is not null)
            {
                reader.Dispose();
                port.Dispose();
                _port = null;
            }
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _textReader?.Dispose();
        _port?.Dispose();
        _port = null;
        return default;
    }
}
