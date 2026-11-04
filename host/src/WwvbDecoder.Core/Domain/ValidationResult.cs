using System.Collections.Immutable;

namespace WwvbDecoder.Core;

/// <summary>
/// Stage 6's verdict on one frame.
/// </summary>
/// <param name="Frame">The decoded frame that was validated.</param>
/// <param name="Accepted">True exactly when Failures is empty.</param>
/// <param name="Failures">In <see cref="ValidationCheck"/> order. Failures[0] is the headline a UI should display.</param>
public sealed record ValidationResult(
    DecodedFrame Frame,
    bool Accepted,
    ImmutableArray<ValidationFailure> Failures
);
