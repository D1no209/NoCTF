using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Challenges.Flags;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeFlagRegularExpressionPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Break_flags_follow_bank_permissions_and_remain_independent_of_runtime_injection_and_other_capabilities(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Draft, ct);
        var db = fixture.Db; var managerId = Guid.NewGuid(); var outsiderId = Guid.NewGuid();
        foreach (var id in new[] { managerId, outsiderId })
            db.Users.Add(new User { Id = id, UserName = id.ToString("N"), NormalizedUserName = id.ToString("N").ToUpperInvariant(),
                Email = $"{id:N}@example.test", Role = UserRole.Organizer, AccountStatus = UserAccountStatus.Active });
        var template = await db.Challenges.AsSplitQuery().SingleAsync(value => value.Id == fixture.TemplateId, ct);
        template.ManagerIds = [managerId];
        var repairableId = Guid.NewGuid();
        db.Challenges.Add(new Challenge { Id = repairableId, OwnerId = fixture.OwnerId, Title = "Repairable",
            Definition = NoCTF.Tests.Unit.Gameplay.ChallengeCapabilityTests.Repairable() });
        await db.SaveChangesAsync(ct);
        var flags = new ManageChallengeFlags(new ChallengeFlagManagementStore(db));
        var owner = await flags.SaveAsync(Command(template.Id, @"flag\{owner-[0-9]+\}", fixture.Now), fixture.OwnerId, false, ct);
        var manager = await flags.SaveAsync(Command(template.Id, @"flag\{manager-[0-9]+\}", fixture.Now), managerId, false, ct);
        var outsider = await flags.SaveAsync(Command(template.Id, @"flag\{other-[0-9]+\}", fixture.Now), outsiderId, false, ct);
        await Assert.That(owner.Succeeded).IsTrue(); await Assert.That(manager.Succeeded).IsTrue();
        await Assert.That(outsider.FailureCode).IsEqualTo(ChallengeFlagFailureCode.FlagNotFound);
        await Assert.That((await flags.SaveAsync(Command(repairableId, @"flag\{repair-[0-9]+\}", fixture.Now), fixture.OwnerId, false, ct)).Succeeded).IsTrue();
        var definition = new ChallengeDefinition { Capabilities = [new BreakCapabilityDefinition
            { EnvironmentTargets = [new() { ServiceName = "main", VariableName = "FLAG" }] }],
            Runtime = new ContainerChallengeRuntimeTemplate { Services = [new() { Name = "main", Image = RuntimeExecutionFixture.Image }] } };
        var updated = await new ChallengeBankStore(db, fixture.Publisher).UpdateAsync(new(template.Id, fixture.OwnerId, false,
            ChallengeVisibility.Private, template.Title, null, "Web", definition, fixture.Now.AddSeconds(1)), ct);
        await Assert.That(updated.Succeeded).IsTrue();
        await Assert.That((await flags.ListAsync(ChallengeFlagScope.Template(template.Id), fixture.OwnerId, false, false, ct))!.Count).IsEqualTo(2);
        await Assert.That((await flags.DeleteAsync(ChallengeFlagScope.Template(template.Id), owner.Value!.Id, fixture.OwnerId, false, fixture.Now.AddSeconds(2), ct)).Succeeded).IsTrue();
        await Assert.That((await flags.DeleteAsync(ChallengeFlagScope.Template(template.Id), manager.Value!.Id, managerId, false, fixture.Now.AddSeconds(2), ct)).Succeeded).IsTrue();
        await Assert.That((await flags.RestoreAsync(ChallengeFlagScope.Template(template.Id), owner.Value.Id, fixture.OwnerId, false, fixture.Now.AddSeconds(3), ct)).Succeeded).IsTrue();
        var restored = await db.ChallengeFlags.AsNoTracking().SingleAsync(value => value.ChallengeId == template.Id, ct);
        await Assert.That(restored.MatchKind).IsEqualTo(ChallengeFlagMatchKind.RegularExpression);
    }

    private static SaveChallengeFlagCommand Command(Guid challengeId, string pattern, DateTimeOffset now) => new(
        ChallengeFlagScope.Template(challengeId), null, true, null, pattern, null, null, null, null, now, ChallengeFlagMatchKind.RegularExpression);
}
