namespace WwvbDecoder.Core;

/// <summary>
/// One step of the decoder pipeline: each input produces zero or more outputs.
/// </summary>
/// <typeparam name="TIn">What the previous stage produces.</typeparam>
/// <typeparam name="TOut">What this stage produces for the next one.</typeparam>
/// <remarks>
/// Synchronous and single-threaded; the stage owns its state.
/// </remarks>
public interface IStage<in TIn, out TOut>
{
    /// <summary>
    /// Processes one input and returns its outputs as a materialized collection, never a lazy
    /// iterator, so calling <c>Push</c> without enumerating the result still advances the stage.
    /// </summary>
    IEnumerable<TOut> Push(TIn input);

    /// <summary>
    /// Discards state so the next input is treated as the first. Called on every source
    /// <see cref="EdgeEvent.Reset"/>, because timestamps before and after a reset are not comparable.
    /// </summary>
    void Reset();
}
