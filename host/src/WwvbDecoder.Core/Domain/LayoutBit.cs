namespace WwvbDecoder.Core;

/// <summary>
/// A single bit in the layout of a WWVB frame.
/// </summary>
/// <param name="Second">0 to 59</param>
/// <param name="Weight">The weight as the layout table writes it: 40, not 4, for the first minute bit.</param>
public readonly record struct LayoutBit(int Second, int Weight);
