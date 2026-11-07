using System.Collections.Immutable;

namespace WwvbDecoder.Core.Tests;

internal static class TestFrames
{
    public static readonly DateTimeOffset CleanMinute = new(2026, 12, 10, 19, 30, 0, TimeSpan.Zero);

    public static Frame Encode(DateTimeOffset utc, ulong startMicros = 1_000_000) =>
        WwvbFrameEncoder.Encode(utc, startMicros, Ut1Sign.Plus, 0, false, DstStatus.Standard);

    public static DecodedFrame Decode(Frame frame) =>
        new FieldDecoder().Push(frame).ShouldHaveSingleItem();

    // "19:Z 4:O" sets second 19 to Zero and second 4 to One. M is Marker, E is Error.
    public static Frame Change(Frame frame, string changes)
    {
        ImmutableArray<SymbolKind> symbols = frame.Symbols;
        foreach (string change in changes.Split(' '))
        {
            string[] parts = change.Split(':');
            SymbolKind kind = parts[1] switch
            {
                "Z" => SymbolKind.Zero,
                "O" => SymbolKind.One,
                "M" => SymbolKind.Marker,
                "E" => SymbolKind.Error,
                _ => throw new ArgumentException(change, nameof(changes)),
            };
            symbols = symbols.SetItem(int.Parse(parts[0]), kind);
        }

        return new Frame(frame.StartMicros, frame.EndMicros, symbols);
    }
}
