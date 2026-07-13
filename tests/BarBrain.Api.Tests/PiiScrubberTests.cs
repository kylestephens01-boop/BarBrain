using BarBrain.Api.Monitoring;

namespace BarBrain.Api.Tests;

public class PiiScrubberTests
{
    [Theory]
    [InlineData("duplicate key for kyle.stephens@example.com found",
                "duplicate key for [email] found")]
    [InlineData("two hits a@b.co and c.d+tag@e-f.org here",
                "two hits [email] and [email] here")]
    [InlineData("connection string Password=hunter2 rejected",
                "connection string Password=[redacted]")] // to end of line, on purpose
    [InlineData("header Authorization: Bearer abc.def.ghi",
                "header Authorization=[redacted]")]
    [InlineData("token=tok_123 cookie: bb_session=xyz",
                "token=[redacted]")] // one hit consumes the rest of the line
    [InlineData("plain exception about a drink named Weller 12", // untouched
                "plain exception about a drink named Weller 12")]
    public void Scrubs_pii_and_credentials_only(string input, string expected)
        => Assert.Equal(expected, PiiScrubber.Scrub(input));

    [Fact]
    public void Null_and_empty_are_safe()
    {
        Assert.Equal("", PiiScrubber.Scrub(null));
        Assert.Equal("", PiiScrubber.Scrub(""));
    }
}
