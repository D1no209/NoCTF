using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NoCTF.API;
using NoCTF.API.Permissions;
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

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new MutableTenantContext());
    }

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid? CompetitionId { get; private set; }
        public void SetCompetitionId(Guid? competitionId) => CompetitionId = competitionId;
    }
}
