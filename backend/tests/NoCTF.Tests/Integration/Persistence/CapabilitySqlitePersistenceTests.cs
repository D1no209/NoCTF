using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Persistence.Sqlite;
using NoCTF.Tests.Unit.Gameplay;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CapabilitySqlitePersistenceTests
{
    [Test]
    public async Task Development_database_creates_the_relational_capability_model_and_reuses_a_template(CancellationToken ct)
    {
        await using var services = new ServiceCollection().AddNoCtfSqliteDevelopmentDatabase($"capabilities-{Guid.NewGuid():N}").BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        await db.Database.EnsureCreatedAsync(ct);
        var ownerId = Guid.NewGuid(); var templateId = Guid.NewGuid(); var ctfId = Guid.NewGuid(); var awdpId = Guid.NewGuid();
        db.Users.Add(new() { Id = ownerId, UserName = "author", NormalizedUserName = "AUTHOR", Email = "author@example.test", NormalizedEmail = "AUTHOR@EXAMPLE.TEST" });
        var definition = ChallengeCapabilityTests.Repairable();
        definition.Break!.EnvironmentTargets.Add(new() { ServiceName = "web", VariableName = "FLAG" });
        db.Challenges.Add(new() { Id = templateId, OwnerId = ownerId, Title = "Reusable", Definition = definition });
        db.Competitions.AddRange(new CtfCompetition { Id = ctfId, OwnerId = ownerId, Title = "CTF", ModeConfiguration = new CtfCompetitionModeConfiguration() },
            new AwdpCompetition { Id = awdpId, OwnerId = ownerId, Title = "AWDP", ModeConfiguration = new AwdpCompetitionModeConfiguration() });
        db.CompetitionChallenges.AddRange(new CtfCompetitionChallenge { Id = Guid.NewGuid(), CompetitionId = ctfId, ChallengeId = templateId, Rules = new CtfCompetitionChallengeRules() },
            new AwdpCompetitionChallenge { Id = Guid.NewGuid(), CompetitionId = awdpId, ChallengeId = templateId, Rules = new AwdpCompetitionChallengeRules() });
        await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
        var loaded = await db.Challenges.AsSplitQuery().SingleAsync(ct);
        await Assert.That(loaded.Definition!.Capabilities.Count).IsEqualTo(3);
        await Assert.That(loaded.Definition.Break!.EnvironmentTargets.Single().VariableName).IsEqualTo("FLAG");
        await Assert.That(await db.CompetitionChallenges.CountAsync(value => value.ChallengeId == templateId, ct)).IsEqualTo(2);
        await Assert.That(db.Model.FindEntityType(typeof(Challenge))!.GetProperties().Any(property => property.Name == "Mode")).IsFalse();
    }
}
