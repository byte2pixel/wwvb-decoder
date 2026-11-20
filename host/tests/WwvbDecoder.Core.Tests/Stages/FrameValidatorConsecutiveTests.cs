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
    // Two minutes or more after the previous frame: held to the minutes between the two starts,
    // within 50 ms for each minute.
    [InlineData("2026-12-10T19:30Z", "2026-12-10T19:32Z", 120_000_000, "")]
    [InlineData("2026-12-10T19:30Z", "2026-12-10T19:32Z", 119_900_000, "")]
    [InlineData("2026-12-10T19:30Z", "2026-12-10T19:32Z", 120_100_000, "")]
    [InlineData("2026-12-10T19:30Z", "2026-12-10T19:33Z", 180_000_000, "")]
    [InlineData("2026-12-10T19:59Z", "2026-12-10T20:01Z", 120_000_000, "")]
    [InlineData("2026-12-31T23:58Z", "2027-01-01T00:01Z", 180_000_000, "")]
    [InlineData("2026-12-10T19:30Z", "2026-12-10T20:30Z", 3_600_000_000, "")]
    [InlineData("2026-12-10T19:30Z", "2026-12-10T20:30Z", 3_597_000_000, "")]
    [InlineData("2026-12-10T19:30Z", "2026-12-10T20:30Z", 3_603_000_000, "")]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:31Z",
        120_000_000,
        "",
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:33Z",
        120_000_000,
        "",
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:30Z",
        120_000_000,
        "",
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:32Z",
        180_000_000,
        "",
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T20:31Z",
        3_600_000_000,
        "",
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T20:29Z",
        3_603_000_000,
        "",
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T20:29Z",
        3_597_000_000,
        "",
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:31Z",
        119_900_000,
        "",
        ValidationCheck.Consecutive
    )]
    [InlineData(
        "2026-12-10T19:30Z",
        "2026-12-10T19:31Z",
        120_100_000,
        "",
        ValidationCheck.Consecutive
    )]
    // Not on a whole minute, or more than 60 minutes on: nothing to compare with, so the same
    // minute as the previous frame is accepted. 121 s is the leap second.
    [InlineData("2026-12-10T19:30Z", "2026-12-10T19:30Z", 119_899_999, "")]
    [InlineData("2026-12-10T19:30Z", "2026-12-10T19:30Z", 120_100_001, "")]
    [InlineData("2026-12-10T19:30Z", "2026-12-10T19:30Z", 121_000_000, "")]
    [InlineData("2026-12-10T19:30Z", "2026-12-10T19:30Z", 150_000_000, "")]
    [InlineData("2026-12-10T19:30Z", "2026-12-10T19:30Z", 3_596_999_999, "")]
    [InlineData("2026-12-10T19:30Z", "2026-12-10T19:30Z", 3_603_000_001, "")]
    [InlineData("2026-12-10T19:30Z", "2026-12-10T19:30Z", 3_660_000_000, "")]
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
        long gapMicros,
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

    // The frames of the simulator's noisy recording: 19:31 is accepted, the frame of 19:32 is
    // lost with frame sync, and the frame of 19:33 arrives with second 8 read as a zero.
    [Fact]
    public void Push_FlippedBitAfterALostFrame_IsRejected()
    {
        // Arrange
        FrameValidator validator = new();
        PushOne(validator, Frame("2026-12-10T19:30Z", 1_000_000)).Accepted.ShouldBeTrue();
        PushOne(validator, Frame("2026-12-10T19:31Z", 61_000_000)).Accepted.ShouldBeTrue();

        // Act
        ValidationResult flipped = PushOne(
            validator,
            Frame("2026-12-10T19:33Z", 181_000_000, "8:Z")
        );
        ValidationResult next = PushOne(validator, Frame("2026-12-10T19:34Z", 241_000_000));

        // Assert
        flipped.Frame.Minute.ShouldBe(32);
        flipped
            .Failures.ShouldHaveSingleItem()
            .ShouldBe(
                new ValidationFailure(
                    ValidationCheck.Consecutive,
                    "expected 2026-12-10T19:33Z, got 2026-12-10T19:32Z"
                )
            );
        next.Accepted.ShouldBeTrue();
    }

    // The earlier start is 2^64 less the difference away, far above 60 minutes.
    [Theory]
    [InlineData(10_000_000)]
    [InlineData(60_000_000)]
    public void Push_FrameStartingBeforePrevious_IsNotCompared(long earlierByMicros)
    {
        // Arrange
        FrameValidator validator = new();
        PushOne(validator, Frame("2026-12-10T19:30Z", 200_000_000)).Accepted.ShouldBeTrue();

        // Act
        ValidationResult result = PushOne(
            validator,
            Frame("2026-12-10T19:30Z", 200_000_000 - (ulong)earlierByMicros)
        );

        // Assert
        result.Accepted.ShouldBeTrue();
    }

    // The first frame reads 19:35 where 19:30 was sent, and nothing was there to check it against.
    [Fact]
    public void Push_FrameFollowingTheChallenger_IsAcceptedAndBecomesPrevious()
    {
        // Arrange
        FrameValidator validator = new();
        PushOne(validator, Frame("2026-12-10T19:35Z", 1_000_000)).Accepted.ShouldBeTrue();

        // Act
        ValidationResult first = PushOne(validator, Frame("2026-12-10T19:31Z", 61_000_000));
        ValidationResult second = PushOne(validator, Frame("2026-12-10T19:32Z", 121_000_000));
        ValidationResult afterWrong = PushOne(validator, Frame("2026-12-10T19:38Z", 181_000_000));

        // Assert: the wrong frame would have been followed by 19:38, the right ones by 19:33.
        first
            .Failures.ShouldHaveSingleItem()
            .Detail.ShouldBe("expected 2026-12-10T19:36Z, got 2026-12-10T19:31Z");
        second.Accepted.ShouldBeTrue();
        afterWrong
            .Failures.ShouldHaveSingleItem()
            .Detail.ShouldBe("expected 2026-12-10T19:33Z, got 2026-12-10T19:38Z");
    }

    [Fact]
    public void Push_FrameFollowingTheChallengerAcrossALostFrame_IsAccepted()
    {
        // Arrange
        FrameValidator validator = new();
        PushOne(validator, Frame("2026-12-10T19:35Z", 1_000_000)).Accepted.ShouldBeTrue();
        PushOne(validator, Frame("2026-12-10T19:31Z", 61_000_000)).Accepted.ShouldBeFalse();

        // Act
        ValidationResult result = PushOne(validator, Frame("2026-12-10T19:33Z", 181_000_000));

        // Assert
        result.Accepted.ShouldBeTrue();
    }

    // 19:30 at 1 s is accepted, then 19:40 is rejected, and each row's last frame follows the
    // previous frame in no case. It is accepted only when it follows the rejected one: the right
    // number of minutes on, starting on the minute, after a frame whose only failure was
    // Consecutive. "4:O" gives the rejected frame a failure of its own.
    [Theory]
    [InlineData(61_000_000, "", "2026-12-10T19:41Z", 121_000_000, true)]
    [InlineData(61_000_000, "", "2026-12-10T19:42Z", 181_000_000, true)]
    [InlineData(61_000_000, "", "2026-12-10T19:42Z", 121_000_000, false)]
    [InlineData(62_000_000, "", "2026-12-10T19:41Z", 121_000_000, false)]
    [InlineData(62_000_000, "", "2026-12-10T19:42Z", 181_000_000, false)]
    [InlineData(61_000_000, "4:O", "2026-12-10T19:41Z", 121_000_000, false)]
    public void Push_AfterRejectedFrame_AcceptsOnlyAFrameThatFollowsTheChallenger(
        long rejectedStartMicros,
        string rejectedChanges,
        string nextUtc,
        long nextStartMicros,
        bool accepted
    )
    {
        // Arrange
        FrameValidator validator = new();
        PushOne(validator, Frame("2026-12-10T19:30Z", 1_000_000)).Accepted.ShouldBeTrue();
        PushOne(validator, Frame("2026-12-10T19:40Z", (ulong)rejectedStartMicros, rejectedChanges))
            .Accepted.ShouldBeFalse();

        // Act
        ValidationResult result = PushOne(validator, Frame(nextUtc, (ulong)nextStartMicros));

        // Assert
        result.Accepted.ShouldBe(accepted);
    }

    [Fact]
    public void Push_AcceptedFrame_ClearsTheChallenger()
    {
        // Arrange: 19:40 is the challenger until 19:32 follows the previous frame.
        FrameValidator validator = new();
        PushOne(validator, Frame("2026-12-10T19:30Z", 1_000_000)).Accepted.ShouldBeTrue();
        PushOne(validator, Frame("2026-12-10T19:40Z", 61_000_000)).Accepted.ShouldBeFalse();
        PushOne(validator, Frame("2026-12-10T19:32Z", 121_000_000)).Accepted.ShouldBeTrue();

        // Act: two minutes after the challenger that was
        ValidationResult result = PushOne(validator, Frame("2026-12-10T19:42Z", 181_000_000));

        // Assert
        result
            .Failures.ShouldHaveSingleItem()
            .Detail.ShouldBe("expected 2026-12-10T19:33Z, got 2026-12-10T19:42Z");
    }

    [Fact]
    public void Push_SecondFrameRejectedForConsecutiveAlone_ReplacesTheChallenger()
    {
        // Arrange
        FrameValidator validator = new();
        PushOne(validator, Frame("2026-12-10T19:30Z", 1_000_000)).Accepted.ShouldBeTrue();
        PushOne(validator, Frame("2026-12-10T19:40Z", 61_000_000)).Accepted.ShouldBeFalse();
        PushOne(validator, Frame("2026-12-10T19:50Z", 121_000_000)).Accepted.ShouldBeFalse();

        // Act: follows 19:40, the first challenger, and not 19:50, the second
        ValidationResult result = PushOne(validator, Frame("2026-12-10T19:42Z", 181_000_000));

        // Assert
        result.Accepted.ShouldBeFalse();
    }

    // A frame that follows the challenger passes Consecutive, whatever else is wrong with it,
    // and being rejected it leaves the challenger in place for the frame after.
    [Fact]
    public void Push_RejectedFrameFollowingTheChallenger_ReportsOnlyItsOwnFailure()
    {
        // Arrange
        FrameValidator validator = new();
        PushOne(validator, Frame("2026-12-10T19:35Z", 1_000_000)).Accepted.ShouldBeTrue();
        PushOne(validator, Frame("2026-12-10T19:31Z", 61_000_000)).Accepted.ShouldBeFalse();

        // Act
        ValidationResult rejected = PushOne(
            validator,
            Frame("2026-12-10T19:32Z", 121_000_000, "4:O")
        );
        ValidationResult next = PushOne(validator, Frame("2026-12-10T19:33Z", 181_000_000));

        // Assert
        rejected.Failures.Select(f => f.Check).ShouldBe([ValidationCheck.UnusedBits]);
        next.Accepted.ShouldBeTrue();
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
