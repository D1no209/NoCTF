using NoCTF.API.Endpoints.Administration.Teams;

namespace NoCTF.Tests.Unit.Api;

public sealed class AdminRequestValidatorTests
{
    [Test]
    public async Task BanTeam_RequiresAReason()
    {
        var validator = new BanTeamValidator();

        var missing = await validator.ValidateAsync(new BanTeamRequest { Reason = " " });
        var supplied = await validator.ValidateAsync(new BanTeamRequest { Reason = "Rule violation" });

        await Assert.That(missing.IsValid).IsFalse();
        await Assert.That(missing.Errors.Select(error => error.PropertyName)).Contains(nameof(BanTeamRequest.Reason));
        await Assert.That(supplied.IsValid).IsTrue();
    }
}
