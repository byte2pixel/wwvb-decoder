namespace WwvbDecoder.Core;

/// <summary>
/// Sign of the UT1 correction, from seconds 36 to 38.
/// </summary>
public enum Ut1Sign
{
    /// <summary>
    /// Reading <c>1 0 1</c>
    /// </summary>
    Plus,

    /// <summary>
    /// Reading <c>0 1 0</c>
    /// </summary>
    Minus,

    /// <summary>
    /// Reading any other pattern than <c>1 0 1</c> or <c>0 1 0</c>
    /// </summary>
    Invalid,
}
