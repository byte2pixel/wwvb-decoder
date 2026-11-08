using System.Globalization;

namespace WwvbDecoder.Core.Tests;

public class FrameValidatorConsecutiveTests
{
    private const ulong PrevStart = 1_000_000;

    private static DecodedFrame Frame(string utc, ulong startMicros, string changes = "") =>
        TestFrames.Decode(
            TestFrames.Change(
                TestFrames.Encode(
                    DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture),
                    startMicros
                ),
                changes
            )
        );

    private static ValidationResult PushOne(FrameValidator validator, DecodedFrame frame) =>
        validator.Push(frame).ShouldHaveSingleItem();

    [Theory]
    [InlineData("2026-12-10T19:30Z", "2026-12-10T19:31Z", 60_000_000, "")]
    [InlineData("2026-12-10T19:59Z", "2026-12-10T20:00Z", 60_000_000, "")]
    [InlineData("2026-12-10T23:59Z", "2026-12-11T00:00Z", 60_000_000, "")]
    [InlineData("2026-12-31T23:59Z", "2027-01-01T00:00Z", 60_000_000, "")]
    [InlineData("2028-12-31T23:59Z", "2029-01-01T00:00Z", 60_000_000, "")]
    [InlineData("2026-12-10T19:30Z", "2026-12-10T19:31Z", 59_950_000, "")]
    [InlineData("2026-12-10T19:30Z", "2026-12-10T19:31Z", 60_050_000, "")]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:30Z",
        60_000_000,
        "",
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:32Z",
        60_000_000,
        "",
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:29Z",
        60_000_000,
        "",
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T20:31Z",
        60_000_000,
        "",
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-11T19:31Z",
        60_000_000,
        "",
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2027-12-10T19:31Z",
        60_000_000,
        "",
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:31Z",
        59_949_999,
        "",
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:31Z",
        60_050_001,
        "",
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:31Z",
        90_000_000,
        "",
        ValidationCheck.Consecutive
    )]
    [InlineData("2026-12-10T19:30Z", "2026-12-10T19:30Z", 90_000_001, "")]
    [InlineData("2026-12-10T19:30Z", "2026-12-10T19:32Z", 120_000_000, "")]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:31Z",
        60_000_000,
        "5:O 6:O",
        ValidationCheck.BcdDigits
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:31Z",
        60_000_000,
        "1:O 2:O",
        ValidationCheck.MinuteRange
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:31Z",
        60_000_000,
        "12:O 15:Z 18:Z",
        ValidationCheck.HourRange
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:31Z",
        60_000_000,
        "27:O 32:O",
        ValidationCheck.DayOfYearRange
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:31Z",
        60_000_000,
        "4:O",
        ValidationCheck.UnusedBits
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:35Z",
        60_000_000,
        "19:Z",
        ValidationCheck.Markers,
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:35Z",
        60_000_000,
        "4:O",
        ValidationCheck.UnusedBits,
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:35Z",
        60_000_000,
        "57:E",
        ValidationCheck.ErrorSymbols,
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:35Z",
        60_000_000,
        "37:O",
        ValidationCheck.Ut1Sign,
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:35Z",
        60_000_000,
        "40:O 42:O",
        ValidationCheck.Ut1Range,
        ValidationCheck.Consecutive
    )]
    public void Push_AfterAcceptedFrame_ComparesWithIt(
        string prevUtc,
        string nextUtc,
        int gapMicros,
        string changes,
        params ValidationCheck[] expected
    )
    {
        // Arrange
        FrameValidator validator = new();
        DecodedFrame prev = Frame(prevUtc, PrevStart);
        DecodedFrame next = Frame(nextUtc, PrevStart + (ulong)gapMicros, changes);
        PushOne(validator, prev).Accepted.ShouldBeTrue();

        // Act
        ValidationResult result = PushOne(validator, next);

        // Assert
        result.Frame.ShouldBeSameAs(next);
        result.Failures.Select(f => f.Check).ShouldBe(expected);
        result.Accepted.ShouldBe(expected.Length == 0);
        result.Failures.ShouldAllBe(f => !string.IsNullOrWhiteSpace(f.Detail));
    }

    [Fact]
    public void Push_RejectedFrame_DoesNotReplacePrevious()
    {
        // Arrange
        FrameValidator validator = new();
        PushOne(validator, Frame("2026-12-10T19:30Z", PrevStart)).Accepted.ShouldBeTrue();
        PushOne(validator, Frame("2026-12-10T19:30Z", PrevStart + 30_000_000, "4:O"))
            .Accepted.ShouldBeFalse();

        // Act
        ValidationResult result = PushOne(
            validator,
            Frame("2026-12-10T19:31Z", PrevStart + 60_000_000)
        );

        // Assert
        result.Accepted.ShouldBeTrue();
    }

    [Fact]
    public void Push_AcceptedFrame_ReplacesPrevious()
    {
        // Arrange
        FrameValidator validator = new();
        PushOne(validator, Frame("2026-12-10T19:30Z", PrevStart)).Accepted.ShouldBeTrue();
        PushOne(validator, Frame("2026-12-10T19:31Z", PrevStart + 60_000_000))
            .Accepted.ShouldBeTrue();

        // Act: the same minute as the second frame, 120 s after the first
        ValidationResult result = PushOne(
            validator,
            Frame("2026-12-10T19:31Z", PrevStart + 120_000_000)
        );

        // Assert
        result.Failures.Select(f => f.Check).ShouldBe([ValidationCheck.Consecutive]);
    }

    [Fact]
    public void Reset_ForgetsPrevious()
    {
        // Arrange
        FrameValidator validator = new();
        PushOne(validator, Frame("2026-12-10T19:30Z", PrevStart)).Accepted.ShouldBeTrue();

        // Act
        validator.Reset();
        ValidationResult result = PushOne(
            validator,
            Frame("2026-12-10T19:30Z", PrevStart + 60_000_000)
        );

        // Assert
        result.Accepted.ShouldBeTrue();
    }

    [Fact]
    public void Push_ResultNotEnumerated_StillRemembersFrame()
    {
        // Arrange
        FrameValidator validator = new();
        _ = validator.Push(Frame("2026-12-10T19:30Z", PrevStart));

        // Act
        ValidationResult result = PushOne(
            validator,
            Frame("2026-12-10T19:30Z", PrevStart + 60_000_000)
        );

        // Assert
        result.Failures.Select(f => f.Check).ShouldBe([ValidationCheck.Consecutive]);
    }
}
