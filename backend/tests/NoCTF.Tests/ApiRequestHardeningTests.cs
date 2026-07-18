using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.API;
using NoCTF.API.Endpoints.Admin;
using NoCTF.API.Endpoints.Auth;
using NoCTF.Core;
using NoCTF.Infrastructure;
using Npgsql;

namespace NoCTF.Tests;

public class ApiRequestHardeningTests
{
    [Fact]
    public void ClientIpAddress_NormalizesIpv4MappedIpv6Addresses()
    {
        Assert.Equal("139.200.81.205", ClientIpAddress.Normalize(IPAddress.Parse("::ffff:139.200.81.205")));
        Assert.Equal("139.200.81.205", ClientIpAddress.Normalize("::ffff:139.200.81.205"));
        Assert.Equal("2001:db8::1", ClientIpAddress.Normalize(IPAddress.Parse("2001:db8::1")));
    }

    [Fact]
    public void RequestBodyLimits_ResolveFlagAndMultipartRoutes()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Submissions:MaxRequestBodyBytes"] = "8192",
                ["PatchUpload:MaxRequestBodyBytes"] = "65536"
            })
            .Build();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;

        context.Request.Path = $"/api/competitions/{Guid.NewGuid()}/challenges/{Guid.NewGuid()}/submit";
        Assert.Equal(8192, RequestBodyLimits.Resolve(context.Request, configuration));

        context.Request.Path = $"/api/competitions/{Guid.NewGuid()}/challenges/{Guid.NewGuid()}/patch";
        Assert.Equal(65536, RequestBodyLimits.Resolve(context.Request, configuration));

        context.Request.Path = $"/api/competitions/{Guid.NewGuid()}/challenges/{Guid.NewGuid()}/penetration/flags/submit";
        Assert.Equal(8192, RequestBodyLimits.Resolve(context.Request, configuration));

        context.Request.Path = $"/api/competitions/{Guid.NewGuid()}/actions/custom";
        Assert.Equal(64 * 1024, RequestBodyLimits.Resolve(context.Request, configuration));

        context.Request.Method = HttpMethods.Put;
        context.Request.Path = "/api/admin/competitions";
        Assert.Equal(2 * 1024 * 1024, RequestBodyLimits.Resolve(context.Request, configuration));

        context.Request.Method = HttpMethods.Get;
        Assert.Null(RequestBodyLimits.Resolve(context.Request, configuration));
    }

    [Fact]
    public async Task RequestBodyLimitMiddleware_RejectsOversizedContentLengthBeforeReading()
    {
        var nextCalled = false;
        var middleware = new RequestBodyLimitMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = $"/api/competitions/{Guid.NewGuid()}/challenges/{Guid.NewGuid()}/submit";
        context.Request.ContentLength = 9;
        context.Response.Body = new MemoryStream();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Submissions:MaxRequestBodyBytes"] = "8"
            })
            .Build();

        await middleware.InvokeAsync(context, configuration);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
    }

    [Fact]
    public async Task RequestBodyLimitMiddleware_RejectsOversizedChunkedBodyWhileReading()
    {
        var middleware = new RequestBodyLimitMiddleware(async context =>
        {
            await context.Request.Body.CopyToAsync(Stream.Null);
        });
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = $"/api/competitions/{Guid.NewGuid()}/challenges/{Guid.NewGuid()}/submit";
        context.Request.ContentLength = null;
        context.Request.Body = new MemoryStream(new byte[9]);
        context.Response.Body = new MemoryStream();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Submissions:MaxRequestBodyBytes"] = "8"
            })
            .Build();

        await middleware.InvokeAsync(context, configuration);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
    }

    [Fact]
    public async Task UploadedObjectPersistence_DurablyQueuesNewObjectWhenDatabaseWriteFails()
    {
        await using var db = CreateDb();

        await Assert.ThrowsAsync<DbUpdateException>(() => UploadedObjectPersistence.CompleteAsync(
            db,
            "challenge-attachments/new-object",
            _ => Task.FromException<string>(new DbUpdateException("write failed")),
            NullLogger.Instance,
            CancellationToken.None));

        var cleanup = Assert.Single(db.StorageCleanupItems);
        Assert.Equal("challenge-attachments/new-object", cleanup.StorageKey);
    }

    [Fact]
    public void UserModel_UsesBoundedCaseInsensitiveUniqueKeys()
    {
        using var db = CreateDb();
        var user = db.Model.FindEntityType(typeof(User));

        Assert.NotNull(user);
        Assert.Equal(UserInputLimits.EmailMaxLength, user.FindProperty(nameof(User.Email))?.GetMaxLength());
        Assert.Equal(UserInputLimits.UserNameMaxLength, user.FindProperty(nameof(User.UserName))?.GetMaxLength());
        Assert.Contains(user.GetIndexes(), index =>
            index.IsUnique &&
            index.GetDatabaseName() == "ix_users_email" &&
            index.Properties.Single().Name == "NormalizedEmail");
        Assert.Contains(user.GetIndexes(), index =>
            index.IsUnique &&
            index.GetDatabaseName() == "ix_users_username" &&
            index.Properties.Single().Name == "NormalizedUserName");
    }

    [Fact]
    public void RegisterConflictDetector_RecognizesOnlyUserUniqueIndexes()
    {
        var userConflict = new DbUpdateException(
            "conflict",
            CreateUniqueViolation("ix_users_username"));
        var unrelatedConflict = new DbUpdateException(
            "conflict",
            CreateUniqueViolation("ux_teams_invite_token"));

        Assert.True(RegisterEndpoint.IsUserUniquenessConflict(userConflict));
        Assert.False(RegisterEndpoint.IsUserUniquenessConflict(unrelatedConflict));
    }

    [Theory]
    [InlineData(" player ", "player")]
    [InlineData("\tplayer\r\n", "player")]
    public void RegisterUserName_NormalizationRemovesSurroundingWhitespace(string input, string expected)
    {
        Assert.Equal(expected, RegisterEndpoint.NormalizeUserName(input));
    }

    [Theory]
    [InlineData("player one")]
    [InlineData("player\tone")]
    [InlineData("player\none")]
    public void RegisterUserName_RejectsRemainingWhitespace(string userName)
    {
        Assert.True(RegisterEndpoint.ContainsWhitespace(userName));
    }

    [Theory]
    [InlineData(" PLAYER ")]
    [InlineData("PLAYER@EXAMPLE.TEST")]
    public async Task LoginLookup_AcceptsEmailOrUserNameCaseInsensitively(string identifier)
    {
        await using var db = CreateDb();
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "Player",
            Email = "player@example.test",
            PasswordHash = "unused",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await LoginEndpoint.FindUserAsync(
            db,
            LoginEndpoint.NormalizeIdentifier(identifier));

        Assert.Same(user, result);
    }

    [Fact]
    public void CollaboratorConflictDetector_RecognizesOnlyCollaboratorUniqueIndex()
    {
        var collaboratorConflict = new DbUpdateException(
            "conflict",
            CreateUniqueViolation("ix_competitioncollaborators_competition_user"));
        var unrelatedConflict = new DbUpdateException(
            "conflict",
            CreateUniqueViolation("ux_teams_invite_token"));

        Assert.True(AddCollaboratorEndpoint.IsCollaboratorConflict(collaboratorConflict));
        Assert.False(AddCollaboratorEndpoint.IsCollaboratorConflict(unrelatedConflict));
    }

    [Fact]
    public void AuditLogPagination_SaturatesInsteadOfOverflowing()
    {
        Assert.Equal(0, GetAuditLogsEndpoint.CalculateOffset(1, 200));
        Assert.Equal(200, GetAuditLogsEndpoint.CalculateOffset(2, 200));
        Assert.Equal(int.MaxValue, GetAuditLogsEndpoint.CalculateOffset(int.MaxValue, 200));
    }

    private static PostgresException CreateUniqueViolation(string constraintName)
        => new(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            constraintName: constraintName);

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new TestTenantContext());
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid? CompetitionId => null;
        public void SetCompetitionId(Guid? competitionId)
        {
        }
    }

}
