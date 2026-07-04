using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.Plugins.Penetration;

namespace NoCTF.Tests;

public class PenetrationFlagServiceTests
{
    [Fact]
    public async Task RegenerateDynamicFlags_CreatesPerTeamValuesAndDeactivatesOldOnReset()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var topologyId = Guid.NewGuid();
        var flag = SeedDynamicFlag(competitionId, challengeId, topologyId);
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        db.PenetrationFlags.Add(flag);
        await db.SaveChangesAsync();

        var challenge = Challenge(competitionId, challengeId);
        var service = new PenetrationFlagService(db);
        var instanceA = Instance(competitionId, challengeId, teamA);
        var instanceB = Instance(competitionId, challengeId, teamB);
        var firstA = await service.RegenerateDynamicFlagsAsync(challenge, instanceA, [flag], CancellationToken.None);
        var firstB = await service.RegenerateDynamicFlagsAsync(challenge, instanceB, [flag], CancellationToken.None);
        var secondA = await service.RegenerateDynamicFlagsAsync(challenge, instanceA, [flag], CancellationToken.None);

        Assert.Equal("[REDACTED]", firstA[0].ValueSecret);
        Assert.NotEqual(firstA[0].PlainValue, firstB[0].PlainValue);
        Assert.NotEqual(firstA[0].PlainValue, secondA[0].PlainValue);
        Assert.False(await db.DynamicFlagInstances.IgnoreQueryFilters().Where(f => f.Id == firstA[0].Id).Select(f => f.IsActive).SingleAsync());
        Assert.True(await db.DynamicFlagInstances.IgnoreQueryFilters().Where(f => f.Id == secondA[0].Id).Select(f => f.IsActive).SingleAsync());
    }

    [Fact]
    public async Task MatchAsync_AcceptsOwnActiveDynamicFlagAndDetectsCrossTeamFlag()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var topologyId = Guid.NewGuid();
        var flag = SeedDynamicFlag(competitionId, challengeId, topologyId);
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        db.PenetrationFlags.Add(flag);
        await db.SaveChangesAsync();

        var challenge = Challenge(competitionId, challengeId);
        var service = new PenetrationFlagService(db);
        var instanceA = Instance(competitionId, challengeId, teamA);
        var instanceB = Instance(competitionId, challengeId, teamB);
        var dynamicA = (await service.RegenerateDynamicFlagsAsync(challenge, instanceA, [flag], CancellationToken.None))[0];
        var dynamicB = (await service.RegenerateDynamicFlagsAsync(challenge, instanceB, [flag], CancellationToken.None))[0];

        var own = await service.MatchAsync(challenge, instanceA, PenetrationFlagService.FormatFlag(challenge, dynamicA.PlainValue!), CancellationToken.None);
        var crossTeam = await service.MatchAsync(challenge, instanceA, PenetrationFlagService.FormatFlag(challenge, dynamicB.PlainValue!), CancellationToken.None);

        Assert.True(own.IsCorrect);
        Assert.False(own.IsCrossTeamDynamicFlag);
        Assert.Equal(flag.Id, own.Flag?.Id);
        Assert.False(crossTeam.IsCorrect);
        Assert.True(crossTeam.IsCrossTeamDynamicFlag);
        Assert.Equal(teamB, crossTeam.VictimTeamId);
    }

    [Fact]
    public void RedactSubmittedFlag_StoresOnlyHashAndLength()
    {
        var redacted = PenetrationFlagService.RedactSubmittedFlag("flag{secret_value}");

        Assert.StartsWith("sha256:", redacted);
        Assert.EndsWith(";len:18", redacted);
        Assert.DoesNotContain("secret_value", redacted);
    }

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new PenetrationFlagTenantContext(competitionId));
    }

    private static Challenge Challenge(Guid competitionId, Guid challengeId) => new()
    {
        Id = challengeId,
        CompetitionId = competitionId,
        Title = "Penetration",
        TypeId = PenetrationConstants.TypeId,
        FlagPrefix = "flag"
    };

    private static TeamChallengeInstance Instance(Guid competitionId, Guid challengeId, Guid teamId) => new()
    {
        Id = Guid.NewGuid(),
        CompetitionId = competitionId,
        TeamId = teamId,
        ChallengeId = challengeId,
        Status = PenetrationInstanceStatus.Running
    };

    private static PenetrationFlag SeedDynamicFlag(Guid competitionId, Guid challengeId, Guid topologyId) => new()
    {
        Id = Guid.NewGuid(),
        CompetitionId = competitionId,
        ChallengeId = challengeId,
        TopologyId = topologyId,
        Stage = 1,
        Name = "Initial",
        Score = 100,
        IsDynamic = true,
        InjectionKey = "NOCTF_STAGE1",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}

file class PenetrationFlagTenantContext(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
