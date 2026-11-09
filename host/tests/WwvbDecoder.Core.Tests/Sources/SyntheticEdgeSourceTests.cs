using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using static WwvbDecoder.Core.Tests.EnumeratorHelpers;

namespace WwvbDecoder.Core.Tests;

public class SyntheticEdgeSourceTests
{
    private enum Fault
    {
        None,
        DroppedEntering,
        DroppedLeaving,
        Jitter,
        PulseGlitch,
        GapGlitch,
    }

    private readonly FakeTimeProvider _timeProvider = new();
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

    [Fact]
    public async Task ReadAsync_FirstEvent_IsResetStart()
    {
        // Arrange
        SyntheticEdgeSource source = new(_timeProvider) { StartUtc = StartUtc, Speed = 0.0 };

        // Act
        var events = await TakeAsync(source, 1);

        // Assert
        events.Count.ShouldBe(1);
        events[0].ShouldBeOfType<EdgeEvent.Reset>().Reason.ShouldBe("start");
    }

    [Fact]
    public async Task ReadAsync_LeadIn_IsMarker()
    {
        // Arrange
        SyntheticEdgeSource source = new(_timeProvider) { StartUtc = StartUtc, Speed = 0.0 };

        // Act
        var events = await TakeAsync(source, 3);

        // Assert
        events.Count.ShouldBe(3);
        Edge edge1 = events[1].ShouldBeOfType<EdgeEvent.Sample>().Edge;
        EdgeLineCodec.Format(edge1).ShouldBe("H 0");
        Edge edge2 = events[2].ShouldBeOfType<EdgeEvent.Sample>().Edge;
        EdgeLineCodec.Format(edge2).ShouldBe("L 800000");
    }

    [Fact]
    public async Task ReadAsync_FirstFrame_MatchesLiteralEdges()
    {
        // Arrange
        SyntheticEdgeSource source = new(_timeProvider) { StartUtc = StartUtc, Speed = 0.0 };

        // Act
        var events = await TakeAsync(source, 120 + 3);

        // Assert
        List<Edge> actual =
        [
            .. events.Skip(3).Take(120).Select(e => e.ShouldBeOfType<EdgeEvent.Sample>().Edge),
        ];
        actual.ShouldBe(FirstFrameEdges);
    }

    [Theory]
    [InlineData("2026-11-04T12:30:00Z")]
    [InlineData("2026-12-31T23:59:00Z")]
    public async Task ReadAsync_TwoMinutes_MatchEncoderFrames(string time)
    {
        // Arrange
        DateTimeOffset start = DateTimeOffset.Parse(time, CultureInfo.InvariantCulture);
        SyntheticEdgeSource source = new(_timeProvider) { StartUtc = start, Speed = 0.0 };
        List<Edge> expected = [.. ExpectedFrameEdges(start, 0), .. ExpectedFrameEdges(start, 1)];

        // Act
        var events = await TakeAsync(source, 3 + 240);

        // Assert
        List<Edge> actual =
        [
            .. events.Skip(3).Select(e => e.ShouldBeOfType<EdgeEvent.Sample>().Edge),
        ];
        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task ReadAsync_EarliestStartUtc_ReadsLeadIn()
    {
        // Arrange
        DateTimeOffset earliestStartUtc = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        SyntheticEdgeSource source = new(_timeProvider)
        {
            StartUtc = earliestStartUtc,
            Speed = 0.0,
        };

        // Act
        var events = await TakeAsync(source, 4);

        // Assert
        events.Count.ShouldBe(4);
        Edge edge1 = events[1].ShouldBeOfType<EdgeEvent.Sample>().Edge;
        EdgeLineCodec.Format(edge1).ShouldBe("H 0");
        Edge edge2 = events[2].ShouldBeOfType<EdgeEvent.Sample>().Edge;
        EdgeLineCodec.Format(edge2).ShouldBe("L 800000");
        Edge edge3 = events[3].ShouldBeOfType<EdgeEvent.Sample>().Edge;
        EdgeLineCodec.Format(edge3).ShouldBe("H 1000000");
    }

    [Fact]
    public async Task ReadAsync_ReducedCarrierLevelLow_InvertsLevels()
    {
        // Arrange
        SyntheticEdgeSource source = new(_timeProvider)
        {
            StartUtc = StartUtc,
            Speed = 0.0,
            ReducedCarrierLevel = PinLevel.Low,
        };

        // Act
        var events = await TakeAsync(source, 4);

        // Assert
        events.Count.ShouldBe(4);
        Edge edge1 = events[1].ShouldBeOfType<EdgeEvent.Sample>().Edge;
        EdgeLineCodec.Format(edge1).ShouldBe("L 0");
        Edge edge2 = events[2].ShouldBeOfType<EdgeEvent.Sample>().Edge;
        EdgeLineCodec.Format(edge2).ShouldBe("H 800000");
        Edge edge3 = events[3].ShouldBeOfType<EdgeEvent.Sample>().Edge;
        EdgeLineCodec.Format(edge3).ShouldBe("L 1000000");
    }

    [Fact]
    public async Task ReadAsync_PulseDurations_AreContractValues()
    {
        // Arrange
        SyntheticEdgeSource source = new(_timeProvider) { StartUtc = StartUtc, Speed = 0.0 };

        // Act
        var events = await TakeAsync(source, 3 + 240);

        // Assert
        List<Edge> actual =
        [
            .. events.Skip(3).Select(e => e.ShouldBeOfType<EdgeEvent.Sample>().Edge),
        ];

        List<ulong> durations =
        [
            .. actual
                .Chunk(2)
                .Select(chunk => chunk[1].Micros - chunk[0].Micros)
                .Distinct()
                .Order(),
        ];
        durations.ShouldBe([200_000UL, 500_000UL, 800_000UL]);
    }

    [Fact]
    public async Task ReadAsync_SecondEnumeration_StartsOver()
    {
        // Arrange
        SyntheticEdgeSource source = new(_timeProvider) { StartUtc = StartUtc, Speed = 0.0 };

        // Act
        var events1 = await TakeAsync(source, 3 + 240);
        var events2 = await TakeAsync(source, 3 + 240);

        // Assert
        events1.ShouldBe(events2);
    }

    [Fact(Timeout = 5000)]
    public async Task ReadAsync_CancelledMidStream_Throws()
    {
        // Arrange
        SyntheticEdgeSource source = new(_timeProvider) { StartUtc = StartUtc, Speed = 0.0 };
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );

        int eventCount = 0;
        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (EdgeEvent _ in source.ReadAsync(cts.Token))
            {
                // Consume events until cancellation occurs
                if (++eventCount == 10)
                {
                    await cts.CancelAsync();
                }
            }
        });
        eventCount.ShouldBe(10);
    }

    [Fact]
    public async Task ReadAsync_PastYear2099_Throws()
    {
        // Arrange
        DateTimeOffset start = new(2099, 12, 31, 23, 59, 0, TimeSpan.Zero);
        SyntheticEdgeSource source = new(_timeProvider) { StartUtc = start, Speed = 0.0 };

        await TakeAsync(source, 3 + 120).ShouldNotThrowAsync();

        // Act & Assert
        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
        {
            await TakeAsync(source, 3 + 120 + 1);
        });
    }

    [Fact]
    public async Task ReadAsync_SpeedOne_PacesEdges()
    {
        SyntheticEdgeSource source = new(_timeProvider) { StartUtc = StartUtc, Speed = 1.0 };
        var e = source
            .ReadAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var move = MoveAsync(e);
        move.IsCompleted.ShouldBeTrue();
        (await NextAsync(e, move)).ShouldBe(new EdgeEvent.Reset("start"));

        move = MoveAsync(e);
        move.IsCompleted.ShouldBeTrue();
        (await NextAsync(e, move)).ShouldBe(new EdgeEvent.Sample(new Edge(0, PinLevel.High)));

        move = MoveAsync(e);
        move.IsCompleted.ShouldBeFalse();
        _timeProvider.Advance(TimeSpan.FromMilliseconds(799));
        move.IsCompleted.ShouldBeFalse(); // L 800000 is waiting on the clock
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        (await NextAsync(e, move)).ShouldBe(new EdgeEvent.Sample(new Edge(800_000, PinLevel.Low)));

        move = MoveAsync(e);
        move.IsCompleted.ShouldBeFalse();
        _timeProvider.Advance(TimeSpan.FromMilliseconds(199));
        move.IsCompleted.ShouldBeFalse(); // H 1000000 is waiting on the clock
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        (await NextAsync(e, move)).ShouldBe(
            new EdgeEvent.Sample(new Edge(1_000_000, PinLevel.High))
        );
    }

    [Fact]
    public async Task ReadAsync_SpeedTwo_HalvesDelays()
    {
        SyntheticEdgeSource source = new(_timeProvider) { StartUtc = StartUtc, Speed = 2.0 };
        var e = source
            .ReadAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var move = MoveAsync(e);
        move.IsCompleted.ShouldBeTrue();
        (await NextAsync(e, move)).ShouldBe(new EdgeEvent.Reset("start"));

        move = MoveAsync(e);
        move.IsCompleted.ShouldBeTrue();
        (await NextAsync(e, move)).ShouldBe(new EdgeEvent.Sample(new Edge(0, PinLevel.High)));

        move = MoveAsync(e);
        move.IsCompleted.ShouldBeFalse();
        _timeProvider.Advance(TimeSpan.FromMilliseconds(399));
        move.IsCompleted.ShouldBeFalse(); // L 800000 is waiting on the clock
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        (await NextAsync(e, move)).ShouldBe(new EdgeEvent.Sample(new Edge(800_000, PinLevel.Low)));

        move = MoveAsync(e);
        move.IsCompleted.ShouldBeFalse();
        _timeProvider.Advance(TimeSpan.FromMilliseconds(99));
        move.IsCompleted.ShouldBeFalse(); // H 1000000 is waiting on the clock
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        (await NextAsync(e, move)).ShouldBe(
            new EdgeEvent.Sample(new Edge(1_000_000, PinLevel.High))
        );
    }

    [Fact]
    public async Task ReadAsync_SlowSpeed_WaitsOnTimeProvider()
    {
        SyntheticEdgeSource source = new(_timeProvider) { StartUtc = StartUtc, Speed = 0.01 };
        var e = source
            .ReadAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var move = MoveAsync(e);
        move.IsCompleted.ShouldBeTrue();
        (await NextAsync(e, move)).ShouldBe(new EdgeEvent.Reset("start"));

        move = MoveAsync(e);
        move.IsCompleted.ShouldBeTrue();
        (await NextAsync(e, move)).ShouldBe(new EdgeEvent.Sample(new Edge(0, PinLevel.High)));

        move = MoveAsync(e);
        move.IsCompleted.ShouldBeFalse();
        _timeProvider.Advance(TimeSpan.FromSeconds(79.9));
        move.IsCompleted.ShouldBeFalse(); // L 800000 is waiting on the clock
        _timeProvider.Advance(TimeSpan.FromSeconds(0.1));
        (await NextAsync(e, move)).ShouldBe(new EdgeEvent.Sample(new Edge(800_000, PinLevel.Low)));
    }

    [Fact]
    public async Task ReadAsync_SpeedChange_AppliesToNextDelay()
    {
        SyntheticEdgeSource source = new(_timeProvider) { StartUtc = StartUtc, Speed = 1.0 };
        var e = source
            .ReadAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var move = MoveAsync(e);
        move.IsCompleted.ShouldBeTrue();
        (await NextAsync(e, move)).ShouldBe(new EdgeEvent.Reset("start"));

        move = MoveAsync(e);
        move.IsCompleted.ShouldBeTrue();
        (await NextAsync(e, move)).ShouldBe(new EdgeEvent.Sample(new Edge(0, PinLevel.High)));

        move = MoveAsync(e);
        source.Speed = 4.0;
        move.IsCompleted.ShouldBeFalse();
        _timeProvider.Advance(TimeSpan.FromMilliseconds(799));
        move.IsCompleted.ShouldBeFalse(); // L 800000 is waiting on the clock
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        (await NextAsync(e, move)).ShouldBe(new EdgeEvent.Sample(new Edge(800_000, PinLevel.Low)));

        move = MoveAsync(e);
        move.IsCompleted.ShouldBeFalse();
        _timeProvider.Advance(TimeSpan.FromMilliseconds(49));
        move.IsCompleted.ShouldBeFalse(); // H 1000000 is waiting on the clock
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        (await NextAsync(e, move)).ShouldBe(
            new EdgeEvent.Sample(new Edge(1_000_000, PinLevel.High))
        );

        source.Speed = 0.0;
        move = MoveAsync(e);
        move.IsCompleted.ShouldBeTrue();
        (await NextAsync(e, move)).ShouldBe(
            new EdgeEvent.Sample(new Edge(1_800_000, PinLevel.Low))
        );
    }

    [Fact]
    public async Task ReadAsync_CancelDuringDelay_Throws()
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        SyntheticEdgeSource source = new(_timeProvider) { StartUtc = StartUtc, Speed = 1.0 };
        var e = source
            .ReadAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(cts.Token);
        var move = MoveAsync(e);
        move.IsCompleted.ShouldBeTrue();
        (await NextAsync(e, move)).ShouldBe(new EdgeEvent.Reset("start"));

        move = MoveAsync(e);
        move.IsCompleted.ShouldBeTrue();
        (await NextAsync(e, move)).ShouldBe(new EdgeEvent.Sample(new Edge(0, PinLevel.High)));

        move = MoveAsync(e);
        move.IsCompleted.ShouldBeFalse();
        await cts.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            move.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
        );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ReadAsync_SameSeed_GivesSameStream(int seed)
    {
        SyntheticEdgeSource noisySource1 = new(_timeProvider)
        {
            StartUtc = StartUtc,
            Speed = 0.0,
            Noise = 0.5,
            Seed = seed,
        };

        SyntheticEdgeSource noisySource2 = new(_timeProvider)
        {
            StartUtc = StartUtc,
            Speed = 0.0,
            Noise = 0.5,
            Seed = seed,
        };

        var noisyEdges1 = await ReadSecondsAsync(noisySource1, 600);
        var noisyEdges2 = await ReadSecondsAsync(noisySource2, 600);

        noisyEdges1.ShouldBeEquivalentTo(noisyEdges2);
    }

    [Fact]
    public async Task ReadAsync_NoiseSecondEnumeration_StartsOver()
    {
        SyntheticEdgeSource noisySource = new(_timeProvider)
        {
            StartUtc = StartUtc,
            Speed = 0.0,
            Noise = 0.5,
            Seed = 7,
        };

        var firstRead = await ReadSecondsAsync(noisySource, 600);
        var secondRead = await ReadSecondsAsync(noisySource, 600);
        firstRead.ShouldBeEquivalentTo(secondRead);
    }

    [Fact]
    public async Task ReadAsync_DifferentSeed_GivesDifferentStream()
    {
        SyntheticEdgeSource noisySource1 = new(_timeProvider)
        {
            StartUtc = StartUtc,
            Speed = 0.0,
            Noise = 0.5,
            Seed = 1,
        };

        SyntheticEdgeSource noisySource2 = new(_timeProvider)
        {
            StartUtc = StartUtc,
            Speed = 0.0,
            Noise = 0.5,
            Seed = 2,
        };

        (await ReadSecondsAsync(noisySource1, 600)).ShouldNotBe(
            await ReadSecondsAsync(noisySource2, 600)
        );
    }

    [Theory]
    [InlineData(1, PinLevel.High)]
    [InlineData(2, PinLevel.High)]
    [InlineData(3, PinLevel.High)]
    [InlineData(1, PinLevel.Low)]
    [InlineData(2, PinLevel.Low)]
    [InlineData(3, PinLevel.Low)]
    public async Task ReadAsync_NoiseOne_FaultsEverySecond(int seed, PinLevel reducedCarrierLevel)
    {
        SyntheticEdgeSource noisySource = new(_timeProvider)
        {
            StartUtc = StartUtc,
            Speed = 0.0,
            Noise = 1.0,
            Seed = seed,
            ReducedCarrierLevel = reducedCarrierLevel,
        };

        var faults = await ClassifySecondsAsync(noisySource, 600);
        faults.ShouldAllBe(f => f != Fault.None);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ReadAsync_NoiseOne_TimestampsIncrease(int seed)
    {
        SyntheticEdgeSource noisySource = new(_timeProvider)
        {
            StartUtc = StartUtc,
            Speed = 0.0,
            Noise = 1.0,
            Seed = seed,
        };

        var edges = await ReadSecondsAsync(noisySource, 600);
        for (int i = 1; i < edges.Count; i++)
        {
            edges[i].Micros.ShouldBeGreaterThan(edges[i - 1].Micros);
        }
    }

    [Fact]
    public async Task ReadAsync_Jitter_ReachesBothClamps()
    {
        SyntheticEdgeSource noisySource = new(_timeProvider)
        {
            StartUtc = StartUtc,
            Speed = 0.0,
            Noise = 1.0,
            Seed = 42,
        };

        var edges = await ReadSecondsAsync(noisySource, 1200);
        List<ulong> durations =
        [
            .. edges
                .GroupBy(edge => edge.Micros / 1_000_000UL)
                .Where(second => second.Count() == 2)
                .Select(second => second.Last().Micros - second.First().Micros),
        ];

        durations.ShouldContain(10_000UL);
        durations.ShouldContain(990_000UL);
    }

    [Fact]
    public async Task ReadAsync_NoiseQuarter_FaultsAboutQuarter()
    {
        SyntheticEdgeSource noisySource = new(_timeProvider)
        {
            StartUtc = StartUtc,
            Speed = 0.0,
            Noise = 0.25,
        };

        var faults = await ClassifySecondsAsync(noisySource, 600);
        int faultCount = faults.Count(f => f != Fault.None);
        faultCount.ShouldBeInRange(100, 200); // Expect about 25% of seconds to have faults, but allow for some randomness
    }

    [Fact]
    public async Task ReadAsync_NoiseChangedMidStream_AppliesToLaterSeconds()
    {
        SyntheticEdgeSource cleanSource = new(_timeProvider)
        {
            StartUtc = StartUtc,
            Speed = 0.0,
            Noise = 0.0,
            Seed = 5,
        };
        SyntheticEdgeSource noisySource = new(_timeProvider)
        {
            StartUtc = StartUtc,
            Speed = 0.0,
            Noise = 0.0,
            Seed = 5,
        };

        List<Edge> cleanEdges = await ReadSecondsAsync(cleanSource, 60);
        List<Edge> noisyEdges = [];
        await foreach (
            EdgeEvent evt in noisySource.ReadAsync(TestContext.Current.CancellationToken)
        )
        {
            if (evt is not EdgeEvent.Sample sample)
            {
                continue;
            }

            if (sample.Edge.Micros >= 60_000_000UL)
            {
                break;
            }

            noisyEdges.Add(sample.Edge);
            if (noisyEdges.Count == 60)
            {
                noisySource.Noise = 1.0; // seconds 0 to 29 gave 2 edges each
            }
        }
        var noisy = noisyEdges.ToLookup(edge => edge.Micros / 1_000_000UL);
        var clean = cleanEdges.ToLookup(edge => edge.Micros / 1_000_000UL);

        List<Fault> faults = [];
        for (ulong second = 0; second < 60; second++)
        {
            faults.Add(Classify([.. noisy[second]], [.. clean[second]]));
        }

        faults.Take(30).ShouldAllBe(fault => fault == Fault.None);
        faults.Skip(30).ShouldAllBe(fault => fault != Fault.None);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ReadAsync_NoiseOne_UsesAllFaultsEvenly(int seed)
    {
        // Arrange
        SyntheticEdgeSource noisySource = new(_timeProvider)
        {
            StartUtc = StartUtc,
            Speed = 0.0,
            Noise = 1.0,
            Seed = seed,
        };

        // Act
        var faults = await ClassifySecondsAsync(noisySource, 600);

        // Assert
        int jitterCount = faults.Count(f => f == Fault.Jitter);
        int gapCount = faults.Count(f => f == Fault.GapGlitch);
        int pulseCount = faults.Count(f => f == Fault.PulseGlitch);
        int droppedEnteringCount = faults.Count(f => f == Fault.DroppedEntering);
        int droppedLeavingCount = faults.Count(f => f == Fault.DroppedLeaving);

        droppedEnteringCount.ShouldBeGreaterThan(40);
        droppedLeavingCount.ShouldBeGreaterThan(40);
        (droppedEnteringCount + droppedLeavingCount).ShouldBeInRange(100, 200);
        jitterCount.ShouldBeInRange(100, 200);
        gapCount.ShouldBeInRange(100, 200);
        pulseCount.ShouldBeInRange(100, 200);
    }

    private static readonly Edge[] FirstFrameEdges =
    [
        new(1_000_000, PinLevel.High), // second 0: Marker
        new(1_800_000, PinLevel.Low),
        new(2_000_000, PinLevel.High), // second 1: Zero
        new(2_200_000, PinLevel.Low),
        new(3_000_000, PinLevel.High), // second 2: One, minute 20
        new(3_500_000, PinLevel.Low),
        new(4_000_000, PinLevel.High), // second 3: One, minute 10
        new(4_500_000, PinLevel.Low),
        new(5_000_000, PinLevel.High), // second 4: Zero
        new(5_200_000, PinLevel.Low),
        new(6_000_000, PinLevel.High), // second 5: Zero
        new(6_200_000, PinLevel.Low),
        new(7_000_000, PinLevel.High), // second 6: Zero
        new(7_200_000, PinLevel.Low),
        new(8_000_000, PinLevel.High), // second 7: Zero
        new(8_200_000, PinLevel.Low),
        new(9_000_000, PinLevel.High), // second 8: Zero
        new(9_200_000, PinLevel.Low),
        new(10_000_000, PinLevel.High), // second 9: Marker
        new(10_800_000, PinLevel.Low),
        new(11_000_000, PinLevel.High), // second 10: Zero
        new(11_200_000, PinLevel.Low),
        new(12_000_000, PinLevel.High), // second 11: Zero
        new(12_200_000, PinLevel.Low),
        new(13_000_000, PinLevel.High), // second 12: Zero
        new(13_200_000, PinLevel.Low),
        new(14_000_000, PinLevel.High), // second 13: One, hour 10
        new(14_500_000, PinLevel.Low),
        new(15_000_000, PinLevel.High), // second 14: Zero
        new(15_200_000, PinLevel.Low),
        new(16_000_000, PinLevel.High), // second 15: Zero
        new(16_200_000, PinLevel.Low),
        new(17_000_000, PinLevel.High), // second 16: Zero
        new(17_200_000, PinLevel.Low),
        new(18_000_000, PinLevel.High), // second 17: One, hour 2
        new(18_500_000, PinLevel.Low),
        new(19_000_000, PinLevel.High), // second 18: Zero
        new(19_200_000, PinLevel.Low),
        new(20_000_000, PinLevel.High), // second 19: Marker
        new(20_800_000, PinLevel.Low),
        new(21_000_000, PinLevel.High), // second 20: Zero
        new(21_200_000, PinLevel.Low),
        new(22_000_000, PinLevel.High), // second 21: Zero
        new(22_200_000, PinLevel.Low),
        new(23_000_000, PinLevel.High), // second 22: One, day 200
        new(23_500_000, PinLevel.Low),
        new(24_000_000, PinLevel.High), // second 23: One, day 100
        new(24_500_000, PinLevel.Low),
        new(25_000_000, PinLevel.High), // second 24: Zero
        new(25_200_000, PinLevel.Low),
        new(26_000_000, PinLevel.High), // second 25: Zero
        new(26_200_000, PinLevel.Low),
        new(27_000_000, PinLevel.High), // second 26: Zero
        new(27_200_000, PinLevel.Low),
        new(28_000_000, PinLevel.High), // second 27: Zero
        new(28_200_000, PinLevel.Low),
        new(29_000_000, PinLevel.High), // second 28: Zero
        new(29_200_000, PinLevel.Low),
        new(30_000_000, PinLevel.High), // second 29: Marker
        new(30_800_000, PinLevel.Low),
        new(31_000_000, PinLevel.High), // second 30: One, day 8
        new(31_500_000, PinLevel.Low),
        new(32_000_000, PinLevel.High), // second 31: Zero
        new(32_200_000, PinLevel.Low),
        new(33_000_000, PinLevel.High), // second 32: Zero
        new(33_200_000, PinLevel.Low),
        new(34_000_000, PinLevel.High), // second 33: Zero
        new(34_200_000, PinLevel.Low),
        new(35_000_000, PinLevel.High), // second 34: Zero
        new(35_200_000, PinLevel.Low),
        new(36_000_000, PinLevel.High), // second 35: Zero
        new(36_200_000, PinLevel.Low),
        new(37_000_000, PinLevel.High), // second 36: One, UT1 sign +
        new(37_500_000, PinLevel.Low),
        new(38_000_000, PinLevel.High), // second 37: Zero
        new(38_200_000, PinLevel.Low),
        new(39_000_000, PinLevel.High), // second 38: One, UT1 sign +
        new(39_500_000, PinLevel.Low),
        new(40_000_000, PinLevel.High), // second 39: Marker
        new(40_800_000, PinLevel.Low),
        new(41_000_000, PinLevel.High), // second 40: Zero
        new(41_200_000, PinLevel.Low),
        new(42_000_000, PinLevel.High), // second 41: Zero
        new(42_200_000, PinLevel.Low),
        new(43_000_000, PinLevel.High), // second 42: Zero
        new(43_200_000, PinLevel.Low),
        new(44_000_000, PinLevel.High), // second 43: Zero
        new(44_200_000, PinLevel.Low),
        new(45_000_000, PinLevel.High), // second 44: Zero
        new(45_200_000, PinLevel.Low),
        new(46_000_000, PinLevel.High), // second 45: Zero
        new(46_200_000, PinLevel.Low),
        new(47_000_000, PinLevel.High), // second 46: Zero
        new(47_200_000, PinLevel.Low),
        new(48_000_000, PinLevel.High), // second 47: One, year 20
        new(48_500_000, PinLevel.Low),
        new(49_000_000, PinLevel.High), // second 48: Zero
        new(49_200_000, PinLevel.Low),
        new(50_000_000, PinLevel.High), // second 49: Marker
        new(50_800_000, PinLevel.Low),
        new(51_000_000, PinLevel.High), // second 50: Zero
        new(51_200_000, PinLevel.Low),
        new(52_000_000, PinLevel.High), // second 51: One, year 4
        new(52_500_000, PinLevel.Low),
        new(53_000_000, PinLevel.High), // second 52: One, year 2
        new(53_500_000, PinLevel.Low),
        new(54_000_000, PinLevel.High), // second 53: Zero
        new(54_200_000, PinLevel.Low),
        new(55_000_000, PinLevel.High), // second 54: Zero
        new(55_200_000, PinLevel.Low),
        new(56_000_000, PinLevel.High), // second 55: Zero
        new(56_200_000, PinLevel.Low),
        new(57_000_000, PinLevel.High), // second 56: Zero
        new(57_200_000, PinLevel.Low),
        new(58_000_000, PinLevel.High), // second 57: Zero
        new(58_200_000, PinLevel.Low),
        new(59_000_000, PinLevel.High), // second 58: Zero
        new(59_200_000, PinLevel.Low),
        new(60_000_000, PinLevel.High), // second 59: Marker
        new(60_800_000, PinLevel.Low),
    ];

    /// <summary>
    /// Helper method to read a specified number of events from the synthetic edge source.
    /// </summary>
    private static async Task<List<EdgeEvent>> TakeAsync(SyntheticEdgeSource source, int count)
    {
        List<EdgeEvent> events = [];
        await foreach (EdgeEvent evt in source.ReadAsync(TestContext.Current.CancellationToken))
        {
            events.Add(evt);
            if (events.Count == count)
            {
                break;
            }
        }

        return events;
    }

    private static async Task<List<Edge>> ReadSecondsAsync(
        SyntheticEdgeSource source,
        ulong seconds
    )
    {
        List<Edge> edges = [];
        await foreach (EdgeEvent evt in source.ReadAsync(TestContext.Current.CancellationToken))
        {
            if (evt is not EdgeEvent.Sample sample)
            {
                continue;
            }

            if (sample.Edge.Micros >= seconds * 1_000_000UL)
            {
                break;
            }
            edges.Add(sample.Edge);
        }

        return edges;
    }

    private async Task<List<Fault>> ClassifySecondsAsync(
        SyntheticEdgeSource noisySource,
        ulong seconds
    )
    {
        List<Fault> faults = [];
        SyntheticEdgeSource cleanSource = new(_timeProvider)
        {
            StartUtc = noisySource.StartUtc,
            Speed = 0.0,
            Noise = 0.0,
            ReducedCarrierLevel = noisySource.ReducedCarrierLevel,
        };

        var cleanEdges = await ReadSecondsAsync(cleanSource, seconds);
        var noisyEdges = await ReadSecondsAsync(noisySource, seconds);

        var noisy = noisyEdges.ToLookup(edge => edge.Micros / 1_000_000UL);
        var clean = cleanEdges.ToLookup(edge => edge.Micros / 1_000_000UL);

        // classify
        for (ulong second = 0; second < seconds; second++)
        {
            faults.Add(Classify([.. noisy[second]], [.. clean[second]]));
        }
        return faults;
    }

    private static Fault Classify(List<Edge> edges, List<Edge> cleanEdges)
    {
        Edge cleanEnter = cleanEdges[0];
        Edge cleanLeave = cleanEdges[1];

        switch (edges.Count)
        {
            case 1:
                if (edges[0] == cleanEnter)
                {
                    return Fault.DroppedLeaving;
                }
                edges[0].ShouldBe(cleanLeave);
                return Fault.DroppedEntering;

            case 2:
                edges[0].ShouldBe(cleanEnter);
                edges[1].Level.ShouldBe(cleanLeave.Level);
                long cleanDuration = (long)(cleanLeave.Micros - cleanEnter.Micros);
                long duration = (long)(edges[1].Micros - edges[0].Micros);
                duration.ShouldBeInRange(
                    Math.Clamp(cleanDuration - 300_000, 10_000, 990_000),
                    Math.Clamp(cleanDuration + 300_000, 10_000, 990_000)
                );
                return duration == cleanDuration ? Fault.None : Fault.Jitter;

            case 4:
                edges[0].ShouldBe(cleanEnter);
                edges[1].Level.ShouldBe(cleanLeave.Level);
                edges[2].Level.ShouldBe(cleanEnter.Level);
                edges[3].Level.ShouldBe(cleanLeave.Level);

                if (edges[1] == cleanLeave)
                {
                    (edges[3].Micros - edges[2].Micros).ShouldBeInRange(10_000UL, 40_000UL);
                    edges[2].Micros.ShouldBeGreaterThanOrEqualTo(cleanLeave.Micros + 60_000UL);
                    edges[3].Micros.ShouldBeLessThanOrEqualTo(cleanEnter.Micros + 940_000UL);
                    return Fault.PulseGlitch;
                }

                edges[3].ShouldBe(cleanLeave);
                (edges[2].Micros - edges[1].Micros).ShouldBeInRange(10_000UL, 40_000UL);
                edges[1].Micros.ShouldBeGreaterThanOrEqualTo(cleanEnter.Micros + 20_000UL);
                (edges[2].Micros + 20_000UL).ShouldBeLessThanOrEqualTo(cleanLeave.Micros);
                return Fault.GapGlitch;
            default:
                throw new ShouldAssertException($"{edges.Count} edges at {cleanEnter.Micros}");
        }
    }

    private static IEnumerable<Edge> ExpectedFrameEdges(DateTimeOffset start, int minute)
    {
        ulong pulseStart = 1_000_000UL + (ulong)minute * 60_000_000UL;
        Frame frame = WwvbFrameEncoder.Encode(
            start.AddMinutes(minute),
            pulseStart,
            Ut1Sign.Plus,
            0,
            false,
            DstStatus.Standard
        );

        foreach (
            ulong duration in frame.Symbols.Select(symbol =>
                symbol switch
                {
                    SymbolKind.Zero => 200_000UL,
                    SymbolKind.One => 500_000UL,
                    SymbolKind.Marker => 800_000UL,
                    _ => throw new InvalidOperationException($"Unexpected symbol: {symbol}"),
                }
            )
        )
        {
            yield return new Edge(pulseStart, PinLevel.High);
            yield return new Edge(pulseStart + duration, PinLevel.Low);
            pulseStart += 1_000_000;
        }
    }
}
