using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace WwvbDecoder.Core;

/// <summary>
/// A codec for parsing lines of text into <see cref="EdgeEvent"/>s and formatting <see cref="Edge"/>s as lines of text.
/// </summary>
public static class EdgeLineCodec
{
    /// <summary>
    /// Takes one line without its line feed. A malformed line returns false rather than throwing.
    /// </summary>
    /// <param name="line">The line of text to parse.</param>
    /// <param name="evt">The parsed <see cref="EdgeEvent"/> if successful; otherwise, <c>null</c>.</param>
    /// <returns><c>true</c> if the line was successfully parsed; otherwise, <c>false</c>.</returns>
    public static bool TryParse(ReadOnlySpan<char> line, [NotNullWhen(true)] out EdgeEvent? evt)
    {
        evt = null;
        if (line.Length > 63 || line.Contains('\r'))
        {
            return false;
        }

        switch (line)
        {
            case "":
                return false;
            case "# session":
                evt = new EdgeEvent.Reset("session");
                return true;
        }

        char edgeType = line[0];

        switch (edgeType)
        {
            case 'H':
            case 'L':
                break;
            case '#':
                evt = new EdgeEvent.Info(line.ToString());
                return true;
            default:
                return false;
        }

        if (line.Length is < 3 or > 22)
        {
            return false;
        }

        foreach (char c in line[2..])
        {
            if (char.IsAsciiDigit(c))
                continue;

            return false;
        }

        if (
            line[1] != ' '
            || !ulong.TryParse(
                line[2..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out ulong timestamp
            )
        )
        {
            return false;
        }

        Edge edge = new(timestamp, edgeType == 'H' ? PinLevel.High : PinLevel.Low);
        evt = new EdgeEvent.Sample(edge);
        return true;
    }

    /// <summary>
    /// Formats an <see cref="Edge"/> as a protocol line, without the line feed.
    /// </summary>
    /// <param name="edge">The <see cref="Edge"/> to format.</param>
    /// <returns>A string representation of the <see cref="Edge"/>.</returns>
    public static string Format(Edge edge)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(edge.Level == PinLevel.High ? 'H' : 'L')} {edge.Micros}"
        );
    }
}
