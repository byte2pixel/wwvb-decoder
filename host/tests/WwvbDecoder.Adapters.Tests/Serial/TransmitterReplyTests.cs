namespace WwvbDecoder.Adapters.Tests;

public class TransmitterReplyTests
{
    // 1
    [Theory]
    [InlineData("OK time=1796931000 carrier=on custom=none", true)]
    [InlineData("OK", true)]
    [InlineData("ERR time is outside 2000 to 2099", false)]
    [InlineData("ERR OK", false)]
    // The first word is the whole word, in upper case, at the start of the line.
    [InlineData("OKAY time=1796931000", false)]
    [InlineData("ok time=1796931000", false)]
    [InlineData(" OK time=1796931000", false)]
    [InlineData("", false)]
    public void Ok_IsWhetherTheFirstWordIsOk(string line, bool ok)
    {
        new TransmitterReply(line).Ok.ShouldBe(ok);
    }
}
