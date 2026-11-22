using WwvbDecoder.Core;

namespace WwvbDecoder.Host.Services;

/// <summary>
/// Watches the symbols of a run for the sign that the reduced-carrier level is set the wrong way
/// round. A single pulse cannot show it, because 0.2 s reduced in a second is 0.8 s full. Over a
/// minute it shows: with the level inverted every 0 reads as a marker, so a minute holds some
/// forty markers where a frame has seven, and no frame is ever completed.
/// </summary>
/// <remarks>
/// Measured on 2026-11-21. With the right level the most markers within 60 s and no frame
/// between them was 11, on the synthetic source at every noise from 0 to 1. With the level
/// inverted it was 33 to 42, on the overnight capture and on the synthetic source.
/// The symbols come on the pump thread and a change of level on the dispatcher, hence the lock.
/// </remarks>
public sealed class PolarityWatch
{
    /// <summary>What the host tells the presenter. The page shows it in its banner.</summary>
    public const string Warning =
        "The reduced-carrier level looks inverted. Try the other level in the signal settings.";

    private const int MostMarkers = 20;
    private const ulong WindowMicros = 60_000_000;

    private readonly Lock _gate = new();

    // The start of each marker of the last 60 s of the source's time, since the last frame.
    private readonly Queue<ulong> _markers = new();
    private bool _looksInverted;

    /// <summary>Whether the level looks inverted. It stays so until <see cref="Clear"/>.</summary>
    public bool LooksInverted
    {
        get
        {
            lock (_gate)
            {
                return _looksInverted;
            }
        }
    }

    /// <summary>Takes one symbol of stage 3.</summary>
    /// <param name="symbol">The symbol, with its start in the source's time.</param>
    /// <returns>True when this symbol made the level look inverted.</returns>
    public bool Add(Symbol symbol)
    {
        lock (_gate)
        {
            if (_looksInverted || symbol.Kind != SymbolKind.Marker)
            {
                return false;
            }

            _markers.Enqueue(symbol.StartMicros);
            while (symbol.StartMicros - _markers.Peek() > WindowMicros)
            {
                _markers.Dequeue();
            }

            _looksInverted = _markers.Count > MostMarkers;
            return _looksInverted;
        }
    }

    /// <summary>
    /// Starts the count again: a frame was completed, the level was changed, or the source reset.
    /// </summary>
    /// <returns>True when the level looked inverted until now.</returns>
    public bool Clear()
    {
        lock (_gate)
        {
            bool looked = _looksInverted;
            _looksInverted = false;
            _markers.Clear();
            return looked;
        }
    }
}
