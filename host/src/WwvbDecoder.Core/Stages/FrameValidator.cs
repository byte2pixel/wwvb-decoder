namespace WwvbDecoder.Core;

/// <summary>
/// Stage 6: Validation of the decoded frame.
/// </summary>
public sealed class FrameValidator : IStage<DecodedFrame, ValidationResult>
{
    private const ulong MaxComparableGapMicros = 90_000_000;
    private const ulong FrameGapMicros = 60_000_000;
    private const ulong FrameGapToleranceMicros = 50_000;

    private DecodedFrame? _previousFrame;

    /// <summary>
    /// Validates a decoded frame and returns the validation results.
    /// </summary>
    /// <param name="input">The decoded frame to validate.</param>
    /// <returns>A collection of validation results, only ever returns one result.</returns>
    public IEnumerable<ValidationResult> Push(DecodedFrame input)
    {
        ArgumentNullException.ThrowIfNull(input);

        List<ValidationFailure> failures = [];

        // Marker checks
        for (int second = 0; second < WwvbFrameLayout.FrameSeconds; second++)
        {
            var role = WwvbFrameLayout.Roles[second];
            var kind = input.Frame.Symbols[second];
            bool failureAdded = false;
            switch (role, kind)
            {
                case (SecondRole.Marker, SymbolKind.Marker):
                    break;
                case (SecondRole.Marker, _):
                case (_, SymbolKind.Marker):
                    failures.Add(
                        new ValidationFailure(
                            ValidationCheck.Markers,
                            $"Expected {role} but got {kind} at second {second}."
                        )
                    );
                    failureAdded = true;
                    break;
            }
            if (failureAdded)
            {
                break;
            }
        }

        // UnusedBits checks
        var unusedBitsWithErrors = WwvbFrameLayout
            .UnusedSeconds.Where(second => input.Frame.Symbols[second] != SymbolKind.Zero)
            .ToArray();
        if (unusedBitsWithErrors.Length > 0)
        {
            failures.Add(
                new ValidationFailure(
                    ValidationCheck.UnusedBits,
                    $"Second {unusedBitsWithErrors[0]} should be zero"
                )
            );
        }

        // ErrorSymbols checks
        if (!input.ErrorSeconds.IsEmpty)
        {
            var errorSeconds = string.Join(',', input.ErrorSeconds);
            failures.Add(
                new ValidationFailure(
                    ValidationCheck.ErrorSymbols,
                    $"Error symbols at seconds {errorSeconds}."
                )
            );
        }

        // BcdDigits checks
        var badDigit = WwvbFrameLayout
            .Digits.Select(digit =>
                (digit, value: SumBits(digit.Bits, input.Frame) / digit.PlaceValue)
            )
            .FirstOrDefault(t => t.value > 9);
        if (badDigit.digit is not null)
        {
            failures.Add(
                new ValidationFailure(
                    ValidationCheck.BcdDigits,
                    $"BCD digit {badDigit.digit.Field} has value {badDigit.value} which exceeds maximum of 9."
                )
            );
        }

        // Range checks
        if (input.Minute > 59)
        {
            failures.Add(
                new ValidationFailure(
                    ValidationCheck.MinuteRange,
                    $"Minute value {input.Minute} exceeds maximum of 59."
                )
            );
        }

        if (input.Hour > 23)
        {
            failures.Add(
                new ValidationFailure(
                    ValidationCheck.HourRange,
                    $"Hour value {input.Hour} exceeds maximum of 23."
                )
            );
        }

        switch (input.DayOfYear, input.LeapYear)
        {
            case (0, _):
                failures.Add(
                    new ValidationFailure(
                        ValidationCheck.DayOfYearRange,
                        "Day of year value cannot be zero."
                    )
                );
                break;
            case (> 366, true):
                failures.Add(
                    new ValidationFailure(
                        ValidationCheck.DayOfYearRange,
                        $"Day of year value {input.DayOfYear} is out of range for a leap year."
                    )
                );
                break;
            case (> 365, false):
                failures.Add(
                    new ValidationFailure(
                        ValidationCheck.DayOfYearRange,
                        $"Day of year value {input.DayOfYear} is out of range for non-leap year."
                    )
                );
                break;
        }

        if (input.Ut1Sign == Ut1Sign.Invalid)
        {
            failures.Add(new ValidationFailure(ValidationCheck.Ut1Sign, "UT1 sign is invalid."));
        }

        if (input.Ut1Tenths > 9)
        {
            failures.Add(
                new ValidationFailure(
                    ValidationCheck.Ut1Range,
                    $"UT1 tenths value {input.Ut1Tenths} exceeds maximum of 9."
                )
            );
        }

        // Consecutive check. Skipped when there is no previous accepted frame, when it started
        // more than 90 s before this one (a decreasing StartMicros wraps to a huge gap, so it
        // is skipped too), or when a failed check means this frame's time cannot be built.
        bool timeCanBeBuilt = !failures.Exists(failure =>
            failure.Check
                is ValidationCheck.BcdDigits
                    or ValidationCheck.MinuteRange
                    or ValidationCheck.HourRange
                    or ValidationCheck.DayOfYearRange
        );
        if (_previousFrame is not null && timeCanBeBuilt)
        {
            ulong gap = input.Frame.StartMicros - _previousFrame.Frame.StartMicros;
            if (gap <= MaxComparableGapMicros)
            {
                DateTime expected = FrameTime(_previousFrame).AddMinutes(1);
                DateTime actual = FrameTime(input);
                bool gapInRange =
                    gap >= FrameGapMicros - FrameGapToleranceMicros
                    && gap <= FrameGapMicros + FrameGapToleranceMicros;
                if (actual != expected || !gapInRange)
                {
                    failures.Add(
                        new ValidationFailure(
                            ValidationCheck.Consecutive,
                            $"expected {expected:yyyy-MM-ddTHH:mm}Z, got {actual:yyyy-MM-ddTHH:mm}Z, {gap} us after the previous frame"
                        )
                    );
                }
            }
        }

        bool accepted = failures.Count == 0;
        if (accepted)
        {
            _previousFrame = input;
        }

        return [new ValidationResult(input, accepted, [.. failures])];
    }

    /// <summary>
    /// Resets the state of the frame validator.
    /// </summary>
    public void Reset()
    {
        _previousFrame = null;
    }

    // Real date arithmetic carries minute, hour, day, and year rollovers.
    private static DateTime FrameTime(DecodedFrame frame) =>
        new DateTime(2000 + frame.Year, 1, 1, frame.Hour, frame.Minute, 0, DateTimeKind.Utc).AddDays(
            frame.DayOfYear - 1
        );

    private static int SumBits(IEnumerable<LayoutBit> bits, Frame frame)
    {
        return bits.Sum(bit => frame.Symbols[bit.Second] == SymbolKind.One ? bit.Weight : 0);
    }
}
