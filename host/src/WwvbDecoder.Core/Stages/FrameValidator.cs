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

        // Every check runs, in ValidationCheck order, and reports at most one failure.
        ValidationFailure?[] results =
        [
            CheckMarkers(input),
            CheckUnusedBits(input),
            CheckErrorSymbols(input),
            CheckBcdDigits(input),
            CheckMinuteRange(input),
            CheckHourRange(input),
            CheckDayOfYearRange(input),
            CheckUt1Sign(input),
            CheckUt1Range(input),
        ];
        List<ValidationFailure> failures = [.. results.OfType<ValidationFailure>()];

        if (CheckConsecutive(input, failures) is { } consecutive)
        {
            failures.Add(consecutive);
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
        new DateTime(
            2000 + frame.Year,
            1,
            1,
            frame.Hour,
            frame.Minute,
            0,
            DateTimeKind.Utc
        ).AddDays(frame.DayOfYear - 1);

    // A marker second must hold a Marker, and no other second may.
    private static ValidationFailure? CheckMarkers(DecodedFrame input)
    {
        Frame frame = input.Frame;
        for (int second = 0; second < WwvbFrameLayout.FrameSeconds; second++)
        {
            bool isMarkerSecond = WwvbFrameLayout.Roles[second] == SecondRole.Marker;
            SymbolKind kind = frame.Symbols[second];
            if (isMarkerSecond != (kind == SymbolKind.Marker))
            {
                return new ValidationFailure(ValidationCheck.Markers, $"second {second} is {kind}");
            }
        }
        return null;
    }

    private static ValidationFailure? CheckUnusedBits(DecodedFrame input)
    {
        Frame frame = input.Frame;
        foreach (int second in WwvbFrameLayout.UnusedSeconds)
        {
            if (frame.Symbols[second] != SymbolKind.Zero)
            {
                return new ValidationFailure(
                    ValidationCheck.UnusedBits,
                    $"second {second} is {frame.Symbols[second]}"
                );
            }
        }
        return null;
    }

    private static ValidationFailure? CheckErrorSymbols(DecodedFrame input) =>
        input.ErrorSeconds.IsEmpty
            ? null
            : new ValidationFailure(
                ValidationCheck.ErrorSymbols,
                $"Error at seconds {string.Join(", ", input.ErrorSeconds)}"
            );

    private static ValidationFailure? CheckBcdDigits(DecodedFrame input)
    {
        Frame frame = input.Frame;
        var badDigit = WwvbFrameLayout
            .Digits.Select(digit => (digit, value: SumBits(digit.Bits, frame) / digit.PlaceValue))
            .FirstOrDefault(t => t.value > 9);
        return badDigit.digit is null
            ? null
            : new ValidationFailure(
                ValidationCheck.BcdDigits,
                $"{DigitName(badDigit.digit)} digit is {badDigit.value}"
            );
    }

    private static ValidationFailure? CheckMinuteRange(DecodedFrame input) =>
        input.Minute > 59
            ? new ValidationFailure(ValidationCheck.MinuteRange, $"minute {input.Minute}")
            : null;

    private static ValidationFailure? CheckHourRange(DecodedFrame input) =>
        input.Hour > 23
            ? new ValidationFailure(ValidationCheck.HourRange, $"hour {input.Hour}")
            : null;

    private static ValidationFailure? CheckDayOfYearRange(DecodedFrame input) =>
        input switch
        {
            { DayOfYear: 0 or > 366 } => new ValidationFailure(
                ValidationCheck.DayOfYearRange,
                $"day {input.DayOfYear}"
            ),
            { DayOfYear: 366, LeapYear: false } => new ValidationFailure(
                ValidationCheck.DayOfYearRange,
                "day 366 without the leap-year flag"
            ),
            _ => null,
        };

    private static ValidationFailure? CheckUt1Sign(DecodedFrame input)
    {
        if (input.Ut1Sign != Ut1Sign.Invalid)
        {
            return null;
        }

        var signBits = string.Join(
            ' ',
            WwvbFrameLayout.Ut1SignSeconds.Select(second => BitText(input.Frame.Symbols[second]))
        );
        return new ValidationFailure(ValidationCheck.Ut1Sign, $"sign bits {signBits}");
    }

    // Integer arithmetic keeps the text the same in every culture.
    private static ValidationFailure? CheckUt1Range(DecodedFrame input) =>
        input.Ut1Tenths > 9
            ? new ValidationFailure(
                ValidationCheck.Ut1Range,
                $"UT1 correction {input.Ut1Tenths / 10}.{input.Ut1Tenths % 10} s"
            )
            : null;

    // Skipped when there is:
    // - No previously accepted frame.
    // - Frame started more than 90 s before this one (a decreasing StartMicros wraps to a huge gap, so it is skipped too)
    // - A failed check means this frame's time cannot be built.
    private ValidationFailure? CheckConsecutive(
        DecodedFrame input,
        List<ValidationFailure> failures
    )
    {
        bool timeCanBeBuilt = !failures.Exists(failure =>
            failure.Check
                is ValidationCheck.BcdDigits
                    or ValidationCheck.MinuteRange
                    or ValidationCheck.HourRange
                    or ValidationCheck.DayOfYearRange
        );
        if (_previousFrame is null || !timeCanBeBuilt)
        {
            return null;
        }

        ulong gap = input.Frame.StartMicros - _previousFrame.Frame.StartMicros;
        if (gap > MaxComparableGapMicros)
        {
            return null;
        }

        DateTime expected = FrameTime(_previousFrame).AddMinutes(1);
        DateTime actual = FrameTime(input);
        if (actual != expected)
        {
            // ':' in a custom format is the culture's time separator, so pin the culture.
            return new ValidationFailure(
                ValidationCheck.Consecutive,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"expected {expected:yyyy-MM-ddTHH:mm}Z, got {actual:yyyy-MM-ddTHH:mm}Z"
                )
            );
        }

        bool gapInRange =
            gap
            is >= FrameGapMicros - FrameGapToleranceMicros
                and <= FrameGapMicros + FrameGapToleranceMicros;
        return gapInRange
            ? null
            : new ValidationFailure(
                ValidationCheck.Consecutive,
                $"started {gap / 1_000_000}.{gap % 1_000_000 / 1_000:000} s after the previous frame"
            );
    }

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
