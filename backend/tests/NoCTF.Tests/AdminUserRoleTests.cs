using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NoCTF.API.Auth;
using NoCTF.API.Endpoints.Admin;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Tests;

public class AdminUserRoleTests
{
    [Fact]
    public async Task DataSeeder_SeedsBootstrapAdministratorAsEmailVerified()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SeedAdmin:Email"] = "bootstrap-admin@example.test",
                ["SeedAdmin:UserName"] = "bootstrap-admin",
                ["SeedAdmin:Password"] = "BootstrapAdmin123!"
            })
            .Build();

        await DataSeeder.SeedAsync(db, configuration);

        var admin = await db.Users.SingleAsync(ct);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.NotNull(admin.EmailVerifiedAt);
    }

    [Fact]
    public async Task UpdateUserRole_AllowsAdminPromotionAndRevokesExistingSessions()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        await using var db = CreateDb();
        db.Users.Add(new User
        {
            Id = userId,
            UserName = "future-admin",
            Email = "future-admin@example.test",
            Role = UserRole.User,
            TokenVersion = 4,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        var cache = new RecordingTokenVersionCache();
        var endpoint = Factory.Create<UpdateUserRoleEndpoint>(
            context => context.Request.RouteValues["id"] = userId,
            db,
            cache);

        await endpoint.HandleAsync(new UpdateUserRoleRequest { Role = "admin" }, ct);

        var updated = await db.Users.SingleAsync(user => user.Id == userId, ct);
        Assert.Equal(UserRole.Admin, updated.Role);
        Assert.Equal(5, updated.TokenVersion);
        Assert.Equal((userId, 5), cache.LastUpdate);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new TestTenantContext());
    }

    private sealed class RecordingTokenVersionCache : IUserTokenVersionCache
    {
        public (Guid UserId, int TokenVersion)? LastUpdate { get; private set; }

        public Task<int?> GetAsync(Guid userId, ApplicationDbContext db, CancellationToken ct)
            => Task.FromResult<int?>(null);

        public Task SetAsync(Guid userId, int tokenVersion)
        {
            LastUpdate = (userId, tokenVersion);
            return Task.CompletedTask;
        }
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid? CompetitionId => null;
        public void SetCompetitionId(Guid? competitionId) { }
    }
}
