namespace WwvbDecoder.Core.Tests;

public class FrameValidatorTests
{
    [Fact]
    public void Push_CleanFrame_IsAccepted()
    {
        // Arrange
        DecodedFrame decoded = TestFrames.Decode(TestFrames.Encode(TestFrames.CleanMinute));

        // Act
        ValidationResult result = new FrameValidator().Push(decoded).ShouldHaveSingleItem();

        // Assert
        result.Frame.ShouldBeSameAs(decoded);
        result.Accepted.ShouldBeTrue();
        result.Failures.IsDefault.ShouldBeFalse();
        result.Failures.ShouldBeEmpty();
    }

    [Fact]
    public void Push_Null_Throws()
    {
        // Arrange
        FrameValidator validator = new();

        // Act
        Action act = () => validator.Push(null!);

        // Assert
        act.ShouldThrow<ArgumentNullException>();
    }

    [Theory]
    [InlineData(2026, 12, 31, 23, 59, Ut1Sign.Plus, 0, false, DstStatus.Standard)]
    [InlineData(2028, 12, 31, 23, 59, Ut1Sign.Plus, 0, false, DstStatus.Standard)]
    [InlineData(2028, 1, 1, 0, 0, Ut1Sign.Plus, 0, false, DstStatus.Standard)]
    [InlineData(2026, 12, 10, 19, 30, Ut1Sign.Minus, 9, false, DstStatus.Standard)]
    [InlineData(2026, 12, 10, 19, 30, Ut1Sign.Plus, 0, true, DstStatus.Dst)]
    public void Push_LegalBoundaryFrame_IsAccepted(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        Ut1Sign sign,
        int tenths,
        bool pending,
        DstStatus dst
    )
    {
        // Arrange
        Frame frame = WwvbFrameEncoder.Encode(
            new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero),
            1_000_000,
            sign,
            tenths,
            pending,
            dst
        );

        // Act
        ValidationResult result = new FrameValidator()
            .Push(TestFrames.Decode(frame))
            .ShouldHaveSingleItem();

        // Assert
        result.Failures.ShouldBeEmpty();
        result.Accepted.ShouldBeTrue();
    }

    [Theory]
    [InlineData("0:Z", ValidationCheck.Markers)]
    [InlineData("9:O", ValidationCheck.Markers)]
    [InlineData("19:E", ValidationCheck.Markers)]
    [InlineData("29:Z", ValidationCheck.Markers)]
    [InlineData("39:O", ValidationCheck.Markers)]
    [InlineData("49:E", ValidationCheck.Markers)]
    [InlineData("59:Z", ValidationCheck.Markers)]
    [InlineData("1:M", ValidationCheck.Markers, ValidationCheck.ErrorSymbols)]
    [InlineData("4:M", ValidationCheck.Markers, ValidationCheck.UnusedBits)]
    [InlineData("4:O", ValidationCheck.UnusedBits)]
    [InlineData("10:O", ValidationCheck.UnusedBits)]
    [InlineData("11:O", ValidationCheck.UnusedBits)]
    [InlineData("14:O", ValidationCheck.UnusedBits)]
    [InlineData("20:O", ValidationCheck.UnusedBits)]
    [InlineData("21:O", ValidationCheck.UnusedBits)]
    [InlineData("24:O", ValidationCheck.UnusedBits)]
    [InlineData("34:O", ValidationCheck.UnusedBits)]
    [InlineData("35:O", ValidationCheck.UnusedBits)]
    [InlineData("44:O", ValidationCheck.UnusedBits)]
    [InlineData("54:O", ValidationCheck.UnusedBits)]
    [InlineData("44:E", ValidationCheck.UnusedBits)]
    [InlineData("6:E", ValidationCheck.ErrorSymbols)]
    [InlineData("2:E", ValidationCheck.ErrorSymbols)]
    [InlineData("40:E", ValidationCheck.ErrorSymbols)]
    [InlineData("55:E", ValidationCheck.ErrorSymbols)]
    [InlineData("5:O 6:E", ValidationCheck.ErrorSymbols)]
    [InlineData("36:E", ValidationCheck.ErrorSymbols, ValidationCheck.Ut1Sign)]
    [InlineData("5:O 6:O", ValidationCheck.BcdDigits)]
    [InlineData("16:O", ValidationCheck.BcdDigits)]
    [InlineData("22:Z 25:O", ValidationCheck.BcdDigits)]
    [InlineData("30:O", ValidationCheck.BcdDigits)]
    [InlineData("45:O", ValidationCheck.BcdDigits)]
    [InlineData("50:O", ValidationCheck.BcdDigits)]
    [InlineData("1:O 2:O 3:Z", ValidationCheck.MinuteRange)]
    [InlineData("1:O 2:O", ValidationCheck.MinuteRange)]
    [InlineData("12:O 13:Z 15:Z 16:O 18:Z", ValidationCheck.HourRange)]
    [InlineData("12:O 15:Z 18:Z", ValidationCheck.HourRange)]
    [InlineData("27:O 32:O", ValidationCheck.DayOfYearRange)]
    [InlineData("22:Z 23:Z 26:Z 31:Z", ValidationCheck.DayOfYearRange)]
    [InlineData("27:O 32:O 33:O 55:O", ValidationCheck.DayOfYearRange)]
    [InlineData("27:O 32:O 55:O")]
    [InlineData("37:O", ValidationCheck.Ut1Sign)]
    [InlineData("36:Z 38:Z", ValidationCheck.Ut1Sign)]
    [InlineData("38:Z", ValidationCheck.Ut1Sign)]
    [InlineData("40:O 42:O", ValidationCheck.Ut1Range)]
    [InlineData("40:O 41:O 42:O 43:O", ValidationCheck.Ut1Range)]
    [InlineData("19:Z 29:Z", ValidationCheck.Markers)]
    [InlineData("4:O 10:O", ValidationCheck.UnusedBits)]
    [InlineData("5:O 6:O 30:O", ValidationCheck.BcdDigits)]
    [InlineData(
        "19:Z 4:O 31:E 1:O 2:O 3:Z 5:O 6:O 12:O 13:Z 15:Z 16:O 18:Z 22:Z 23:Z 26:Z 37:O 40:O 42:O",
        ValidationCheck.Markers,
        ValidationCheck.UnusedBits,
        ValidationCheck.ErrorSymbols,
        ValidationCheck.BcdDigits,
        ValidationCheck.MinuteRange,
        ValidationCheck.HourRange,
        ValidationCheck.DayOfYearRange,
        ValidationCheck.Ut1Sign,
        ValidationCheck.Ut1Range
    )]
    public void Push_ChangedFrame_FailsExactlyTheseChecks(
        string changes,
        params ValidationCheck[] expected
    )
    {
        // Arrange
        Frame frame = TestFrames.Change(TestFrames.Encode(TestFrames.CleanMinute), changes);
        DecodedFrame decoded = TestFrames.Decode(frame);

        // Act
        ValidationResult result = new FrameValidator().Push(decoded).ShouldHaveSingleItem();

        // Assert
        result.Frame.ShouldBeSameAs(decoded);
        result.Failures.Select(f => f.Check).ShouldBe(expected);
        result.Accepted.ShouldBe(expected.Length == 0);
        result.Failures.ShouldAllBe(f => !string.IsNullOrWhiteSpace(f.Detail));
    }
}
