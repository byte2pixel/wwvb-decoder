namespace WwvbDecoder.Core;

/// <summary>
/// The state of the WWVB clock.
/// </summary>
/// <param name="Status">The current status of the clock.</param>
/// <param name="FrameUtc">UTC at second 0 of the last accepted frame.</param>
/// <param name="FrameStartMicros">Start time from the last accepted frame, in microseconds. Null before the first.</param>
/// <param name="FrameEndMicros">End time from the last accepted frame, in microseconds. Null before the first.</param>
/// <param name="OffsetFromPc">Positive when WWVB is ahead of the PC clock. Null until the first accepted frame.</param>
/// <param name="ConsecutiveAccepted">Reset to 0 by a rejection.</param>
/// <param name="RejectedSinceLastAccepted">Reset to 0 by an acceptance. 3 drops <see cref="ClockStatus.Holding"/> to <see cref="ClockStatus.Acquiring"/>.</param>
public sealed record ClockState(
    ClockStatus Status,
    DateTimeOffset? FrameUtc,
    ulong? FrameStartMicros,
    ulong? FrameEndMicros,
    TimeSpan? OffsetFromPc,
    int ConsecutiveAccepted,
    int RejectedSinceLastAccepted
);
