using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication.Account;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Authentication;

[Category("Integration")]
public sealed class BotAuthenticationTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Bot_CannotUsePasswordAuthentication(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_bot_auth")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);
            var hasher = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions
            {
                IterationCount = 10_000
            }));
            var dummyPassword = Guid.NewGuid().ToString("N");
            var bot = new User
            {
                Id = Guid.CreateVersion7(),
                UserName = "gitops-bot",
                NormalizedUserName = "GITOPS-BOT",
                Email = "bot-test@bot.invalid",
                NormalizedEmail = "BOT-TEST@BOT.INVALID",
                Kind = UserKind.Bot,
                Role = UserRole.Organizer,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            bot.PasswordHash = hasher.HashPassword(bot, dummyPassword);
            db.Users.Add(bot);
            await db.SaveChangesAsync(cancellationToken);
            var store = new AuthenticationStore(db, hasher);

            await Assert.That(await store.FindByLoginAsync(bot.UserName, cancellationToken))
                .IsNull();
            await Assert.That(await store.FindByLoginAsync(bot.Email, cancellationToken))
                .IsNull();
            await Assert.That(await store.FindByLoginAsync(bot.Id.ToString(), cancellationToken))
                .IsNull();
            await Assert.That(await store.VerifyPasswordAsync(
                    bot.Id,
                    dummyPassword,
                    cancellationToken))
                .IsFalse();
            await Assert.That(await store.ChangePasswordAsync(
                    bot.Id,
                    dummyPassword,
                    "ReplacementPassword123!",
                    DateTimeOffset.UtcNow,
                    cancellationToken))
                .IsEqualTo(ChangePasswordState.CurrentPasswordInvalid);
        });
    }
}
