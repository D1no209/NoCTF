using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.API.Endpoints.Administration.Teams;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Api;

public sealed class AdminRequestValidatorTests
{
    [Test]
    public async Task Competition_runtime_quota_is_required_and_accepts_zero_as_unlimited()
    {
        var now = DateTimeOffset.UtcNow;
        var create = new CreateCompetitionRequest
        {
            Title = "CTF",
            Mode = GameMode.Ctf,
            StartTime = now,
            EndTime = now.AddHours(1),
            MaxTeamMembers = 5
        };
        var update = new UpdateCompetitionRequest
        {
            Title = "CTF",
            StartTime = now,
            EndTime = now.AddHours(1),
            MaxTeamMembers = 5
        };

        await Assert.That((await new CreateCompetitionValidator()
                .ValidateAsync(create)).IsValid)
            .IsFalse();
        await Assert.That((await new UpdateCompetitionValidator()
                .ValidateAsync(update)).IsValid)
            .IsFalse();

        create.MaxConcurrentRuntimeInstancesPerTeam = 0;
        update.MaxConcurrentRuntimeInstancesPerTeam = 0;
        await Assert.That((await new CreateCompetitionValidator()
                .ValidateAsync(create)).IsValid)
            .IsTrue();
        await Assert.That((await new UpdateCompetitionValidator()
                .ValidateAsync(update)).IsValid)
            .IsTrue();
    }

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
