using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Challenges.Management;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

[Category("Integration")]
public sealed class ChallengeConfigurationTests
{
    [Test, Timeout(300_000), Arguments(CompetitionStatus.Draft), Arguments(CompetitionStatus.Running), Arguments(CompetitionStatus.Paused), Arguments(CompetitionStatus.Finished)]
    public async Task Rule_updates_persist_current_values_across_lifecycle_states(CompetitionStatus status, CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(status, ct);
        var created = await new CreateChallenge(fixture.Challenges, fixture.Catalog).ExecuteAsync(new(null, fixture.CompetitionId, fixture.TemplateId, 0, fixture.Now), ct);
        await Assert.That(created.Failure).IsNull();
        var entryId = created.Challenge!.Id;
        var update = new UpdateChallengeConfiguration(fixture.Configurations, fixture.Catalog);
        var first = await update.ExecuteAsync(fixture.CompetitionId, entryId, new CtfCompetitionChallengeRules { MaxFlagAttempts = 2 }, fixture.Now.AddSeconds(1), ct);
        var last = await update.ExecuteAsync(fixture.CompetitionId, entryId, new CtfCompetitionChallengeRules { MaxFlagAttempts = 3 }, fixture.Now.AddSeconds(2), ct);
        await Assert.That(first.ErrorMessage).IsNull(); await Assert.That(last.ErrorMessage).IsNull();
        await Assert.That(first.Succeeded).IsTrue(); await Assert.That(last.Succeeded).IsTrue();
        fixture.Db.ChangeTracker.Clear();
        var current = await fixture.Configurations.FindAsync(fixture.CompetitionId, entryId, ct);
        await Assert.That(((CtfCompetitionChallengeRules)current!.Rules).MaxFlagAttempts).IsEqualTo(3);
        await fixture.AssertPublishedEventsAsync(ct);
    }

    [Test, Timeout(300_000)]
    public async Task Invalid_configuration_does_not_change_rules_or_append_events(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Draft, ct);
        var created = await new CreateChallenge(fixture.Challenges, fixture.Catalog).ExecuteAsync(new(null, fixture.CompetitionId, fixture.TemplateId, 0, fixture.Now), ct);
        var before = await fixture.Db.CompetitionEvents.CountAsync(ct);
        var result = await new UpdateChallengeConfiguration(fixture.Configurations, fixture.Catalog).ExecuteAsync(fixture.CompetitionId,
            created.Challenge!.Id, new CtfCompetitionChallengeRules { MaxFlagAttempts = -1 }, fixture.Now.AddSeconds(1), ct);
        await Assert.That(result.FailureCode).IsEqualTo(ChallengeConfigurationFailureCode.InvalidConfiguration);
        fixture.Db.ChangeTracker.Clear();
        var current = await fixture.Configurations.FindAsync(fixture.CompetitionId, created.Challenge.Id, ct);
        await Assert.That(((CtfCompetitionChallengeRules)current!.Rules).MaxFlagAttempts).IsNull();
        await Assert.That(await fixture.Db.CompetitionEvents.CountAsync(ct)).IsEqualTo(before);
    }
}
