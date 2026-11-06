namespace WwvbDecoder.Core.Tests;

internal static class SymbolStream
{
    private const ulong Second = 1_000_000;

    public static ulong DurationOf(SymbolKind kind) =>
        kind switch
        {
            SymbolKind.Marker => 800_000,
            SymbolKind.One => 500_000,
            SymbolKind.Zero => 200_000,
            _ => 50_000,
        };

    public static Symbol LeadIn(Frame frame) =>
        new(frame.StartMicros - Second, DurationOf(SymbolKind.Marker), SymbolKind.Marker);

    public static IEnumerable<Symbol> Of(Frame frame) =>
        frame.Symbols.Select(
            (kind, second) =>
                new Symbol(frame.StartMicros + (ulong)second * Second, DurationOf(kind), kind)
        );
}
