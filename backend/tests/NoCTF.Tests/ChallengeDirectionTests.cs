using NoCTF.API.Endpoints.Admin;
using NoCTF.Core;

namespace NoCTF.Tests;

public class ChallengeDirectionTests
{
    [Theory]
    [InlineData("  WEB  ", "WEB")]
    [InlineData("Pwn", "PWN")]
    [InlineData(null, "Uncategorized")]
    [InlineData("   ", "Uncategorized")]
    public void NormalizeDirection_ProducesStableStoredValue(string? value, string expected)
        => Assert.Equal(expected, ChallengeTemplateRequestRules.NormalizeDirection(value));

    [Fact]
    public void AdminMapping_KeepsChallengeTypeAndDirectionSeparate()
    {
        var template = new ChallengeTemplate
        {
            Id = Guid.NewGuid(),
            Title = "service",
            TypeId = "Awdp",
            Direction = "AI"
        };

        var dto = ChallengeAdminMapping.ToTemplateDto(template);

        Assert.Equal("Awdp", dto.TypeId);
        Assert.Equal("AI", dto.Direction);
    }
}
