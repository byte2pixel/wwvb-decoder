using System.Globalization;

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
                        new ValidationFailure(ValidationCheck.Markers, $"second {second} is {kind}")
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
            int second = unusedBitsWithErrors[0];
            failures.Add(
                new ValidationFailure(
                    ValidationCheck.UnusedBits,
                    $"second {second} is {input.Frame.Symbols[second]}"
                )
            );
        }

        // ErrorSymbols checks
        if (!input.ErrorSeconds.IsEmpty)
        {
            var errorSeconds = string.Join(", ", input.ErrorSeconds);
            failures.Add(
                new ValidationFailure(ValidationCheck.ErrorSymbols, $"Error at seconds {errorSeconds}")
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
                    $"{DigitName(badDigit.digit)} digit is {badDigit.value}"
                )
            );
        }

        // Range checks
        if (input.Minute > 59)
        {
            failures.Add(new ValidationFailure(ValidationCheck.MinuteRange, $"minute {input.Minute}"));
        }

        if (input.Hour > 23)
        {
            failures.Add(new ValidationFailure(ValidationCheck.HourRange, $"hour {input.Hour}"));
        }

        switch (input.DayOfYear, input.LeapYear)
        {
            case (0, _):
            case (> 366, _):
                failures.Add(
                    new ValidationFailure(ValidationCheck.DayOfYearRange, $"day {input.DayOfYear}")
                );
                break;
            case (366, false):
                failures.Add(
                    new ValidationFailure(
                        ValidationCheck.DayOfYearRange,
                        "day 366 without the leap-year flag"
                    )
                );
                break;
        }

        if (input.Ut1Sign == Ut1Sign.Invalid)
        {
            var signBits = string.Join(
                ' ',
                WwvbFrameLayout.Ut1SignSeconds.Select(second => BitText(input.Frame.Symbols[second]))
            );
            failures.Add(new ValidationFailure(ValidationCheck.Ut1Sign, $"sign bits {signBits}"));
        }

        if (input.Ut1Tenths > 9)
        {
            // Integer arithmetic keeps the text the same in every culture.
            failures.Add(
                new ValidationFailure(
                    ValidationCheck.Ut1Range,
                    $"UT1 correction {input.Ut1Tenths / 10}.{input.Ut1Tenths % 10} s"
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
                if (actual != expected)
                {
                    // ':' in a custom format is the culture's time separator, so pin the culture.
                    failures.Add(
                        new ValidationFailure(
                            ValidationCheck.Consecutive,
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"expected {expected:yyyy-MM-ddTHH:mm}Z, got {actual:yyyy-MM-ddTHH:mm}Z"
                            )
                        )
                    );
                }
                else if (!gapInRange)
                {
                    failures.Add(
                        new ValidationFailure(
                            ValidationCheck.Consecutive,
                            $"started {gap / 1_000_000}.{gap % 1_000_000 / 1_000:000} s after the previous frame"
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

    // UTC at second 0 of the frame. Real date arithmetic carries minute, hour, day, and year
    // rollovers. WwvbClock uses it for FrameUtc, so Consecutive and the clock share one formula.
    internal static DateTime FrameTime(DecodedFrame frame) =>
        new DateTime(2000 + frame.Year, 1, 1, frame.Hour, frame.Minute, 0, DateTimeKind.Utc).AddDays(
            frame.DayOfYear - 1
        );

    // "minute units", "day of year hundreds", and so on.
    private static string DigitName(BcdDigit digit)
    {
        string field = digit.Field switch
        {
            SecondRole.Minute => "minute",
            SecondRole.Hour => "hour",
            SecondRole.DayOfYear => "day of year",
            SecondRole.Year => "year",
            _ => digit.Field.ToString(),
        };
        string place = digit.PlaceValue switch
        {
            100 => "hundreds",
            10 => "tens",
            _ => "units",
        };
        return $"{field} {place}";
    }

    private static string BitText(SymbolKind kind) =>
        kind switch
        {
            SymbolKind.One => "1",
            SymbolKind.Zero => "0",
            _ => kind.ToString(),
        };

    private static int SumBits(IEnumerable<LayoutBit> bits, Frame frame)
    {
        return bits.Sum(bit => frame.Symbols[bit.Second] == SymbolKind.One ? bit.Weight : 0);
    }
}
