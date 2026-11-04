namespace WwvbDecoder.Core;

/// <summary>
/// Status of the WWVB clock.
/// </summary>
public enum ClockStatus
{
    /// <summary>
    /// No frame since construction or <c>Reset()</c>.
    /// </summary>
    NoSignal,

    /// <summary>
    /// Frames arriving, but fewer than 2 accepted in a row
    /// </summary>
    Acquiring,

    /// <summary>
    /// At least 2 consecutive accepted frames, or an accepted frame while <see cref="Holding"/>
    /// </summary>
    Locked,

    /// <summary>
    /// Was locked. The last 1 or 2 frames were rejected, and the last good time is still shown.
    /// The third rejection drops to <see cref="Acquiring"/>.
    /// </summary>
    Holding,
}
