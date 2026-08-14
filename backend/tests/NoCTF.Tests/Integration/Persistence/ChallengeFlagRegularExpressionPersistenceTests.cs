using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeFlagRegularExpressionPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Owners_and_managers_control_static_regex_flags_and_dynamic_conversion_is_fenced(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_flag_regex")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var ownerId = Guid.CreateVersion7(now);
            var managerId = Guid.CreateVersion7(now.AddTicks(1));
            var outsiderId = Guid.CreateVersion7(now.AddTicks(2));
            var challengeId = Guid.CreateVersion7(now.AddTicks(3));
            var awdpChallengeId = Guid.CreateVersion7(now.AddTicks(4));

            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(ct);
            db.Users.AddRange(
                User(ownerId, "regex-owner", now),
                User(managerId, "regex-manager", now),
                User(outsiderId, "regex-outsider", now));
            db.Challenges.Add(new Challenge
            {
                Id = challengeId,
                OwnerId = ownerId,
                ManagerIds = [managerId],
                Mode = GameMode.Ctf,
                Visibility = ChallengeVisibility.Private,
                Title = "Static regex challenge",
                Direction = "Web",
                DefinitionJson = """{"schemaVersion":1}""",
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Challenges.Add(new Challenge
            {
                Id = awdpChallengeId,
                OwnerId = ownerId,
                ManagerIds = [],
                Mode = GameMode.Awdp,
                Visibility = ChallengeVisibility.Private,
                Title = "AWDP break challenge",
                Direction = "Pwn",
                DefinitionJson = """{"schemaVersion":1}""",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(ct);

            var flags = new ManageChallengeFlags(new ChallengeFlagManagementStore(db));
            var ownerSave = await flags.SaveAsync(
                Command(challengeId, @"flag\{owner-[0-9]+\}", now),
                ownerId,
                false,
                ct);
            var managerSave = await flags.SaveAsync(
                Command(challengeId, @"flag\{manager-[0-9]+\}", now.AddTicks(1)),
                managerId,
                false,
                ct);
            var outsiderSave = await flags.SaveAsync(
                Command(challengeId, @"flag\{outsider-[0-9]+\}", now.AddTicks(2)),
                outsiderId,
                false,
                ct);

            await Assert.That(ownerSave.Succeeded).IsTrue();
            await Assert.That(managerSave.Succeeded).IsTrue();
            await Assert.That(ownerSave.Value!.Id).IsNotEqualTo(managerSave.Value!.Id);
            await Assert.That(outsiderSave.FailureCode)
                .IsEqualTo(ChallengeFlagFailureCode.FlagNotFound);
            var awdpExact = await flags.SaveAsync(
                ExactCommand(awdpChallengeId, "flag{awdp-break}", now.AddTicks(3)),
                ownerId,
                false,
                ct);
            var awdpRegex = await flags.SaveAsync(
                Command(awdpChallengeId, @"flag\{awdp-[0-9]+\}", now.AddTicks(4)),
                ownerId,
                false,
                ct);

            await Assert.That(awdpExact.Succeeded).IsTrue();
            await Assert.That(awdpRegex.FailureCode)
                .IsEqualTo(ChallengeFlagFailureCode.RegularExpressionNotSupported);
            await Assert.That(await db.ChallengeFlags.CountAsync(ct)).IsEqualTo(3);

            var dynamicDefinition = JsonSerializer.Serialize(
                new CtfChallengeConfiguration(
                    CtfChallengeConfiguration.CurrentSchemaVersion,
                    null,
                    null,
                    Runtime: new ChallengeRuntimeTemplate(
                        RuntimeAllocation.PerTeam,
                        new ContainerRuntimeDefinition(
                            "registry.example/challenge:v1",
                            FlagEnvironmentVariableName: "FLAG"),
                        FlagSource: RuntimeFlagSource.PerTeam)),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var bank = new ChallengeBankStore(db);
            var blocked = await bank.UpdateAsync(new(
                challengeId,
                ownerId,
                false,
                GameMode.Ctf,
                ChallengeVisibility.Private,
                "Static regex challenge",
                null,
                "Web",
                dynamicDefinition,
                0,
                now.AddMinutes(1)), ct);

            await Assert.That(blocked.State)
                .IsEqualTo(ChallengeTemplateWriteState.InvalidDefinition);
            await Assert.That(blocked.Detail).Contains("regular-expression flags");

            await using var mutationDb = new NoCtfDbContext(options);
            var mutationFlags = new ManageChallengeFlags(new ChallengeFlagManagementStore(mutationDb));
            var ownerDeleted = await mutationFlags.DeleteAsync(
                ChallengeFlagScope.Template(challengeId),
                ownerSave.Value!.Id,
                ownerId,
                false,
                now.AddMinutes(2),
                ct);
            var managerDeleted = await mutationFlags.DeleteAsync(
                ChallengeFlagScope.Template(challengeId),
                managerSave.Value!.Id,
                managerId,
                false,
                now.AddMinutes(2),
                ct);
            await Assert.That(ownerDeleted.Succeeded).IsTrue();
            await Assert.That(managerDeleted.Succeeded).IsTrue();
            var activeFlagIds = await mutationDb.ChallengeFlags.AsNoTracking()
                .Where(item => item.ChallengeId == challengeId)
                .Select(item => item.Id)
                .ToArrayAsync(ct);
            await Assert.That(activeFlagIds).IsEmpty()
                .Because($"Active IDs: {string.Join(", ", activeFlagIds)}; owner={ownerSave.Value.Id}; manager={managerSave.Value.Id}");
            await using var conversionDb = new NoCtfDbContext(options);
            var converted = await new ChallengeBankStore(conversionDb).UpdateAsync(new(
                challengeId,
                ownerId,
                false,
                GameMode.Ctf,
                ChallengeVisibility.Private,
                "Static regex challenge",
                null,
                "Web",
                dynamicDefinition,
                0,
                now.AddMinutes(3)), ct);
            await using var restoreDb = new NoCtfDbContext(options);
            var restored = await new ManageChallengeFlags(new ChallengeFlagManagementStore(restoreDb)).RestoreAsync(
                ChallengeFlagScope.Template(challengeId),
                ownerSave.Value.Id,
                ownerId,
                false,
                now.AddMinutes(4),
                ct);

            await Assert.That(converted.State)
                .IsEqualTo(ChallengeTemplateWriteState.Succeeded)
                .Because(converted.Detail ?? "No detail was returned.");
            await Assert.That(restored.Succeeded).IsFalse();
            await Assert.That(await restoreDb.ChallengeFlags.IgnoreQueryFilters()
                .CountAsync(item => item.ChallengeId == challengeId && item.DeletedAt == null, ct))
                .IsEqualTo(0);
        });
    }

    private static SaveChallengeFlagCommand Command(
        Guid challengeId,
        string pattern,
        DateTimeOffset now) => new(
        ChallengeFlagScope.Template(challengeId),
        null,
        true,
        null,
        pattern,
        null,
        null,
        null,
        null,
        now,
        ChallengeFlagMatchKind.RegularExpression);

    private static SaveChallengeFlagCommand ExactCommand(
        Guid challengeId,
        string flag,
        DateTimeOffset now) => new(
        ChallengeFlagScope.Template(challengeId),
        null,
        true,
        null,
        flag,
        null,
        null,
        null,
        null,
        now,
        ChallengeFlagMatchKind.Exact);

    private static User User(Guid id, string name, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        NormalizedEmail = $"{name.ToUpperInvariant()}@EXAMPLE.TEST",
        PasswordHash = "test",
        Role = UserRole.Organizer,
        CreatedAt = now,
        UpdatedAt = now
    };
}
