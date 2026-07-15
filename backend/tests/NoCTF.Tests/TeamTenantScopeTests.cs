using Microsoft.EntityFrameworkCore;
using NoCTF.API.Endpoints.Teams;
using NoCTF.API.Permissions;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Tests;

public class TeamTenantScopeTests
{
    [Fact]
    public async Task TeamPermissionService_ResolvesTeamMembershipWhenRouteHasNoTenant()
    {
        await using var db = CreateDbWithUnsetTenant();
        var (competition, team, captain) = SeedTeam(maxTeamMembers: 3);
        db.AddRange(competition, team, captain);
        await db.SaveChangesAsync();
        var permissions = new TeamPermissionService(db);

        Assert.True(await permissions.IsCaptainAsync(captain.UserId, team.Id));
        Assert.True(await permissions.IsTeamMemberAsync(captain.UserId, team.Id));
    }

    [Fact]
    public async Task TryAddMember_EnforcesCapacityWhenRouteHasNoTenant()
    {
        await using var db = CreateDbWithUnsetTenant();
        var (competition, team, captain) = SeedTeam(maxTeamMembers: 1);
        db.AddRange(competition, team, captain);
        await db.SaveChangesAsync();

        var result = await TeamLifecycleRules.TryAddMemberAsync(
            db,
            team.Id,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("team_full", result.Code);
        Assert.Equal(1, await db.TeamMembers.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task AutoApprovedTeam_RemainsUnlockedAndJoinableUntilCapacity()
    {
        await using var db = CreateDbWithUnsetTenant();
        var captainId = Guid.NewGuid();
        var competition = Competition(maxTeamMembers: 2, CompetitionStatus.Running, DateTime.UtcNow.AddMinutes(-1));
        competition.TeamRegistrationAutoApprove = true;
        var team = TeamLifecycleRules.CreateRegisteredTeam(
            competition,
            new CreateTeamRequest { CompetitionId = competition.Id, Name = "auto approved" },
            captainId,
            trackName: null,
            DateTime.UtcNow);
        var captain = Captain(team, captainId);
        db.AddRange(competition, team, captain);
        await db.SaveChangesAsync();

        Assert.Equal(TeamRegistrationStatus.Approved, team.RegistrationStatus);
        Assert.False(team.IsLocked);
        Assert.NotNull(team.ApprovedAt);

        var joined = await TeamLifecycleRules.TryAddMemberAsync(
            db,
            team.Id,
            Guid.NewGuid(),
            CancellationToken.None);
        var full = await TeamLifecycleRules.TryAddMemberAsync(
            db,
            team.Id,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(joined.Success);
        Assert.False(full.Success);
        Assert.Equal("team_full", full.Code);
        Assert.Equal(2, await db.TeamMembers.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task TryLeaveMember_SoleCaptainBeforeStartWithdrawsTeamWithoutGhostRecord()
    {
        await using var db = CreateDbWithUnsetTenant();
        var captainId = Guid.NewGuid();
        var competition = Competition(maxTeamMembers: 3, CompetitionStatus.Published, DateTime.UtcNow.AddHours(1));
        competition.TeamRegistrationAutoApprove = true;
        var team = TeamLifecycleRules.CreateRegisteredTeam(
            competition,
            new CreateTeamRequest { CompetitionId = competition.Id, Name = "withdrawn team" },
            captainId,
            trackName: null,
            DateTime.UtcNow);
        db.AddRange(competition, team, Captain(team, captainId));
        await db.SaveChangesAsync();

        var result = await TeamLifecycleRules.TryLeaveMemberAsync(
            db,
            team.Id,
            captainId,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Empty(await db.Teams.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.TeamMembers.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(
            "team.withdrawn",
            Assert.Single(await db.CompetitionLogs.IgnoreQueryFilters().ToListAsync()).EventType);
    }

    [Fact]
    public async Task TryLeaveMember_SoleCaptainAfterStartIsRejectedAndTeamRemainsValid()
    {
        await using var db = CreateDbWithUnsetTenant();
        var captainId = Guid.NewGuid();
        var competition = Competition(maxTeamMembers: 3, CompetitionStatus.Running, DateTime.UtcNow.AddMinutes(-1));
        competition.TeamRegistrationAutoApprove = true;
        var team = TeamLifecycleRules.CreateRegisteredTeam(
            competition,
            new CreateTeamRequest { CompetitionId = competition.Id, Name = "active team" },
            captainId,
            trackName: null,
            DateTime.UtcNow);
        db.AddRange(competition, team, Captain(team, captainId));
        await db.SaveChangesAsync();

        var result = await TeamLifecycleRules.TryLeaveMemberAsync(
            db,
            team.Id,
            captainId,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("sole_captain_cannot_leave", result.Code);
        Assert.Equal(TeamRegistrationStatus.Approved,
            (await db.Teams.IgnoreQueryFilters().SingleAsync()).RegistrationStatus);
        Assert.Equal(captainId,
            (await db.TeamMembers.IgnoreQueryFilters().SingleAsync()).UserId);
    }

    [Fact]
    public async Task ExplicitTeamLock_StillBlocksJoinAndLeave()
    {
        await using var db = CreateDbWithUnsetTenant();
        var captainId = Guid.NewGuid();
        var competition = Competition(maxTeamMembers: 3, CompetitionStatus.Published, DateTime.UtcNow.AddHours(1));
        competition.TeamRegistrationAutoApprove = true;
        var team = TeamLifecycleRules.CreateRegisteredTeam(
            competition,
            new CreateTeamRequest { CompetitionId = competition.Id, Name = "locked team" },
            captainId,
            trackName: null,
            DateTime.UtcNow);
        team.IsLocked = true;
        db.AddRange(competition, team, Captain(team, captainId));
        await db.SaveChangesAsync();

        var join = await TeamLifecycleRules.TryAddMemberAsync(
            db,
            team.Id,
            Guid.NewGuid(),
            CancellationToken.None);
        var leave = await TeamLifecycleRules.TryLeaveMemberAsync(
            db,
            team.Id,
            captainId,
            CancellationToken.None);

        Assert.False(join.Success);
        Assert.Equal("team_locked", join.Code);
        Assert.False(leave.Success);
        Assert.Equal("team_locked", leave.Code);
        Assert.Single(await db.TeamMembers.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task TryLeaveMember_CaptainWithTeammateStillRequiresTransfer()
    {
        await using var db = CreateDbWithUnsetTenant();
        var (competition, team, captain) = SeedTeam(maxTeamMembers: 3);
        var teammate = Member(team, Guid.NewGuid());
        db.AddRange(competition, team, captain, teammate);
        await db.SaveChangesAsync();

        var result = await TeamLifecycleRules.TryLeaveMemberAsync(
            db,
            team.Id,
            captain.UserId,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("captain_transfer_required", result.Code);
        Assert.Equal(2, await db.TeamMembers.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task TryLeaveMember_NonCaptainStillLeavesUnlockedTeam()
    {
        await using var db = CreateDbWithUnsetTenant();
        var (competition, team, captain) = SeedTeam(maxTeamMembers: 3);
        var teammate = Member(team, Guid.NewGuid());
        db.AddRange(competition, team, captain, teammate);
        await db.SaveChangesAsync();

        var result = await TeamLifecycleRules.TryLeaveMemberAsync(
            db,
            team.Id,
            teammate.UserId,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(captain.UserId,
            (await db.TeamMembers.IgnoreQueryFilters().SingleAsync()).UserId);
        Assert.Single(await db.Teams.IgnoreQueryFilters().ToListAsync());
    }

    private static ApplicationDbContext CreateDbWithUnsetTenant()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options, new TenantContext());
    }

    private static (Competition Competition, Team Team, TeamMember Captain) SeedTeam(int maxTeamMembers)
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var captainId = Guid.NewGuid();
        var competition = new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "Tenantless team route",
            OwnerId = Guid.NewGuid(),
            StartTime = DateTime.UtcNow.AddMinutes(-1),
            EndTime = DateTime.UtcNow.AddHours(1),
            Status = CompetitionStatus.Running,
            MaxTeamMembers = maxTeamMembers
        };
        var team = new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "team",
            CaptainId = captainId,
            InviteToken = Guid.NewGuid().ToString("N"),
            RegistrationStatus = TeamRegistrationStatus.Pending
        };
        var captain = new TeamMember
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            UserId = captainId,
            Role = TeamMemberRole.Captain
        };
        return (competition, team, captain);
    }

    private static Competition Competition(
        int maxTeamMembers,
        CompetitionStatus status,
        DateTime startTime)
    {
        var id = Guid.NewGuid();
        return new Competition
        {
            Id = id,
            CompetitionId = id,
            Title = "Team lifecycle test",
            OwnerId = Guid.NewGuid(),
            StartTime = startTime,
            EndTime = startTime.AddHours(2),
            Status = status,
            MaxTeamMembers = maxTeamMembers
        };
    }

    private static TeamMember Captain(Team team, Guid captainId)
        => new()
        {
            Id = Guid.NewGuid(),
            CompetitionId = team.CompetitionId,
            TeamId = team.Id,
            UserId = captainId,
            Role = TeamMemberRole.Captain,
            JoinedAt = DateTime.UtcNow
        };

    private static TeamMember Member(Team team, Guid userId)
        => new()
        {
            Id = Guid.NewGuid(),
            CompetitionId = team.CompetitionId,
            TeamId = team.Id,
            UserId = userId,
            Role = TeamMemberRole.Member,
            JoinedAt = DateTime.UtcNow
        };
}
