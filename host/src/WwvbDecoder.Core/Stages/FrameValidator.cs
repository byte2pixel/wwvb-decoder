namespace WwvbDecoder.Core;

/// <summary>
/// Stage 6: Validation of the decoded frame.
/// </summary>
public sealed class FrameValidator : IStage<DecodedFrame, ValidationResult>
{
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

        return [new ValidationResult(input, failures.Count == 0, [.. failures])];
    }

    /// <summary>
    /// Resets the state of the frame validator.
    /// </summary>
    public void Reset()
    {
        // TODO: add the reset logic once the previous frame is stored.
    }

    private static int SumBits(IEnumerable<LayoutBit> bits, Frame frame)
    {
        return bits.Sum(bit => frame.Symbols[bit.Second] == SymbolKind.One ? bit.Weight : 0);
    }
}
