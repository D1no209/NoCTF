using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.LiveSolo.Matches;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloPlayerPolicyPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Approved_participants_read_minimal_policy_but_banned_pending_or_unrelated_identities_cannot(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            var authorizer = new CompetitionModerationAuthorizer(f.Db); var reader = new LiveSoloPlayerPolicyReader(f.Db, authorizer);
            await Assert.That(await authorizer.CanObserveAsync(f.Left.Id, f.Competition.Id, ct)).IsFalse();
            var policy = await reader.ReadAsync(f.Competition.Id, f.Left.Id, ct);
            await Assert.That(policy).IsNotNull(); await Assert.That(policy!.Enabled).IsTrue();
            await Assert.That(await reader.ReadAsync(Guid.NewGuid(), f.Left.Id, ct)).IsNull();
            await Assert.That(await reader.ReadAsync(f.Competition.Id, Guid.NewGuid(), ct)).IsNull();
            var team = await f.Db.Teams.SingleAsync(x => x.Id == f.LeftTeam.Id, ct); team.IsBanned = true; await f.Db.SaveChangesAsync(ct);
            await Assert.That(await reader.ReadAsync(f.Competition.Id, f.Left.Id, ct)).IsNull();
            team.IsBanned = false; team.RegistrationStatus = TeamRegistrationStatus.Pending; await f.Db.SaveChangesAsync(ct);
            await Assert.That(await reader.ReadAsync(f.Competition.Id, f.Left.Id, ct)).IsNull();
            await Assert.That(await reader.ReadAsync(f.Competition.Id, f.Owner.Id, ct)).IsNotNull();
            team.RegistrationStatus = TeamRegistrationStatus.Approved; (await f.Db.Competitions.SingleAsync(ct)).Status = CompetitionStatus.Draft; await f.Db.SaveChangesAsync(ct);
            await Assert.That(await reader.ReadAsync(f.Competition.Id, f.Left.Id, ct)).IsNull();
            await Assert.That(await reader.ReadAsync(f.Competition.Id, f.Owner.Id, ct)).IsNotNull();
        });
    }
}
