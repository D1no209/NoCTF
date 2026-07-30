using System.Text.Json;
using NoCTF.API.Endpoints.Administration.Competitions;

namespace NoCTF.Tests.Unit.API;

public sealed class CompetitionResourceManagerProtocolTests
{
    [Test]
    public async Task Conflict_code_serializes_as_a_named_enum()
    {
        var json = JsonSerializer.Serialize(
            CompetitionResourceManagerConflictCode.RoleNotEligible);

        await Assert.That(json).IsEqualTo("\"RoleNotEligible\"");
    }
}
