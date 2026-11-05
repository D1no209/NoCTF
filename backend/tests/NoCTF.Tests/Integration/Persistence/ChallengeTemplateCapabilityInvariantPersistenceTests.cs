using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Challenges.Management;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Tests.Unit.Gameplay;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeTemplateCapabilityInvariantPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Reusable_template_deploys_to_two_modes_and_summaries_filter_all_required_capabilities(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Draft, ct);
        var definition = ChallengeCapabilityTests.Repairable();
        definition.Break!.EnvironmentTargets.Add(new() { ServiceName = "web", VariableName = "CHALLENGE_FLAG" });
        var saved = await new ChallengeBankStore(fixture.Db, fixture.Publisher).UpdateAsync(new(fixture.TemplateId, fixture.OwnerId, false,
            ChallengeVisibility.Private, "Reusable", null, "Web", definition, fixture.Now), ct);
        await Assert.That(saved.Succeeded).IsTrue();
        var secondCompetitionId = Guid.NewGuid();
        fixture.Db.Competitions.Add(new AwdpCompetition { Id = secondCompetitionId, OwnerId = fixture.OwnerId, Title = "Repair competition",
            Status = CompetitionStatus.Draft, StartAt = fixture.Now.AddHours(1), EndAt = fixture.Now.AddHours(2),
            ModeConfiguration = CompetitionModeConfigurationDefaults.Create(GameMode.Awdp, secondCompetitionId) });
        await fixture.Db.SaveChangesAsync(ct);
        var ctf = await fixture.Challenges.CreateAsync(new(null, fixture.CompetitionId, fixture.TemplateId, 0, fixture.Now), new CtfCompetitionChallengeRules(), ct);
        var awdp = await fixture.Challenges.CreateAsync(new(null, secondCompetitionId, fixture.TemplateId, 0, fixture.Now), new AwdpCompetitionChallengeRules(), ct);
        await Assert.That(ctf.Failure).IsNull(); await Assert.That(awdp.Failure).IsNull();
        await Assert.That(ctf.Challenge!.ChallengeId).IsEqualTo(awdp.Challenge!.ChallengeId);
        var strict = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(fixture.Db.Database.GetConnectionString())
            .ConfigureWarnings(warnings => warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning)).Options;
        await using var read = new NoCtfDbContext(strict);
        var bank = new ChallengeBankStore(read);
        var summaries = await bank.ListPageAsync(new(fixture.OwnerId, false, false, null, null, 0, 10, false,
            RequiredCapabilities: [ChallengeCapabilityKind.Break, ChallengeCapabilityKind.Fix, ChallengeCapabilityKind.Check]), ct);
        await Assert.That(summaries.Items).HasSingleItem();
        await Assert.That(summaries.Items[0].Id).IsEqualTo(fixture.TemplateId);
        await Assert.That(summaries.Items[0].ActiveCompetitionReferenceCount).IsEqualTo(2);
        await Assert.That((await bank.ListPageAsync(new(fixture.OwnerId, false, false, null, null, 0, 10, false,
            RequiredCapabilities: [ChallengeCapabilityKind.Control]), ct)).Items).IsEmpty();
        var removed = ChallengeDefinitionContractCopy(definition);
        removed.Capabilities.RemoveAll(value => value.Kind == ChallengeCapabilityKind.Check);
        var refused = await new ChallengeBankStore(fixture.Db, fixture.Publisher).UpdateAsync(new(fixture.TemplateId, fixture.OwnerId, false,
            ChallengeVisibility.Private, "Reusable", null, "Web", removed, fixture.Now.AddSeconds(1)), ct);
        await Assert.That(refused.State).IsEqualTo(ChallengeTemplateWriteState.ReferencedCapabilityConflict);
    }

    [Test, Timeout(300_000)]
    public async Task Missing_break_is_reported_without_creating_a_ctf_deployment(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Draft, ct);
        var definition = ChallengeCapabilityTests.Repairable();
        definition.Capabilities.RemoveAll(value => value.Kind == ChallengeCapabilityKind.Break);
        var saved = await new ChallengeBankStore(fixture.Db, fixture.Publisher).UpdateAsync(new(fixture.TemplateId, fixture.OwnerId, false,
            ChallengeVisibility.Private, "Repair only", null, "Web", definition, fixture.Now), ct);
        await Assert.That(saved.Succeeded).IsTrue();
        var rejected = await fixture.Challenges.CreateAsync(new(null, fixture.CompetitionId, fixture.TemplateId, 0, fixture.Now), new CtfCompetitionChallengeRules(), ct);
        await Assert.That(rejected.Failure).IsEqualTo(ChallengeMutationFailure.RequiredCapabilitiesMissing);
        await Assert.That(rejected.MissingCapabilities).IsEquivalentTo(new[] { ChallengeCapabilityKind.Break });
        await Assert.That(await fixture.Db.CompetitionChallenges.CountAsync(ct)).IsEqualTo(0);
    }

    private static ChallengeDefinition ChallengeDefinitionContractCopy(ChallengeDefinition definition) =>
        NoCTF.API.Endpoints.Administration.ChallengeBank.ChallengeDefinitionContractMapper.ToDomain(Guid.Empty,
            NoCTF.API.Endpoints.Administration.ChallengeBank.ChallengeDefinitionContractMapper.FromDomain(definition));
}
