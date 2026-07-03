using NoCTF.API.Endpoints.Admin;

namespace NoCTF.Tests;

public class SensitiveDtoTests
{
    [Fact]
    public void ChallengeTemplateAdminDto_DoesNotExposeFlagSecret()
    {
        var property = typeof(ChallengeTemplateAdminDto).GetProperty("FlagSecret");

        Assert.Null(property);
    }
}
