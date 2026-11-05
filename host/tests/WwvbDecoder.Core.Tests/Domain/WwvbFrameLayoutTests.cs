using System.Collections.Immutable;

namespace WwvbDecoder.Core.Tests;

public class WwvbFrameLayoutTests
{
    [Fact]
    public void MarkerSeconds_AreTheSevenMarkers()
    {
        // Act
        var markerSeconds = WwvbFrameLayout.MarkerSeconds;

        // Assert
        markerSeconds.ShouldBe([0, 9, 19, 29, 39, 49, 59]);
    }

    [Fact]
    public void UnusedSeconds_AreTheElevenUnused()
    {
        // Act
        var unusedSeconds = WwvbFrameLayout.UnusedSeconds;

        // Assert
        unusedSeconds.ShouldBe([4, 10, 11, 14, 20, 21, 24, 34, 35, 44, 54]);
    }

    [Fact]
    public void Ut1AndFlags_AreAtTheirSeconds()
    {
        WwvbFrameLayout.Ut1SignSeconds.ShouldBe([36, 37, 38]);
        WwvbFrameLayout.Ut1Correction.ShouldBe([
            new LayoutBit(40, 8),
            new LayoutBit(41, 4),
            new LayoutBit(42, 2),
            new LayoutBit(43, 1),
        ]);
        WwvbFrameLayout.LeapYearSecond.ShouldBe(55);
        WwvbFrameLayout.LeapSecondPendingSecond.ShouldBe(56);
        WwvbFrameLayout.DstSeconds.ShouldBe([57, 58]);
    }

    [Fact]
    public void Digits_AreNineInFrameOrder()
    {
        // Act
        var digits = WwvbFrameLayout.Digits;

        // Assert
        digits.Length.ShouldBe(9);
        digits.Select(d => d.PlaceValue).ShouldBe([10, 1, 10, 1, 100, 10, 1, 10, 1]);
        digits
            .Select(x => x.Field)
            .ShouldBe([
                SecondRole.Minute,
                SecondRole.Minute,
                SecondRole.Hour,
                SecondRole.Hour,
                SecondRole.DayOfYear,
                SecondRole.DayOfYear,
                SecondRole.DayOfYear,
                SecondRole.Year,
                SecondRole.Year,
            ]);
    }

    [Fact]
    public void DigitBits_AreWeightedEightFourTwoOneTimesPlaceValue()
    {
        ImmutableArray<int> weightValues = [8, 4, 2, 1];
        // Act
        var digits = WwvbFrameLayout.Digits;

        // Assert
        foreach (BcdDigit digit in digits)
        {
            var multipliers = digit.Bits.Select(x => x.Weight / digit.PlaceValue);
            multipliers.ShouldBe(weightValues.Skip(weightValues.Length - digit.Bits.Length));
        }
    }

    [Theory]
    [InlineData(SecondRole.Minute, 85)]
    [InlineData(SecondRole.Hour, 45)]
    [InlineData(SecondRole.DayOfYear, 465)]
    [InlineData(SecondRole.Year, 165)]
    public void FieldWeights_SumToFieldMaximum(SecondRole field, int expectedMax)
    {
        // Act
        var digits = WwvbFrameLayout.Digits.Where(x => x.Field == field);
        int sumOfWeights = digits.Sum(x => x.Bits.Sum(b => b.Weight));

        // Assert
        sumOfWeights.ShouldBe(expectedMax);
    }

    [Fact]
    public void EverySecond_IsCoveredExactlyOnce()
    {
        // Act
        var allSeconds = WwvbFrameLayout
            .MarkerSeconds.Concat(WwvbFrameLayout.UnusedSeconds)
            .Concat(WwvbFrameLayout.Digits.SelectMany(x => x.Bits.Select(b => b.Second)))
            .Concat(WwvbFrameLayout.Ut1SignSeconds)
            .Concat(WwvbFrameLayout.Ut1Correction.Select(x => x.Second))
            .Append(WwvbFrameLayout.LeapYearSecond)
            .Append(WwvbFrameLayout.LeapSecondPendingSecond)
            .Concat(WwvbFrameLayout.DstSeconds)
            .Order();

        // Assert
        allSeconds.ShouldBe(Enumerable.Range(0, 60));
    }

    [Fact]
    public void Roles_MatchTheListsTheyCameFrom()
    {
        WwvbFrameLayout.Roles.Length.ShouldBe(WwvbFrameLayout.FrameSeconds);
        WwvbFrameLayout.FrameSeconds.ShouldBe(60);

        WwvbFrameLayout.MarkerSeconds.ShouldAllBe(s =>
            WwvbFrameLayout.Roles[s] == SecondRole.Marker
        );
        WwvbFrameLayout.UnusedSeconds.ShouldAllBe(s =>
            WwvbFrameLayout.Roles[s] == SecondRole.Unused
        );

        WwvbFrameLayout
            .Digits.SelectMany(x => x.Bits.Select(b => b.Second))
            .ShouldAllBe(s =>
                WwvbFrameLayout.Roles[s]
                == WwvbFrameLayout.Digits.First(d => d.Bits.Any(b => b.Second == s)).Field
            );

        WwvbFrameLayout.Ut1SignSeconds.ShouldAllBe(s =>
            WwvbFrameLayout.Roles[s] == SecondRole.Ut1Sign
        );

        WwvbFrameLayout
            .Ut1Correction.Select(x => x.Second)
            .ShouldAllBe(s => WwvbFrameLayout.Roles[s] == SecondRole.Ut1Correction);

        WwvbFrameLayout.Roles[WwvbFrameLayout.LeapYearSecond].ShouldBe(SecondRole.LeapYear);

        WwvbFrameLayout
            .Roles[WwvbFrameLayout.LeapSecondPendingSecond]
            .ShouldBe(SecondRole.LeapSecondPending);

        WwvbFrameLayout.DstSeconds.ShouldAllBe(s => WwvbFrameLayout.Roles[s] == SecondRole.Dst);
    }
}
