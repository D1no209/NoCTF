using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using NoCTF.Application.Administration;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Unit.Administration;

public sealed class PlatformUserAggregatePatchTests
{
    [Test]
    public async Task Multiple_managed_fields_invalidate_tokens_exactly_once(
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "managed-user",
            NormalizedUserName = "MANAGED-USER",
            Email = "managed@example.test",
            PasswordHash = "hash",
            Kind = UserKind.Human,
            Role = UserRole.User,
            AccountStatus = UserAccountStatus.Active,
            TokenVersion = 5,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        var store = new PlatformAdministrationStore(db, new PasswordHasher<User>());
        var now = DateTimeOffset.UnixEpoch.AddHours(1);

        var result = await store.PatchUserAsync(
            user.Id,
            Guid.NewGuid(),
            target =>
            {
                target.Role = UserRole.Organizer;
                target.AccountStatus = UserAccountStatus.Disabled;
            },
            emailVerified: true,
            now,
            cancellationToken);

        await Assert.That(result.State).IsEqualTo(PatchPlatformUserState.Updated);
        await Assert.That(result.User!.TokenVersion).IsEqualTo(6);
        await Assert.That(result.User.Role).IsEqualTo(UserRole.Organizer);
        await Assert.That(result.User.AccountStatus).IsEqualTo(UserAccountStatus.Disabled);
        await Assert.That(result.User.EmailVerified).IsTrue();
    }
}
