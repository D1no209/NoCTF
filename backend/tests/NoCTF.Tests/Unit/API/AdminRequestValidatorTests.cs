using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.API.Endpoints.Administration.Teams;
using NoCTF.API.Endpoints.Teams;
using NoCTF.API.Endpoints.Competitions;

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
            Mode = GameModeProtocol.Ctf,
            StartTime = now,
            EndTime = now.AddHours(1),
            MaxTeamMembers = 5
        };
        var update = new PatchCompetitionRequest
        {
            Metadata = new()
            {
                Title = "CTF",
                Description = null,
                StartTime = now,
                EndTime = now.AddHours(1),
                TeamRegistrationAutoApprove = false,
                AllowTeamRegistrationWhileRunning = false,
                MaxTeamMembers = 5,
                MaxConcurrentRuntimeInstancesPerTeam = -1,
                MaxActiveQuestionsPerTeam = 5,
                MaxParticipantMessagesBeforeHandlerReply = 3,
                AllowChallengeOwnersToHandleQuestions = true,
                PracticeModeEnabled = false
            }
        };

        await Assert.That((await new CreateCompetitionValidator()
                .ValidateAsync(create)).IsValid)
            .IsFalse();
        await Assert.That((await new PatchCompetitionValidator()
                .ValidateAsync(update)).IsValid)
            .IsFalse();

        create.MaxConcurrentRuntimeInstancesPerTeam = 0;
        update.Metadata!.MaxConcurrentRuntimeInstancesPerTeam = 0;
        await Assert.That((await new CreateCompetitionValidator()
                .ValidateAsync(create)).IsValid)
            .IsTrue();
        await Assert.That((await new PatchCompetitionValidator()
                .ValidateAsync(update)).IsValid)
            .IsTrue();
    }

    [Test]
    public async Task BanTeam_RequiresAReason()
    {
        var validator = new PatchTeamValidator();

        var missing = await validator.ValidateAsync(new PatchTeamRequest
        {
            Ban = new() { IsBanned = true, Reason = " " }
        });
        var supplied = await validator.ValidateAsync(new PatchTeamRequest
        {
            Ban = new() { IsBanned = true, Reason = "Rule violation" }
        });

        await Assert.That(missing.IsValid).IsFalse();
        await Assert.That(missing.Errors.Any(error => string.Equals(
            error.PropertyName,
            "Ban.Reason",
            StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(supplied.IsValid).IsTrue();
    }
}
