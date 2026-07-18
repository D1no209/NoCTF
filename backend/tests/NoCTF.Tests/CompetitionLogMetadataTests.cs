using NoCTF.API.Endpoints.Admin;

namespace NoCTF.Tests;

public class CompetitionLogMetadataTests
{
    [Fact]
    public void ReadSubmittedFlag_ReturnsExactStoredValue()
    {
        const string metadata = "{\"submittedFlag\":\"flag{player-input}\",\"submissionId\":\"test\"}";

        Assert.Equal("flag{player-input}", CompetitionLogMetadata.ReadSubmittedFlag(metadata));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("{\"submittedFlag\":42}")]
    public void ReadSubmittedFlag_ReturnsNullWhenUnavailable(string? metadata)
    {
        Assert.Null(CompetitionLogMetadata.ReadSubmittedFlag(metadata));
    }
}
