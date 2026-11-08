using System.Globalization;
using Microsoft.Extensions.Time.Testing;

namespace WwvbDecoder.Core.Tests;

public class WwvbClockTests
{
    private const ulong FirstStart = 1_000_000;
    private const ulong MinuteMicros = 60_000_000;

    private static readonly TimeSpan PushDelay = TimeSpan.FromSeconds(60);

    private static readonly ClockState NoSignal = new(
        ClockStatus.NoSignal,
        null,
        null,
        null,
        null,
        0,
        0
    );

    // Frame n encodes CleanMinute + n minutes and starts n minutes after FirstStart.
    private static DecodedFrame FrameAt(int n) =>
        TestFrames.Decode(
            TestFrames.Encode(
                TestFrames.CleanMinute.AddMinutes(n),
                FirstStart + ((ulong)n * MinuteMicros)
            )
        );

    private static ValidationResult Accepted(DecodedFrame frame) => new(frame, true, []);

    private static ValidationResult Rejected(DecodedFrame frame) =>
        new(frame, false, [new ValidationFailure(ValidationCheck.Markers, "second 19 is Zero")]);

    private static FakeTimeProvider PushTimeFor(DateTimeOffset frameUtc) =>
        new(frameUtc + PushDelay);

    private static ClockState PushOne(WwvbClock clock, ValidationResult input) =>
        clock.Push(input).ShouldHaveSingleItem();

    [Fact]
    public void Constructor_NullTimeProvider_Throws()
    {
        // Act
        Action act = () => _ = new WwvbClock(null!);

        // Assert
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void Current_BeforeFirstPush_IsNoSignal()
    {
        // Arrange
        WwvbClock clock = new(new FakeTimeProvider());

        // Act
        ClockState current = clock.Current;

        // Assert
        current.ShouldBe(NoSignal);
    }

    [Fact]
    public void Push_Null_Throws()
    {
        // Arrange
        WwvbClock clock = new(new FakeTimeProvider());

        // Act
        Action act = () => clock.Push(null!);

        // Assert
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void Push_ReturnsCurrent()
    {
        // Arrange
        WwvbClock clock = new(PushTimeFor(TestFrames.CleanMinute));

        // Act
        ClockState state = PushOne(clock, Accepted(FrameAt(0)));

        // Assert
        state.ShouldBeSameAs(clock.Current);
    }

    // A is an accepted frame, R a rejected one. Each expected step is Status/ConsecutiveAccepted/RejectedSinceLastAccepted.
    [Theory]
    [InlineData("A", "Acquiring/1/0")]
    [InlineData("R", "Acquiring/0/1")]
    [InlineData("R R R R", "Acquiring/0/1 Acquiring/0/2 Acquiring/0/3 Acquiring/0/4")]
    [InlineData("A A", "Acquiring/1/0 Locked/2/0")]
    [InlineData("A A A", "Acquiring/1/0 Locked/2/0 Locked/3/0")]
    [InlineData("R A A", "Acquiring/0/1 Acquiring/1/0 Locked/2/0")]
    [InlineData("A R A", "Acquiring/1/0 Acquiring/0/1 Acquiring/1/0")]
    [InlineData("A A R", "Acquiring/1/0 Locked/2/0 Holding/0/1")]
    [InlineData("A A R R", "Acquiring/1/0 Locked/2/0 Holding/0/1 Holding/0/2")]
    [InlineData("A A R R R", "Acquiring/1/0 Locked/2/0 Holding/0/1 Holding/0/2 Acquiring/0/3")]
    [InlineData(
        "A A R R R R",
        "Acquiring/1/0 Locked/2/0 Holding/0/1 Holding/0/2 Acquiring/0/3 Acquiring/0/4"
    )]
    [InlineData("A A R A", "Acquiring/1/0 Locked/2/0 Holding/0/1 Locked/1/0")]
    [InlineData("A A R R A", "Acquiring/1/0 Locked/2/0 Holding/0/1 Holding/0/2 Locked/1/0")]
    [InlineData(
        "A A R R A R R",
        "Acquiring/1/0 Locked/2/0 Holding/0/1 Holding/0/2 Locked/1/0 Holding/0/1 Holding/0/2"
    )]
    [InlineData(
        "A A R R R A",
        "Acquiring/1/0 Locked/2/0 Holding/0/1 Holding/0/2 Acquiring/0/3 Acquiring/1/0"
    )]
    [InlineData(
        "A A R R R A A",
        "Acquiring/1/0 Locked/2/0 Holding/0/1 Holding/0/2 Acquiring/0/3 Acquiring/1/0 Locked/2/0"
    )]
    public void Push_Sequence_FollowsStatusTable(string inputs, string expectedSteps)
    {
        // Arrange
        WwvbClock clock = new(PushTimeFor(TestFrames.CleanMinute));
        string[] pushes = inputs.Split(' ');
        string[] expected = expectedSteps.Split(' ');
        pushes.Length.ShouldBe(expected.Length);

        // Act
        string[] actual =
        [
            .. pushes
                .Select(
                    (push, n) =>
                        PushOne(clock, push == "A" ? Accepted(FrameAt(n)) : Rejected(FrameAt(n)))
                )
                .Select(s =>
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{s.Status}/{s.ConsecutiveAccepted}/{s.RejectedSinceLastAccepted}"
                    )
                ),
        ];

        // Assert
        actual.ShouldBe(expected);
    }

    [Fact]
    public void Push_Accepted_SetsFrameValues()
    {
        // Arrange
        DecodedFrame frame = FrameAt(0);
        WwvbClock clock = new(PushTimeFor(TestFrames.CleanMinute));

        // Act
        ClockState state = PushOne(clock, Accepted(frame));

        // Assert
        state.FrameUtc.ShouldBe(TestFrames.CleanMinute);
        state.FrameUtc!.Value.Offset.ShouldBe(TimeSpan.Zero);
        state.FrameStartMicros.ShouldBe(frame.Frame.StartMicros);
        state.FrameEndMicros.ShouldBe(frame.Frame.EndMicros);
        state.OffsetFromPc.ShouldBe(TimeSpan.Zero);
    }

    // The PC reads the frame's minute + 60 s + pcLateMs at push time.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(2_000, -2_000)]
    [InlineData(-1_500, 1_500)]
    [InlineData(-200, 200)]
    public void Push_Accepted_OffsetIsNextMinuteMinusPcTime(int pcLateMs, int expectedOffsetMs)
    {
        // Arrange
        WwvbClock clock = new(
            PushTimeFor(TestFrames.CleanMinute + TimeSpan.FromMilliseconds(pcLateMs))
        );

        // Act
        ClockState state = PushOne(clock, Accepted(FrameAt(0)));

        // Assert
        state.OffsetFromPc.ShouldBe(TimeSpan.FromMilliseconds(expectedOffsetMs));
    }

    [Fact]
    public void Push_Accepted_OffsetIsTakenDuringPush()
    {
        // Arrange
        FakeTimeProvider pc = PushTimeFor(TestFrames.CleanMinute);
        WwvbClock clock = new(pc);
        PushOne(clock, Accepted(FrameAt(0)));

        // Act
        pc.Advance(TimeSpan.FromSeconds(5));

        // Assert
        clock.Current.OffsetFromPc.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("2026-12-10T19:30Z")]
    [InlineData("2027-01-01T00:00Z")]
    [InlineData("2026-12-31T23:59Z")]
    [InlineData("2028-02-29T12:00Z")]
    [InlineData("2028-12-31T23:59Z")]
    [InlineData("2099-12-31T23:59Z")]
    public void Push_Accepted_FrameUtcIsTheEncodedMinute(string utc)
    {
        // Arrange
        DateTimeOffset minute = DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture);
        WwvbClock clock = new(PushTimeFor(minute));

        // Act
        ClockState state = PushOne(clock, Accepted(TestFrames.Decode(TestFrames.Encode(minute))));

        // Assert
        state.FrameUtc.ShouldBe(minute);
        state.OffsetFromPc.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Push_Rejected_KeepsLastGoodValues()
    {
        // Arrange
        FakeTimeProvider pc = PushTimeFor(TestFrames.CleanMinute);
        WwvbClock clock = new(pc);
        ClockState good = PushOne(clock, Accepted(FrameAt(0)));
        pc.Advance(TimeSpan.FromSeconds(60));

        // Act
        ClockState state = PushOne(clock, Rejected(FrameAt(1)));

        // Assert
        state.FrameUtc.ShouldBe(good.FrameUtc);
        state.FrameStartMicros.ShouldBe(good.FrameStartMicros);
        state.FrameEndMicros.ShouldBe(good.FrameEndMicros);
        state.OffsetFromPc.ShouldBe(good.OffsetFromPc);
    }

    [Fact]
    public void Push_RejectedFirst_LeavesValuesNull()
    {
        // Arrange
        WwvbClock clock = new(PushTimeFor(TestFrames.CleanMinute));

        // Act
        ClockState state = PushOne(clock, Rejected(FrameAt(0)));

        // Assert
        state.ShouldBe(
            NoSignal with
            {
                Status = ClockStatus.Acquiring,
                RejectedSinceLastAccepted = 1,
            }
        );
    }

    [Fact]
    public void Reset_ReturnsToNoSignal()
    {
        // Arrange
        WwvbClock clock = new(PushTimeFor(TestFrames.CleanMinute));
        PushOne(clock, Accepted(FrameAt(0)));
        PushOne(clock, Accepted(FrameAt(1))).Status.ShouldBe(ClockStatus.Locked);

        // Act
        clock.Reset();

        // Assert
        clock.Current.ShouldBe(NoSignal);
    }

    [Fact]
    public void Reset_ThenAccepted_StartsAcquiringAgain()
    {
        // Arrange
        WwvbClock clock = new(PushTimeFor(TestFrames.CleanMinute));
        PushOne(clock, Accepted(FrameAt(0)));
        PushOne(clock, Accepted(FrameAt(1)));
        clock.Reset();

        // Act
        ClockState state = PushOne(clock, Accepted(FrameAt(2)));

        // Assert
        state.Status.ShouldBe(ClockStatus.Acquiring);
        state.ConsecutiveAccepted.ShouldBe(1);
    }

    [Fact]
    public void Push_ResultNotEnumerated_StillUpdatesCurrent()
    {
        // Arrange
        WwvbClock clock = new(PushTimeFor(TestFrames.CleanMinute));

        // Act
        _ = clock.Push(Accepted(FrameAt(0)));

        // Assert
        clock.Current.Status.ShouldBe(ClockStatus.Acquiring);
    }
}
