using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Management;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

[Category("Integration")]
public sealed class ChallengeManagementTests
{
    [Test, Timeout(300_000)]
    public async Task Normalization_partial_updates_visibility_and_soft_deletion_use_real_storage(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Running, ct);
        var create = new CreateChallenge(fixture.Challenges, fixture.Catalog);
        var created = await create.ExecuteAsync(new(null, fixture.CompetitionId, fixture.TemplateId, 0, fixture.Now,
            CustomTitle: "  Custom  ", Tags: [" Web ", "web", " SQL "]), ct);
        await Assert.That(created.Failure).IsNull(); var id = created.Challenge!.Id;
        await Assert.That(created.Challenge.CustomTitle).IsEqualTo("Custom");
        await Assert.That(created.Challenge.Tags).IsEquivalentTo(["Web", "SQL"]);
        var update = new UpdateChallenge(fixture.Challenges);
        var unchangedTags = await update.ExecuteAsync(new(fixture.CompetitionId, id, 0, false, fixture.Now.AddSeconds(1), CustomTitle: " "), ct);
        await Assert.That(unchangedTags.Failure).IsNull();
        await Assert.That(unchangedTags.Challenge!.Title).IsEqualTo("Reusable");
        await Assert.That(unchangedTags.Challenge.Tags).IsEquivalentTo(["Web", "SQL"]);
        await Assert.That(await new ListChallenges(fixture.Challenges).ExecuteAsync(fixture.CompetitionId, false, ct: ct)).IsEmpty();
        await Assert.That((await new ListChallenges(fixture.Challenges).ExecuteAsync(fixture.CompetitionId, true, ct: ct)).Count).IsEqualTo(1);
        var cleared = await update.ExecuteAsync(new(fixture.CompetitionId, id, 0, false, fixture.Now.AddSeconds(2), Tags: []), ct);
        await Assert.That(cleared.Challenge!.Tags).IsEmpty();
        var delete = new DeleteChallenge(fixture.Challenges);
        await Assert.That(await delete.ExecuteAsync(fixture.CompetitionId, id, fixture.Now.AddSeconds(3), ct)).IsNull();
        await Assert.That(await new GetChallenge(fixture.Challenges).ExecuteAsync(fixture.CompetitionId, id, true, ct: ct)).IsNull();
        await Assert.That(await delete.RestoreAsync(fixture.CompetitionId, id, fixture.Now.AddSeconds(4), ct)).IsNull();
        await Assert.That(await new GetChallenge(fixture.Challenges).ExecuteAsync(fixture.CompetitionId, id, true, ct: ct)).IsNotNull();
        await fixture.AssertPublishedEventsAsync(ct);
    }

    [Test, Timeout(300_000)]
    public async Task Conflicting_identity_order_and_template_return_their_actual_failures(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Draft, ct);
        var create = new CreateChallenge(fixture.Challenges, fixture.Catalog); var id = Guid.NewGuid();
        await Assert.That((await create.ExecuteAsync(new(id, fixture.CompetitionId, fixture.TemplateId, 0, fixture.Now), ct)).Failure).IsNull();
        await Assert.That((await create.ExecuteAsync(new(id, fixture.CompetitionId, fixture.TemplateId, 1, fixture.Now), ct)).Failure).IsEqualTo(ChallengeMutationFailure.ResourceIdConflict);
        var other = new Challenge { Id = Guid.NewGuid(), OwnerId = fixture.OwnerId, Title = "Other", Direction = "Web",
            Definition = new() { Capabilities = [new NoCTF.Domain.Challenges.Capabilities.BreakCapabilityDefinition()] } };
        fixture.Db.Challenges.Add(other); await fixture.Db.SaveChangesAsync(ct);
        await Assert.That((await create.ExecuteAsync(new(null, fixture.CompetitionId, other.Id, 0, fixture.Now), ct)).Failure).IsEqualTo(ChallengeMutationFailure.ChallengeOrderConflict);
        await Assert.That((await create.ExecuteAsync(new(null, fixture.CompetitionId, fixture.TemplateId, 1, fixture.Now), ct)).Failure).IsEqualTo(ChallengeMutationFailure.ChallengeTemplateConflict);
        var count = await fixture.Db.CompetitionChallenges.CountAsync(ct);
        foreach (var invalid in new[]
        {
            new CreateCompetitionChallengeCommand(null, fixture.CompetitionId, other.Id, -1, fixture.Now),
            new CreateCompetitionChallengeCommand(null, fixture.CompetitionId, other.Id, 2, fixture.Now, CustomTitle: new string('x', 161)),
            new CreateCompetitionChallengeCommand(null, fixture.CompetitionId, other.Id, 2, fixture.Now, Tags: [" "])
        }) await Assert.That((await create.ExecuteAsync(invalid, ct)).Failure).IsNotNull();
        await Assert.That(await fixture.Db.CompetitionChallenges.CountAsync(ct)).IsEqualTo(count);
    }
}
