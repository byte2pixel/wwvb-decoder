using System.Collections.Immutable;

namespace WwvbDecoder.Core;

/// <summary>
/// A single BCD (Binary-Coded Decimal) digit in the WWVB time code.
/// </summary>
/// <param name="Field">Which field the digit belongs to.</param>
/// <param name="PlaceValue">100, 10, or 1. The smallest weight in the group.</param>
/// <param name="Bits">The digit's bits in frame order.</param>
public sealed record BcdDigit(SecondRole Field, int PlaceValue, ImmutableArray<LayoutBit> Bits);
