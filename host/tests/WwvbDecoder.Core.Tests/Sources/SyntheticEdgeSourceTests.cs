using System.Globalization;
using Microsoft.Extensions.Time.Testing;

namespace WwvbDecoder.Core.Tests;

public class SyntheticEdgeSourceTests
{
    private readonly TimeProvider _timeProvider = new FakeTimeProvider();
    private static readonly DateTimeOffset StartUtc = new(2026, 11, 04, 12, 30, 00, TimeSpan.Zero);

    [Fact]
    public void Name_IsSynthetic()
    {
        // Arrange & Act
        SyntheticEdgeSource source = new(_timeProvider) { StartUtc = StartUtc };

        // Assert
        source.Name.ShouldBe("synthetic");
    }

    [Fact]
    public void Options_HaveCorrectDefaults()
    {
        // Arrange & Act
        SyntheticEdgeSource source = new(_timeProvider) { StartUtc = StartUtc };

        // Assert
        source.Speed.ShouldBe(1.0);
        source.Noise.ShouldBe(0.0);
        source.Seed.ShouldBe(0);
        source.ReducedCarrierLevel.ShouldBe(PinLevel.High);
    }

    [Fact]
    public void Constructor_NullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange & Act
        Action act = () => _ = new SyntheticEdgeSource(null!) { StartUtc = StartUtc };

        // Assert
        act.ShouldThrow<ArgumentNullException>();
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void Speed_Invalid_Throws(double speed)
    {
        // Arrange & Act
        Action act = () =>
            _ = new SyntheticEdgeSource(_timeProvider) { StartUtc = StartUtc, Speed = speed };

        // Assert
        act.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void Noise_Invalid_Throws(double noise)
    {
        // Arrange & Act
        Action act = () =>
            _ = new SyntheticEdgeSource(_timeProvider) { StartUtc = StartUtc, Noise = noise };

        // Assert
        act.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(1.0, 0.0)]
    [InlineData(2.0, 0.5)]
    public void SpeedAndNoise_CanChangeAfterConstruction(double speed, double noise)
    {
        // Arrange
        SyntheticEdgeSource source = new(_timeProvider) { StartUtc = StartUtc };

        // Act
        source.Speed = speed;
        source.Noise = noise;

        // Assert
        source.Speed.ShouldBe(speed);
        source.Noise.ShouldBe(noise);
    }

    [Theory]
    [InlineData("2026-11-04T12:00:01Z")]
    [InlineData("1999-12-31T23:59:00Z")]
    [InlineData("2100-01-01T00:00:00Z")]
    [InlineData("2000-01-01T00:00:00+05:00")]
    public void StartUtc_Invalid_Throws(string time)
    {
        // Arrange
        DateTimeOffset invalidStartUtc = DateTimeOffset.Parse(time, CultureInfo.InvariantCulture);
        Action act = () =>
            _ = new SyntheticEdgeSource(_timeProvider) { StartUtc = invalidStartUtc };

        // Act & Assert
        act.ShouldThrow<ArgumentException>()
            .ShouldBeOfType<ArgumentException>()
            .ParamName.ShouldBe(nameof(SyntheticEdgeSource.StartUtc));
    }

    [Theory]
    [InlineData("2000-01-01T00:00:00Z")]
    [InlineData("2099-12-31T23:59:00Z")]
    [InlineData("2026-11-04T12:00:00+05:30")]
    public void StartUtc_Valid_IsAccepted(string time)
    {
        // Arrange
        DateTimeOffset validStartUtc = DateTimeOffset.Parse(time, CultureInfo.InvariantCulture);

        // Act
        SyntheticEdgeSource source = new(_timeProvider) { StartUtc = validStartUtc };

        // Assert
        source.StartUtc.ShouldBe(validStartUtc);
    }

    [Fact]
    public async Task DisposeAsync_Completes()
    {
        // Arrange
        SyntheticEdgeSource source = new(_timeProvider) { StartUtc = StartUtc };

        // Act & Assert
        await source.DisposeAsync().AsTask().ShouldNotThrowAsync();
    }
}
