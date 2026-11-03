namespace WwvbDecoder.Core;

public interface IStage<in TIn, out TOut>
{
    IEnumerable<TOut> Push(TIn input);
    void Reset();
}
