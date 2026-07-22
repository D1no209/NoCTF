using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Application.SystemProducers;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Persistence.UseCaseAdapters;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RuntimeOperationClaimStoreTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Producer_dispatch_completion_is_allowed_after_pause_or_finish(
        CancellationToken cancellationToken)
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("noctf_producer_completion")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        var now = DateTimeOffset.UtcNow;
        var cases = new[]
        {
            (CompetitionId: Guid.CreateVersion7(now), Status: CompetitionStatus.Paused),
            (CompetitionId: Guid.CreateVersion7(now.AddTicks(1)), Status: CompetitionStatus.Finished)
        };
        await using (var setup = new NoCtfDbContext(options))
        {
            await setup.Database.MigrateAsync(cancellationToken);
            setup.Competitions.AddRange(cases.Select(item => new Competition
            {
                Id = item.CompetitionId,
                Title = $"Producer completion {item.Status}",
                OwnerId = Guid.NewGuid(),
                Mode = GameMode.Awd,
                StartTime = now.AddHours(-1),
                EndTime = now.AddHours(1),
                Status = CompetitionStatus.Running,
                CreatedAt = now,
                UpdatedAt = now
            }));
            await setup.SaveChangesAsync(cancellationToken);
        }

        foreach (var item in cases)
        {
            var operationKey = $"awd:{item.CompetitionId:N}:checker";
            ProducerDispatchClaim claim;
            await using (var begin = new NoCtfDbContext(options))
            {
                claim = (await new EfProducerDispatchClaimStore(begin).TryBeginAsync(
                    item.CompetitionId, operationKey, now.AddMinutes(-5), now,
                    cancellationToken))!;
            }
            await using (var transition = new NoCtfDbContext(options))
            {
                await transition.Competitions.Where(competition => competition.Id == item.CompetitionId)
                    .ExecuteUpdateAsync(update => update.SetProperty(
                        competition => competition.Status, item.Status), cancellationToken);
            }
            await using (var complete = new NoCtfDbContext(options))
            {
                await Assert.That(await new EfProducerDispatchClaimStore(complete).CompleteAsync(
                    item.CompetitionId, operationKey, claim, true, now.AddMinutes(1), cancellationToken)).IsTrue();
            }
        }

        await using var verify = new NoCtfDbContext(options);
        var operations = await verify.RuntimeOperations.AsNoTracking().ToListAsync(cancellationToken);
        await Assert.That(operations.Count).IsEqualTo(2);
        await Assert.That(operations.All(operation => operation.Status == RuntimeStatus.Stopped)).IsTrue();
    }

    [Test]
    [Timeout(300_000)]
    public async Task Committed_awd_callback_fact_closes_failed_dispatch_without_reexecution(
        CancellationToken cancellationToken)
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("noctf_producer_callback_recovery")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        var now = DateTimeOffset.UtcNow;
        var competitionId = Guid.CreateVersion7(now);
        var operationKey = $"awd:{competitionId:N}:checker";
        await using (var setup = new NoCtfDbContext(options))
        {
            await setup.Database.MigrateAsync(cancellationToken);
            setup.Competitions.Add(new Competition
            {
                Id = competitionId,
                Title = "Committed AWD callback",
                OwnerId = Guid.NewGuid(),
                Mode = GameMode.Awd,
                StartTime = now.AddHours(-1),
                EndTime = now.AddHours(1),
                Status = CompetitionStatus.Running,
                CreatedAt = now,
                UpdatedAt = now
            });
            setup.RuntimeOperations.Add(new RuntimeOperation
            {
                Id = Guid.CreateVersion7(now.AddTicks(1)),
                CompetitionId = competitionId,
                OperationKey = operationKey,
                Kind = RuntimeOperationKind.OneShot,
                Status = RuntimeStatus.Starting,
                ClaimToken = Guid.CreateVersion7(now.AddTicks(2)),
                CreatedAt = now,
                UpdatedAt = now
            });
            setup.ScoringEvents.Add(new NoCTF.Domain.Submissions.ScoringEvent
            {
                Id = Guid.CreateVersion7(now.AddTicks(3)),
                CompetitionId = competitionId,
                Kind = NoCTF.Domain.Submissions.ScoringEventKind.AwdServiceCheck,
                Result = NoCTF.Domain.Submissions.ScoringResult.Correct,
                OccurredAt = now,
                ProcessedAt = now,
                EvaluatorVersion = "awd-checker-v1",
                SourceKey = operationKey,
                CreatedAt = now
            });
            await setup.SaveChangesAsync(cancellationToken);
        }

        ProducerDispatchClaim originalClaim;
        await using (var load = new NoCtfDbContext(options))
        {
            var loadedOperation = await load.RuntimeOperations.AsNoTracking().SingleAsync(cancellationToken);
            originalClaim = new(loadedOperation.Id, loadedOperation.ClaimToken);
        }
        await using (var responseLost = new NoCtfDbContext(options))
        {
            await Assert.That(await new EfProducerDispatchClaimStore(responseLost).CompleteAsync(
                competitionId, operationKey, originalClaim, false, now.AddMinutes(1), cancellationToken)).IsTrue();
        }
        await using (var retry = new NoCtfDbContext(options))
        {
            var claim = await new EfProducerDispatchClaimStore(retry).TryBeginAsync(
                competitionId, operationKey, now.AddMinutes(-5), now.AddMinutes(2), cancellationToken);
            await Assert.That(claim).IsNull();
        }
        await using var verify = new NoCtfDbContext(options);
        var operation = await verify.RuntimeOperations.AsNoTracking().SingleAsync(cancellationToken);
        await Assert.That(operation.Status).IsEqualTo(RuntimeStatus.Stopped);
        await Assert.That(operation.ErrorCode).IsNull();
    }

    [Test]
    [Timeout(300_000)]
    public async Task Health_failure_marks_operation_reclaimable_for_reprovision(
        CancellationToken cancellationToken)
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("noctf_runtime_health")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(cancellationToken);

        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        var now = DateTimeOffset.UtcNow;
        var competitionId = Guid.CreateVersion7(now);
        var competitionChallengeId = Guid.CreateVersion7(now.AddTicks(1));
        var instanceId = Guid.CreateVersion7(now.AddTicks(2));
        var teamId = Guid.CreateVersion7(now.AddTicks(3));
        var activeFlagId = Guid.CreateVersion7(now.AddTicks(4));
        var pendingFlagId = Guid.CreateVersion7(now.AddTicks(5));
        await using (var setup = new NoCtfDbContext(options))
        {
            await setup.Database.MigrateAsync(cancellationToken);
            setup.Competitions.Add(new Competition
            {
                Id = competitionId, Title = "Runtime health", OwnerId = Guid.NewGuid(), Mode = GameMode.Awd,
                StartTime = now.AddHours(-1), EndTime = now.AddHours(1), Status = CompetitionStatus.Running,
                CreatedAt = now, UpdatedAt = now
            });
            var template = new Challenge
            {
                Id = Guid.CreateVersion7(now.AddTicks(3)), Title = "Runtime health challenge",
                CreatedAt = now, UpdatedAt = now
            };
            setup.Challenges.Add(template);
            setup.CompetitionChallenges.Add(new CompetitionChallenge
            {
                Id = competitionChallengeId, CompetitionId = competitionId, ChallengeId = template.Id,
                BaseScore = 100, IsPublished = true, ConfigurationJson = "{}", UpdatedAt = now
            });
            setup.Teams.Add(new Team
            {
                Id = teamId, CompetitionId = competitionId, Name = "Runtime health team",
                InvitationToken = "0123456789abcdef0123456789abcdef", RegistrationStatus = TeamRegistrationStatus.Approved,
                RegisteredAt = now
            });
            setup.ChallengeInstances.Add(new ChallengeInstance
            {
                Id = instanceId, CompetitionId = competitionId, CompetitionChallengeId = competitionChallengeId,
                TeamId = teamId, Provider = RuntimeProvider.Kubernetes, Receipt = "{}", Status = RuntimeStatus.Running,
                CreatedAt = now, UpdatedAt = now
            });
            setup.RuntimeOperations.Add(new RuntimeOperation
            {
                Id = Guid.CreateVersion7(now.AddTicks(6)), CompetitionId = competitionId,
                ChallengeInstanceId = instanceId, OperationKey = "runtime:health", Kind = RuntimeOperationKind.CreateContainer,
                Status = RuntimeStatus.Running, ClaimToken = Guid.CreateVersion7(now.AddTicks(7)),
                CreatedAt = now, UpdatedAt = now
            });
            setup.ChallengeFlags.AddRange(
                new NoCTF.Domain.Challenges.ChallengeFlag
                {
                    Id = activeFlagId, CompetitionId = competitionId,
                    CompetitionChallengeId = competitionChallengeId, TeamId = teamId,
                    ChallengeInstanceId = instanceId, Flag = "NOCTF{active-runtime-health}",
                    ValidStart = now, ValidEnd = now.AddMinutes(10),
                    Status = NoCTF.Domain.Challenges.ChallengeFlagStatus.Active,
                    CreatedAt = now, UpdatedAt = now
                },
                new NoCTF.Domain.Challenges.ChallengeFlag
                {
                    Id = pendingFlagId, CompetitionId = competitionId,
                    CompetitionChallengeId = competitionChallengeId, TeamId = teamId,
                    ChallengeInstanceId = instanceId, Flag = "NOCTF{pending-runtime-health}",
                    ValidStart = now.AddMinutes(-1), ValidEnd = now.AddMinutes(9),
                    Status = NoCTF.Domain.Challenges.ChallengeFlagStatus.PendingInjection,
                    InjectionClaimToken = Guid.NewGuid(), InjectionClaimedAt = now,
                    CreatedAt = now, UpdatedAt = now
                });
            await setup.SaveChangesAsync(cancellationToken);
        }

        await using (var health = new NoCtfDbContext(options))
            await new EfRuntimeHealthStore(health).UpdateStatusAsync(
                instanceId, RuntimeStatus.Running, RuntimeStatus.Failed,
                now.AddMinutes(1), cancellationToken);

        await using var verify = new NoCtfDbContext(options);
        await Assert.That((await verify.ChallengeInstances.SingleAsync(cancellationToken)).Status)
            .IsEqualTo(RuntimeStatus.Failed);
        await Assert.That((await verify.RuntimeOperations.SingleAsync(cancellationToken)).Status)
            .IsEqualTo(RuntimeStatus.Failed);
        var failedFlags = await verify.ChallengeFlags.AsNoTracking()
            .Where(flag => flag.ChallengeInstanceId == instanceId)
            .ToListAsync(cancellationToken);
        var activeValidEnd = failedFlags.Single(flag => flag.Id == activeFlagId).ValidEnd;
        await Assert.That(activeValidEnd).IsNotNull();
        await Assert.That(activeValidEnd!.Value).IsLessThanOrEqualTo(now.AddMinutes(1));
        await Assert.That(failedFlags.Single(flag => flag.Id == pendingFlagId).InjectionClaimToken).IsNull();
        await Assert.That(failedFlags.Single(flag => flag.Id == pendingFlagId).InjectionClaimedAt).IsNull();
        var reclaimed = await new EfRuntimeOperationStore(verify).BeginAsync(
            competitionId, "runtime:health", RuntimeOperationKind.CreateContainer,
            now, now.AddMinutes(2), cancellationToken);
        await Assert.That(reclaimed.Lease).IsNotNull();
        await Assert.That(reclaimed.Lease!.IsNew).IsTrue();
        var replacementInstanceId = Guid.CreateVersion7(now.AddMinutes(2));
        var replacementReceipt = Receipt(reclaimed.Lease.ClaimToken, "replacement-resource");
        await Assert.That(await new EfRuntimeOperationStore(verify).CompleteAsync(
            reclaimed.Lease, replacementReceipt, competitionChallengeId, teamId,
            replacementInstanceId, null, now.AddMinutes(2), cancellationToken)).IsTrue();
        var rotationStore = new EfAwdFlagRotationStore(verify);
        var target = new NoCTF.Application.SystemProducers.AwdFlagRotationTarget(
            competitionId, competitionChallengeId, teamId, replacementInstanceId, 0,
            now.AddHours(-1), "{}", "{}", replacementReceipt);
        var replacementClaim = await rotationStore.TryClaimAsync(
            target, now, now.AddMinutes(10), "NOCTF{unused-new-value}",
            now.AddMinutes(1), now.AddMinutes(2), cancellationToken);
        await Assert.That(replacementClaim).IsNotNull();
        var reclaimedFlag = await verify.ChallengeFlags.AsNoTracking()
            .SingleAsync(flag => flag.Id == activeFlagId, cancellationToken);
        await Assert.That(reclaimedFlag.Status)
            .IsEqualTo(NoCTF.Domain.Challenges.ChallengeFlagStatus.PendingInjection);
        await Assert.That(reclaimedFlag.ChallengeInstanceId).IsEqualTo(replacementInstanceId);

        await verify.ChallengeInstances.Where(instance => instance.Id == replacementInstanceId)
            .ExecuteUpdateAsync(update => update.SetProperty(
                instance => instance.Status, RuntimeStatus.Stopped), cancellationToken);
        await verify.RuntimeOperations.Where(operation => operation.ChallengeInstanceId == replacementInstanceId)
            .ExecuteUpdateAsync(update => update.SetProperty(
                operation => operation.Status, RuntimeStatus.Stopped), cancellationToken);
        var staleHealthUpdate = await new EfRuntimeHealthStore(verify).UpdateStatusAsync(
            replacementInstanceId, RuntimeStatus.Running, RuntimeStatus.Failed,
            now.AddMinutes(3), cancellationToken);
        var staleRotationUpdate = await rotationStore.MarkRuntimeFailedAsync(
            competitionId, replacementInstanceId, now.AddMinutes(3), cancellationToken);
        await Assert.That(staleHealthUpdate).IsFalse();
        await Assert.That(staleRotationUpdate).IsFalse();
        await Assert.That((await verify.ChallengeInstances.AsNoTracking()
            .SingleAsync(instance => instance.Id == replacementInstanceId, cancellationToken)).Status)
            .IsEqualTo(RuntimeStatus.Stopped);
        await Assert.That((await verify.RuntimeOperations.AsNoTracking()
            .SingleAsync(operation => operation.ChallengeInstanceId == replacementInstanceId, cancellationToken)).Status)
            .IsEqualTo(RuntimeStatus.Stopped);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Stale_claim_is_single_owner_and_fences_old_owner(
        CancellationToken cancellationToken)
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("noctf_runtime_claims")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(cancellationToken);

        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        var now = DateTimeOffset.UtcNow;
        var competitionId = Guid.CreateVersion7(now);
        var competitionChallengeId = Guid.CreateVersion7(now.AddTicks(1));
        await using (var setup = new NoCtfDbContext(options))
        {
            await setup.Database.MigrateAsync(cancellationToken);
            setup.Competitions.Add(new Competition
            {
                Id = competitionId,
                Title = "Runtime recovery",
                OwnerId = Guid.NewGuid(),
                Mode = GameMode.Ctf,
                StartTime = now.AddHours(-1),
                EndTime = now.AddHours(1),
                Status = CompetitionStatus.Running,
                CreatedAt = now,
                UpdatedAt = now
            });
            var template = new Challenge
            {
                Id = Guid.CreateVersion7(now.AddTicks(2)),
                Title = "Runtime challenge",
                CreatedAt = now,
                UpdatedAt = now
            };
            setup.Challenges.Add(template);
            setup.CompetitionChallenges.Add(new CompetitionChallenge
            {
                Id = competitionChallengeId,
                CompetitionId = competitionId,
                ChallengeId = template.Id,
                BaseScore = 100,
                Order = 0,
                IsPublished = true,
                ConfigurationJson = """{"schemaVersion":1}""",
                Revision = 0,
                UpdatedAt = now
            });
            await setup.SaveChangesAsync(cancellationToken);
        }

        RuntimeOperationLease originalLease;
        var preparedInstanceId = Guid.NewGuid();
        await using (var firstContext = new NoCtfDbContext(options))
        {
            var first = await new EfRuntimeOperationStore(firstContext).BeginAsync(
                competitionId,
                "runtime:recovery",
                RuntimeOperationKind.CreateContainer,
                now.AddMinutes(-6),
                now,
                cancellationToken);
            originalLease = first.Lease!;
        }

        await using (var ageContext = new NoCtfDbContext(options))
        {
            ageContext.ChallengeInstances.Add(new ChallengeInstance
            {
                Id = preparedInstanceId,
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                TeamId = Guid.NewGuid(),
                Provider = RuntimeProvider.Docker,
                Receipt = "{}",
                Status = RuntimeStatus.Starting,
                CreatedAt = now,
                UpdatedAt = now
            });
            ageContext.ChallengeFlags.Add(new NoCTF.Domain.Challenges.ChallengeFlag
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                TeamId = Guid.NewGuid(),
                StageId = Guid.NewGuid(),
                ChallengeInstanceId = preparedInstanceId,
                Flag = "NOCTF{prepared-orphan}",
                ValidStart = now,
                ValidEnd = now.AddHours(1),
                CreatedAt = now,
                UpdatedAt = now
            });
            await ageContext.SaveChangesAsync(cancellationToken);
            await ageContext.RuntimeOperations.ExecuteUpdateAsync(
                update => update
                    .SetProperty(operation => operation.ChallengeInstanceId, preparedInstanceId)
                    .SetProperty(operation => operation.UpdatedAt, now.AddMinutes(-10)),
                cancellationToken);
        }

        var claims = await Task.WhenAll(
            ClaimAsync(options, competitionId, now.AddMinutes(1), cancellationToken),
            ClaimAsync(options, competitionId, now.AddMinutes(1), cancellationToken));
        await Assert.That(claims.Count(result => result.Lease!.IsNew)).IsEqualTo(1);
        var winningLease = claims.Single(result => result.Lease!.IsNew).Lease!;
        await Assert.That(winningLease.ClaimToken).IsNotEqualTo(originalLease.ClaimToken);

        var oldReceipt = Receipt(originalLease.ClaimToken, "old-resource");
        await using (var staleContext = new NoCtfDbContext(options))
        {
            var completed = await new EfRuntimeOperationStore(staleContext).CompleteAsync(
                originalLease, oldReceipt, competitionChallengeId, null, Guid.NewGuid(), null, now, cancellationToken);
            await Assert.That(completed).IsFalse();
        }

        var winnerReceipt = Receipt(winningLease.ClaimToken, "winner-resource");
        await using (var winnerContext = new NoCtfDbContext(options))
        {
            var completed = await new EfRuntimeOperationStore(winnerContext).CompleteAsync(
                winningLease, winnerReceipt, competitionChallengeId, null, Guid.NewGuid(), null, now, cancellationToken);
            await Assert.That(completed).IsTrue();
        }

        await using (var staleFailureContext = new NoCtfDbContext(options))
        {
            _ = await new EfRuntimeOperationStore(staleFailureContext).FailAsync(
                originalLease,
                new(RuntimeProvisionFailure.PersistenceRejected, competitionChallengeId, null, null, oldReceipt),
                now,
                cancellationToken);
        }

        await using var verify = new NoCtfDbContext(options);
        var operation = await verify.RuntimeOperations.AsNoTracking().SingleAsync(cancellationToken);
        await Assert.That(operation.Status).IsEqualTo(RuntimeStatus.Running);
        await Assert.That(operation.ClaimToken).IsEqualTo(winningLease.ClaimToken);
        var instances = await verify.ChallengeInstances.AsNoTracking().ToListAsync(cancellationToken);
        await Assert.That(instances.Count).IsEqualTo(3);
        await Assert.That(instances.Count(instance => instance.Status == RuntimeStatus.Failed)).IsEqualTo(2);
        var prepared = instances.Single(instance => instance.Id == preparedInstanceId);
        await Assert.That(prepared.Status).IsEqualTo(RuntimeStatus.Failed);
        var preparedFlag = await verify.ChallengeFlags.AsNoTracking()
            .SingleAsync(flag => flag.ChallengeInstanceId == preparedInstanceId, cancellationToken);
        await Assert.That(preparedFlag.ValidEnd).IsNotNull();
        await Assert.That(preparedFlag.ValidEnd!.Value).IsLessThanOrEqualTo(now.AddMinutes(1));
    }

    private static async Task<RuntimeOperationBeginResult> ClaimAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid competitionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        return await new EfRuntimeOperationStore(db).BeginAsync(
            competitionId,
            "runtime:recovery",
            RuntimeOperationKind.CreateContainer,
            now.AddMinutes(-6),
            now,
            cancellationToken);
    }

    private static ContainerReceipt Receipt(Guid claimToken, string resourceId) => new(
        claimToken,
        RuntimeProvider.Docker,
        resourceId,
        RuntimeStatus.Running,
        new Dictionary<int, int>(),
        "localhost",
        null);
}
