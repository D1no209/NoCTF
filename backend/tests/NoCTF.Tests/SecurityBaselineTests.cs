using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NoCTF.API;
using NoCTF.API.Permissions;
using NoCTF.API.SignalR;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Tests;

public class SecurityBaselineTests
{
    [Fact]
    public async Task TenantResolutionMiddleware_IgnoresClientCompetitionHeader()
    {
        var headerCompetitionId = Guid.NewGuid();
        var tenantContext = new MutableTenantContext();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Competition-Id"] = headerCompetitionId.ToString();

        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(httpContext, tenantContext);

        Assert.Null(tenantContext.CompetitionId);
    }

    [Fact]
    public async Task TenantResolutionMiddleware_UsesCompetitionRouteSegment()
    {
        var competitionId = Guid.NewGuid();
        var tenantContext = new MutableTenantContext();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = $"/api/competitions/{competitionId}/challenges";

        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(httpContext, tenantContext);

        Assert.Equal(competitionId, tenantContext.CompetitionId);
    }

    [Fact]
    public async Task CanManageCompetition_OrganizerWithoutScope_CannotManage()
    {
        var competitionId = Guid.NewGuid();
        var organizerId = Guid.NewGuid();
        await using var db = CreateDb();
        db.Users.Add(new User
        {
            Id = organizerId,
            UserName = "organizer",
            Email = "organizer@example.test",
            Role = UserRole.Organizer,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "Scoped competition",
            OwnerId = Guid.NewGuid(),
            GameModeType = GameModeType.Ctf,
            Status = CompetitionStatus.Draft,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync();

        var service = new CompetitionPermissionService(db);
        var canManage = await service.CanManageCompetitionAsync(organizerId, competitionId);

        Assert.False(canManage);
    }

    [Fact]
    public async Task CanManageCompetition_ManagerCollaborator_CanManage()
    {
        var competitionId = Guid.NewGuid();
        var organizerId = Guid.NewGuid();
        await using var db = CreateDb();
        db.Users.Add(new User
        {
            Id = organizerId,
            UserName = "organizer",
            Email = "organizer@example.test",
            Role = UserRole.Organizer,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "Scoped competition",
            OwnerId = Guid.NewGuid(),
            GameModeType = GameModeType.Ctf,
            Status = CompetitionStatus.Draft,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(1)
        });
        db.CompetitionCollaborators.Add(new CompetitionCollaborator
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            UserId = organizerId,
            Role = CollaboratorRole.Manager,
            AddedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new CompetitionPermissionService(db);
        var canManage = await service.CanManageCompetitionAsync(organizerId, competitionId);

        Assert.True(canManage);
    }

    [Fact]
    public async Task GetManageableCompetitionIds_ReturnsOnlyOwnedAndManagedCompetitions()
    {
        var ownerCompetitionId = Guid.NewGuid();
        var managedCompetitionId = Guid.NewGuid();
        var unrelatedCompetitionId = Guid.NewGuid();
        var organizerId = Guid.NewGuid();
        await using var db = CreateDb();
        db.Users.Add(new User
        {
            Id = organizerId,
            UserName = "organizer",
            Email = "organizer@example.test",
            Role = UserRole.Organizer,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Competitions.AddRange(
            Competition(ownerCompetitionId, organizerId),
            Competition(managedCompetitionId, Guid.NewGuid()),
            Competition(unrelatedCompetitionId, Guid.NewGuid()));
        db.CompetitionCollaborators.Add(new CompetitionCollaborator
        {
            Id = Guid.NewGuid(),
            CompetitionId = managedCompetitionId,
            UserId = organizerId,
            Role = CollaboratorRole.Manager,
            AddedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var ids = await new CompetitionPermissionService(db).GetManageableCompetitionIdsAsync(organizerId);

        Assert.Contains(ownerCompetitionId, ids);
        Assert.Contains(managedCompetitionId, ids);
        Assert.DoesNotContain(unrelatedCompetitionId, ids);
    }

    [Fact]
    public async Task GetManageableCompetitionIds_AdminReceivesAllCompetitions()
    {
        var adminId = Guid.NewGuid();
        var firstCompetitionId = Guid.NewGuid();
        var secondCompetitionId = Guid.NewGuid();
        await using var db = CreateDb();
        db.Users.Add(new User
        {
            Id = adminId,
            UserName = "admin",
            Email = "admin-scope@example.test",
            Role = UserRole.Admin,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Competitions.AddRange(
            Competition(firstCompetitionId, Guid.NewGuid()),
            Competition(secondCompetitionId, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var ids = await new CompetitionPermissionService(db)
            .GetManageableCompetitionIdsAsync(adminId);

        Assert.Equal(2, ids.Count);
        Assert.Contains(firstCompetitionId, ids);
        Assert.Contains(secondCompetitionId, ids);
    }

    [Fact]
    public async Task CanViewCompetition_AllowsPausedAndFinishedPublicCompetitions()
    {
        var userId = Guid.NewGuid();
        var pausedId = Guid.NewGuid();
        var finishedId = Guid.NewGuid();
        await using var db = CreateDb();
        db.Users.Add(new User
        {
            Id = userId,
            UserName = "viewer",
            Email = "viewer@example.test",
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        var paused = Competition(pausedId, Guid.NewGuid());
        paused.Status = CompetitionStatus.Paused;
        var finished = Competition(finishedId, Guid.NewGuid());
        finished.Status = CompetitionStatus.Finished;
        db.Competitions.AddRange(paused, finished);
        await db.SaveChangesAsync();

        var permissions = new CompetitionPermissionService(db);

        Assert.True(await permissions.CanViewCompetitionAsync(userId, pausedId));
        Assert.True(await permissions.CanViewCompetitionAsync(userId, finishedId));
    }

    [Fact]
    public async Task RealtimeAccess_UsesOneCombinedQueryAndAllowsPausedParticipants()
    {
        var userId = Guid.NewGuid();
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await using var db = CreateDb();
        db.Users.Add(new User
        {
            Id = userId,
            UserName = "participant",
            Email = "participant@example.test",
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        var competition = Competition(competitionId, Guid.NewGuid());
        competition.Status = CompetitionStatus.Paused;
        competition.StartTime = DateTime.UtcNow.AddHours(-1);
        db.Competitions.Add(competition);
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "participant-team",
            CaptainId = userId,
            RegistrationStatus = TeamRegistrationStatus.Approved,
            CreatedAt = DateTime.UtcNow
        });
        db.TeamMembers.Add(new TeamMember
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            UserId = userId,
            Role = TeamMemberRole.Captain,
            JoinedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.True(await CompetitionRealtimeAccess.BuildAccessQuery(
                db,
                competitionId,
                userId,
                DateTime.UtcNow)
            .AnyAsync());

        await using var postgres = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql("Host=localhost;Database=noctf_query_shape;Username=noctf;Password=noctf")
                .Options,
            new MutableTenantContext());
        var sql = CompetitionRealtimeAccess.BuildAccessQuery(
                postgres,
                competitionId,
                userId,
                DateTime.UtcNow)
            .ToQueryString();
        Assert.Contains("UNION ALL", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DataSeeder_RejectsDefaultAdminPasswordOutsideDevelopmentDefaults()
    {
        await using var db = CreateDb();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SeedAdmin:Email"] = "admin@noctf.local",
                ["SeedAdmin:UserName"] = "admin",
                ["SeedAdmin:Password"] = "Admin@123456"
            })
            .Build();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            DataSeeder.SeedAsync(db, config, allowDefaultAdminCredentials: false));

        Assert.Contains("SeedAdmin:Password", ex.Message);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new MutableTenantContext());
    }

    private static Competition Competition(Guid id, Guid ownerId) => new()
    {
        Id = id,
        CompetitionId = id,
        Title = $"Competition {id:N}",
        OwnerId = ownerId,
        GameModeType = GameModeType.Ctf,
        Status = CompetitionStatus.Draft,
        StartTime = DateTime.UtcNow,
        EndTime = DateTime.UtcNow.AddHours(1)
    };

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid? CompetitionId { get; private set; }
        public void SetCompetitionId(Guid? competitionId) => CompetitionId = competitionId;
    }
}
