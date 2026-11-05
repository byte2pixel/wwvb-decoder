using System.Collections.Immutable;

namespace WwvbDecoder.Core;

/// <summary>
/// One WWVB frame: 60 symbols, one per second, starting at second 0.
/// </summary>
/// <param name="StartMicros">The start of the second-0 symbol.</param>
/// <param name="EndMicros">The end of the second-59 symbol.</param>
/// <param name="Symbols">The symbols in the frame.</param>
/// <exception cref="ArgumentOutOfRangeException">Thrown when the number of symbols is not exactly 60.</exception>
public sealed record Frame(ulong StartMicros, ulong EndMicros, ImmutableArray<SymbolKind> Symbols)
{
    /// <summary>
    /// Gets the symbols in the frame. A frame must contain exactly 60 symbols, as per the WWVB time code specification. If the number of symbols is not exactly 60, an ArgumentOutOfRangeException is thrown.
    /// </summary>
    public ImmutableArray<SymbolKind> Symbols { get; } =
        Symbols is { IsDefault: false, Length: 60 }
            ? Symbols
            : throw new ArgumentOutOfRangeException(
                nameof(Symbols),
                "A frame must contain exactly 60 symbols."
            );
}
