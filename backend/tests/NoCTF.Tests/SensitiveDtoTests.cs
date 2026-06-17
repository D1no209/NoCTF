using NoCTF.API.Endpoints.Admin;

namespace NoCTF.Tests;

public class SensitiveDtoTests
{
    [Fact]
    public void ChallengeAdminDto_DoesNotExposeFlagSecret()
    {
        var property = typeof(ChallengeAdminDto).GetProperty("FlagSecret");

        Assert.Null(property);
    }
}
