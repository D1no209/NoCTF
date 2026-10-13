using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using NoCTF.Application.Authentication;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class AwdRoundCoordinationTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Checker_samples_retry_with_fenced_evidence_and_preserve_original_round_receipt_window(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awd_checker_evidence").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var fixture = await SeedAsync(options, ct, includeChecker: true);
            var clock = new MutableTimeProvider(fixture.Now);
            var outbox = new RecordingOutbox();
            await using (var setup = new NoCtfDbContext(options))
            {
                var template = await setup.Challenges.SingleAsync(ct);
                ((AwdChallengeDefinition)template.Definition!).FlagStores.Single().DeliveryMode = AwdFlagDeliveryMode.BusinessChecker;
                ((AwdChallengeDefinition)template.Definition!).Checker!.TargetServiceName = "main";
                ((AwdChallengeDefinition)template.Definition!).Checker!.TimeoutSeconds = 10;
                var runtime = await setup.RuntimeInstances.SingleAsync(ct);
                runtime.ProviderReceipt = RuntimeReceiptTestData.ContainerEntity(runtime.Id);
                await setup.SaveChangesAsync(ct);
                var rounds = new AwdRoundCoordinator(setup, new AwdRoundConfigurationCatalog(), outbox, clock, Secrets());
                await rounds.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
                await rounds.GenerateFlagsAsync(outbox.Published.OfType<GenerateAwdFlags>().Last(), ct);
            }
            var provider = Substitute.For<IContainerRuntime>();
            provider.GetStatusAsync(Arg.Any<ContainerDeploymentReceipt>(), Arg.Any<CancellationToken>())
                .Returns(new ContainerRuntimeStatus("awd-test", RuntimeStatus.Running,
                    [new("main", "test-runtime", RuntimeStatus.Running, new Dictionary<int, int>(), "runtime.internal")]));
            var providers = Substitute.For<IRuntimeProviderCatalog>();
            providers.Runtime(RuntimeProvider.Docker).Returns(provider);
            var tokens = new RunnerScoringTokenIssuer(Options.Create(new RunnerScoringOptions
            {
                SigningKey = new string('x', 32), Issuer = "awd-test", Audience = "awd-test"
            }));
            var reader = new AwdCheckerWorkReader(new TestContextFactory(options), new(), tokens, providers,
                Options.Create(new RunnerScoringOptions { CallbackBaseUrl = new Uri("http://localhost:5080") }), clock);
            async Task Dispatch()
            {
                await using var db = new NoCtfDbContext(options);
                await new AwdCheckerCoordinator(db, outbox, clock, NullCompetitionEventRecorder.Instance)
                    .DispatchAsync(new(clock.UtcNow), ct);
            }
            async Task<AwdFactDetails> Claim(RunAwdChecker message, bool concurrent = false)
            {
                var work = concurrent
                    ? await Task.WhenAll(reader.ReadAsync(message, ct), reader.ReadAsync(message, ct))
                    : [await reader.ReadAsync(message, ct)];
                await Assert.That(work.Count(item => item is not null)).IsEqualTo(1);
                await using var db = new NoCtfDbContext(options);
                var fact = await db.GameplayFacts.Include(item => item.Awd).ThenInclude(details => details!.Flags)
                    .SingleAsync(item => item.Id == message.GameplayFactId, ct);
                await Assert.That(fact.Awd!.Execution).IsEqualTo(AwdCheckExecution.Running);
                var taskReader = new AwdCheckerTaskReader(db, Secrets());
                var material = await taskReader.ReadAsync(message.RuntimeInstanceId, message.GameplayFactId, message.AttemptId, clock.UtcNow, ct);
                await Assert.That(material).IsNotNull();
                await Assert.That(material!.Flags.Count).IsEqualTo(fact.Awd.Flags.Count);
                await Assert.That(await taskReader.ReadAsync(message.RuntimeInstanceId, message.GameplayFactId, Guid.NewGuid(), clock.UtcNow, ct)).IsNull();
                await Assert.That(await taskReader.ReadAsync(message.RuntimeInstanceId, message.GameplayFactId, message.AttemptId, material.Deadline, ct)).IsNull();
                if (material.Flags.Any(flag => flag.Historical))
                    await Assert.That(material.Flags.Single(flag => flag.Historical).PrivateReadContext).IsEqualTo("private-read-credential");
                return fact.Awd!;
            }
            AwdCheckResult Pass(RunAwdChecker message, AwdFactDetails details, DateTimeOffset observed, DateTimeOffset received) =>
                new(message.RuntimeInstanceId, message.GameplayFactId, message.AttemptId,
                    new(AwdProbeOutcome.Passed), new(AwdProbeOutcome.Passed),
                    details.Flags.Select(flag => new AwdReportedFlag(flag.FlagId,
                        new(flag.RequiresWrite ? AwdProbeOutcome.Passed : AwdProbeOutcome.NotRequired),
                        new(AwdProbeOutcome.Passed), flag.RequiresWrite ? "private-read-credential" : null,
                        flag.RequiresWrite ? "record-42" : null)).ToArray(), observed, received);
            async Task<InternalResultDisposition> Record(AwdCheckResult result)
            {
                await using var db = new NoCtfDbContext(options);
                return await new InternalResultStore(db, new RecordingOutbox(), secrets: Secrets()).RecordAwdAsync(result, ct);
            }

            await Dispatch();
            var first = outbox.RunnerNodeMessages.OfType<RunAwdChecker>().Last();
            await Dispatch();
            await Assert.That(outbox.RunnerNodeMessages.OfType<RunAwdChecker>().Last().AttemptId).IsEqualTo(first.AttemptId);
            await Assert.That(first.DispatchAttemptId).IsNotEqualTo(Guid.Empty);
            await Assert.That(outbox.RunnerNodeMessages.OfType<RunAwdChecker>().Last().DispatchAttemptId).IsNotEqualTo(first.DispatchAttemptId);
            var firstDetails = await Claim(first, concurrent: true);
            await Assert.That(firstDetails.ManifestComplete).IsTrue();
            await Assert.That(firstDetails.Flags.Count).IsEqualTo(1);
            await Assert.That(firstDetails.AttemptDeadline).IsEqualTo(fixture.Now.AddSeconds(10));
            await Assert.That(firstDetails.ReceiptDeadline).IsEqualTo(fixture.Now.AddSeconds(40));
            await Assert.That(await Record(Pass(first, firstDetails, clock.UtcNow, clock.UtcNow))).IsEqualTo(InternalResultDisposition.Applied);
            await using (var verify = new NoCtfDbContext(options))
            {
                var issue = await verify.AwdFlagIssues.SingleAsync(ct);
                await Assert.That(issue.Obligation).IsEqualTo(AwdFlagObligation.PlayerRequired);
                await Assert.That(issue.PublicLocator).IsEqualTo("record-42");
                await Assert.That(issue.ProtectedReadContext).IsNotEqualTo("private-read-credential");
                await Assert.That(await verify.GameplayFacts.CountAsync(item => item.Kind == GameplayFactKind.AwdServiceTransition, ct))
                    .IsEqualTo(10);
            }

            clock.UtcNow = fixture.Now.AddSeconds(30);
            await Dispatch();
            var second = outbox.RunnerNodeMessages.OfType<RunAwdChecker>().Last();
            var secondDetails = await Claim(second);
            var failed = Pass(second, secondDetails, clock.UtcNow, clock.UtcNow) with
            {
                Connection = new(AwdProbeOutcome.PlatformFailure, AwdCheckReason.InfrastructureFailure)
            };
            await Assert.That(await Record(failed)).IsEqualTo(InternalResultDisposition.Applied);
            clock.UtcNow = fixture.Now.AddSeconds(35);
            await Dispatch();
            var retry = outbox.RunnerNodeMessages.OfType<RunAwdChecker>().Last();
            await Assert.That(retry.GameplayFactId).IsEqualTo(second.GameplayFactId);
            await Assert.That(retry.AttemptId).IsNotEqualTo(second.AttemptId);
            var retryDetails = await Claim(retry);
            await Assert.That(retryDetails.Attempt).IsEqualTo(2);
            await Assert.That(await Record(failed)).IsEqualTo(InternalResultDisposition.Superseded);
            var success = Pass(retry, retryDetails, clock.UtcNow, clock.UtcNow);
            var concurrent = await Task.WhenAll(Record(success), Record(success));
            await Assert.That(concurrent.Count(value => value == InternalResultDisposition.Applied)).IsEqualTo(1);
            await Assert.That(concurrent.Count(value => value == InternalResultDisposition.Duplicate)).IsEqualTo(1);

            clock.UtcNow = fixture.Now.AddSeconds(60);
            await Dispatch();
            var late = outbox.RunnerNodeMessages.OfType<RunAwdChecker>().Last();
            var lateDetails = await Claim(late);
            clock.UtcNow = fixture.Now.AddSeconds(90);
            await Dispatch();
            var exemptCandidate = outbox.RunnerNodeMessages.OfType<RunAwdChecker>().Last();
            var exemptDetails = await Claim(exemptCandidate);
            clock.UtcNow = fixture.Now.AddSeconds(121);
            await Dispatch();
            await Assert.That(await Record(Pass(late, lateDetails, fixture.Now.AddSeconds(65), clock.UtcNow)))
                .IsEqualTo(InternalResultDisposition.Applied);

            clock.UtcNow = fixture.Now.AddSeconds(270);
            await Dispatch();
            var final = outbox.RunnerNodeMessages.OfType<RunAwdChecker>().Last();
            var finalDetails = await Claim(final);
            await Assert.That(finalDetails.FinalScheduledCheck).IsTrue();
            await Assert.That(await Record(Pass(final, finalDetails, fixture.Now.AddSeconds(301), fixture.Now.AddSeconds(305))))
                .IsEqualTo(InternalResultDisposition.Conflict);
            await Assert.That(await Record(Pass(final, finalDetails, fixture.Now.AddSeconds(275), fixture.Now.AddSeconds(305))))
                .IsEqualTo(InternalResultDisposition.Applied);
            clock.UtcNow = fixture.Now.AddSeconds(331);
            await Dispatch();
            await using var results = new NoCtfDbContext(options);
            var facts = await results.GameplayFacts.Include(item => item.Awd)
                .Where(item => item.Kind == GameplayFactKind.AwdServiceTransition).ToArrayAsync(ct);
            await Assert.That(facts.Count(item => item.Awd!.Settlement == AwdSettlementState.Final)).IsEqualTo(3);
            await Assert.That(facts.Count(item => item.Awd!.Settlement == AwdSettlementState.PendingAdjudication)).IsEqualTo(7);
            await Assert.That(facts.Where(item => item.Awd!.Health == AwdHealth.InternalError).All(item => item.Result is null)).IsTrue();
            await Assert.That(facts.Single(item => item.Id == final.GameplayFactId).Awd!.ReceivedRoundNumber).IsEqualTo(1);
            await Assert.That(facts.Single(item => item.Id == late.GameplayFactId).Awd!.Reason).IsEqualTo(AwdCheckReason.LateReceipt);

            // The next round cannot reuse last round's conclusion and must read its historical flag.
            await using (var nextRound = new NoCtfDbContext(options))
            {
                var rounds = new AwdRoundCoordinator(nextRound, new AwdRoundConfigurationCatalog(), outbox, clock, Secrets());
                await rounds.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
                await rounds.GenerateFlagsAsync(outbox.Published.OfType<GenerateAwdFlags>().Last(), ct);
            }
            await Dispatch();
            var nextMessage = outbox.RunnerNodeMessages.OfType<RunAwdChecker>().Last();
            var nextDetails = await Claim(nextMessage);
            await Assert.That(nextDetails.ReceivedRoundNumber).IsEqualTo(2);
            await Assert.That(nextDetails.Flags.Count(flag => flag.Historical)).IsEqualTo(1);
            await Assert.That(nextDetails.Flags.Where(flag => flag.Historical).All(flag => !flag.RequiresWrite)).IsTrue();
            var completeReport = Pass(nextMessage, nextDetails, clock.UtcNow, clock.UtcNow);
            await Assert.That(await Record(completeReport with { Flags = completeReport.Flags.Take(1).ToArray() }))
                .IsEqualTo(InternalResultDisposition.Applied);
            clock.UtcNow = clock.UtcNow.AddSeconds(5);
            await Dispatch();
            var recoveryMessage = outbox.RunnerNodeMessages.OfType<RunAwdChecker>().Last();
            var recoveryDetails = await Claim(recoveryMessage);
            var historicalId = recoveryDetails.Flags.Single(flag => flag.Historical).FlagId;
            var recovery = Pass(recoveryMessage, recoveryDetails, clock.UtcNow, clock.UtcNow);
            recovery = recovery with { Flags = recovery.Flags.Select(flag => flag.FlagId == historicalId
                ? flag with { Read = new(AwdProbeOutcome.TargetFailure, AwdCheckReason.HistoricalFlagMissing) } : flag).ToArray() };
            await Assert.That(await Record(recovery)).IsEqualTo(InternalResultDisposition.Applied);
            await using var recovered = new NoCtfDbContext(options);
            var conclusion = await recovered.AwdFactDetails.SingleAsync(item => item.GameplayFactId == recoveryMessage.GameplayFactId, ct);
            await Assert.That(conclusion.Health).IsEqualTo(AwdHealth.Recovering);
            await Assert.That(conclusion.ReadableFlags).IsEqualTo(1);
            await Assert.That(conclusion.RequiredFlags).IsEqualTo(2);

            var nextPending = await recovered.GameplayFacts.AsNoTracking().Include(item => item.Awd)
                .Where(item => item.Awd != null && item.Awd.ReceivedRoundNumber == 2
                    && item.OccurredAt > clock.UtcNow).OrderBy(item => item.OccurredAt).FirstAsync(ct);
            clock.UtcNow = fixture.Now.AddSeconds(338);
            await using (var lifecycleDb = new NoCtfDbContext(options))
            {
                var lifecycle = new CompetitionLifecycleStore(lifecycleDb, null!, outbox,
                    new CompetitionEventStore(lifecycleDb, outbox), clock: clock);
                await Assert.That(await lifecycle.TryTransitionWithAuditAsync(fixture.CompetitionId,
                    CompetitionStatus.Running, CompetitionStatus.Paused, fixture.OwnerId, "pause checker plan", false,
                    CompetitionLifecycleEffects.None, ct)).IsTrue();
                clock.UtcNow = fixture.Now.AddSeconds(398);
                await Dispatch();
                await Assert.That(await lifecycle.TryTransitionWithAuditAsync(fixture.CompetitionId,
                    CompetitionStatus.Paused, CompetitionStatus.Running, fixture.OwnerId, "resume checker plan", false,
                    CompetitionLifecycleEffects.None, ct)).IsTrue();
            }
            await Dispatch();
            await using (var shifted = new NoCtfDbContext(options))
            {
                var check = await shifted.AwdFactDetails.SingleAsync(item => item.GameplayFactId == nextPending.Id, ct);
                await Assert.That(check.ScheduledAt).IsEqualTo(nextPending.OccurredAt.AddSeconds(60));
                await Assert.That(check.Execution).IsEqualTo(AwdCheckExecution.Pending);
                // A missing target is a platform uncertainty; it is not a fabricated successful check.
                await shifted.RuntimeInstances.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.State, RuntimeState.Stopped), ct);
                clock.UtcNow = check.Deadline!.Value.AddSeconds(1);
            }
            await Dispatch();
            await using var unavailable = new NoCtfDbContext(options);
            var missingTarget = await unavailable.GameplayFacts.Include(item => item.Awd).SingleAsync(item => item.Id == nextPending.Id, ct);
            await Assert.That(missingTarget.Awd!.Reason).IsEqualTo(AwdCheckReason.TaskNotExecuted);
            await Assert.That(missingTarget.Awd.Settlement).IsEqualTo(AwdSettlementState.PendingAdjudication);
            await Assert.That(missingTarget.Result).IsNull();
            await VerifyAdjudicationsAsync(options, fixture, late.GameplayFactId, exemptCandidate.GameplayFactId,
                missingTarget.Id, clock.UtcNow, ct);
            await Assert.That(await Record(Pass(exemptCandidate, exemptDetails, fixture.Now.AddSeconds(95), clock.UtcNow)))
                .IsEqualTo(InternalResultDisposition.Duplicate);
        });
    }
}
