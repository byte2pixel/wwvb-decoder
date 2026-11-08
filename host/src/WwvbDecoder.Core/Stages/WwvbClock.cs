namespace WwvbDecoder.Core;

/// <summary>
/// Stage 7: the WWVB clock.
/// Tracks lock status from validation results and holds the last good time.
/// </summary>
public sealed class WwvbClock : IStage<ValidationResult, ClockState>
{
    private const int AcceptedToLock = 2;
    private const int RejectedToDropHold = 3;

    // A frame is pushed when the next frame's second 0 begins, 60 s after the instant it encodes.
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

    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates a clock that reads the PC time from <paramref name="timeProvider"/>.
    /// </summary>
    /// <param name="timeProvider">The PC clock that <see cref="ClockState.OffsetFromPc"/> is measured against.</param>
    public WwvbClock(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// The state after the last push. <see cref="ClockStatus.NoSignal"/> with nulls and zeros
    /// before the first push and after <see cref="Reset"/>.
    /// </summary>
    public ClockState Current { get; private set; } = NoSignal;

    /// <summary>
    /// Updates the clock with one validation result.
    /// </summary>
    /// <param name="input">The validated frame.</param>
    /// <returns>One state, which is also <see cref="Current"/>.</returns>
    public IEnumerable<ClockState> Push(ValidationResult input)
    {
        ArgumentNullException.ThrowIfNull(input);

        Current = input.Accepted ? Accept(input.Frame) : Reject();

        return [Current];
    }

    /// <summary>
    /// Returns to <see cref="ClockStatus.NoSignal"/> with nulls and zero counters.
    /// </summary>
    public void Reset()
    {
        Current = NoSignal;
    }

    private bool WasLocked => Current.Status is ClockStatus.Locked or ClockStatus.Holding;

    private ClockState Accept(DecodedFrame frame)
    {
        int accepted = Current.ConsecutiveAccepted + 1;
        ClockStatus status =
            WasLocked || accepted >= AcceptedToLock ? ClockStatus.Locked : ClockStatus.Acquiring;
        DateTimeOffset frameUtc = new(FrameValidator.FrameTime(frame));

        return new ClockState(
            status,
            frameUtc,
            frame.Frame.StartMicros,
            frame.Frame.EndMicros,
            frameUtc + PushDelay - _timeProvider.GetUtcNow(),
            accepted,
            0
        );
    }

    /// <summary>
    /// The last good time, its micros, and its offset stay as they were.
    /// </summary>
    private ClockState Reject()
    {
        int rejected = Current.RejectedSinceLastAccepted + 1;
        ClockStatus status =
            WasLocked && rejected < RejectedToDropHold
                ? ClockStatus.Holding
                : ClockStatus.Acquiring;

        return Current with
        {
            Status = status,
            ConsecutiveAccepted = 0,
            RejectedSinceLastAccepted = rejected,
        };
    }
}
