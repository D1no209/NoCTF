using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awd.Flags;
using NoCTF.GameModes.Flags;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class AwdInjectionWindowDockerTests
{
    [Test, Arguments(false), Arguments(true), Timeout(300_000)]
    public async Task Effective_round_end_bounds_real_delivery_after_the_scheduled_competition_end(bool totalRounds, CancellationToken ct)
    {
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct);
        var db = fixture.Db; var owner = fixture.Application.OwnerId; var competitionId = Guid.NewGuid();
        var entryId = Guid.NewGuid(); var teamId = Guid.NewGuid(); var storeId = Guid.NewGuid();
        var configuration = (AwdCompetitionModeConfiguration)CompetitionModeConfigurationDefaults.Create(GameMode.Awd, competitionId);
        var competition = new AwdCompetition { Id = competitionId, OwnerId = owner, Title = "Effective delivery window", Status = CompetitionStatus.Running,
            StartAt = DateTimeOffset.UtcNow.AddMinutes(-1), EndAt = DateTimeOffset.UtcNow.AddHours(1), ModeConfiguration = configuration };
        db.Competitions.Add(competition);
        db.CompetitionChallenges.Add(new AwdCompetitionChallenge { Id = entryId, CompetitionId = competitionId, ChallengeId = fixture.Application.TemplateId,
            Rules = new AwdCompetitionChallengeRules(), IsPublished = true });
        db.Teams.Add(new Team { Id = teamId, CompetitionId = competitionId, CaptainId = owner, MemberIds = [owner], Name = "Delivery team",
            InvitationToken = Guid.NewGuid().ToString("N"), RegistrationStatus = TeamRegistrationStatus.Approved });
        await db.SaveChangesAsync(ct);
        var message = await fixture.QueueAsync(1, ct, new PlayerRuntimeInstance { Id = Guid.NewGuid(), CompetitionId = competitionId,
            CompetitionChallengeId = entryId, TeamId = teamId, State = RuntimeState.Queued, RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker,
            CreatedAt = DateTimeOffset.UtcNow });
        var target = await fixture.Handler.ProvisionContainerAsync(message, ct) as NoCTF.Application.Runtime.Instances.RuntimeProvisioned
            ?? throw new InvalidOperationException("The delivery target did not start.");
        await fixture.PersistAsync(target, ct);
        var start = DateTimeOffset.UtcNow; start = start.AddTicks(-(start.Ticks % TimeSpan.TicksPerMicrosecond));
        competition.StartAt = start.AddSeconds(-600); competition.EndAt = start.AddSeconds(totalRounds ? 180 : 360);
        configuration.HardeningDurationSeconds = 600; configuration.RoundDurationSeconds = 180;
        configuration.AwdOptions = configuration.AwdOptions with { TotalRounds = totalRounds ? 3 : null, RoundGapSeconds = totalRounds ? 0 : 30 };
        await new CompetitionEventStore(db, fixture.Application.Publisher).RecordAsync(new(competitionId, CompetitionEventKind.CompetitionLifecycleChanged,
            CompetitionEventLevel.Information, CompetitionEventVisibility.Staff, competition.StartAt, CompetitionStatus: CompetitionStatus.Running,
            PreviousCompetitionStatus: CompetitionStatus.Published), ct);
        var rounds = Enumerable.Range(0, totalRounds ? 3 : 2).Select(index => new AwdRound { Id = Guid.NewGuid(), CompetitionId = competitionId,
            Number = index + 1, StartsAt = start.AddSeconds(index * (totalRounds ? 180 : 210)), EndsAt = start.AddSeconds(index * (totalRounds ? 180 : 210) + 180),
            ActiveStartTicks = TimeSpan.FromSeconds(600 + index * 180).Ticks, ActiveEndTicks = TimeSpan.FromSeconds(600 + (index + 1) * 180).Ticks,
            GapSeconds = totalRounds ? 0 : 30 }).ToArray();
        db.AwdRounds.AddRange(rounds);
        var current = rounds[^1];
        var rules = new AwdRoundRules { Id = Guid.NewGuid(), CompetitionId = competitionId, CompetitionChallengeId = entryId, RoundId = current.Id,
            EffectiveAt = current.StartsAt, CheckerIntervalSeconds = 60, EligibleTeamCount = 1,
            GenerationEpochRound = current.Number, GenerationEpochActiveTicks = current.ActiveStartTicks,
            Stores = [new() { StoreId = storeId, Name = "main", TargetServiceName = "service-0", FlagsPerIssue = 2,
                DeliveryMode = AwdFlagDeliveryMode.RunnerCommand, InjectionTimeoutSeconds = 30,
                InjectionCommand = "printf '%s' \"$FLAG\" > /issued-flag; printf delivery >> /delivery-count" }] };
        db.AwdRoundRules.Add(rules);
        var clock = new ObservationClock(start.AddSeconds(375));
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var key = new AwdFlagKey { Id = Guid.NewGuid(), CompetitionId = competitionId, Version = 1, CreatedAt = start };
        var secrets = new PlatformSecretProtector(Options.Create(new EmailVerificationProtectionOptions { EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));
        key.ProtectedSecret = secrets.Protect(Convert.ToBase64String(keyBytes), PlatformSecretPurpose.AwdFlagKey, competitionId, key.Id); db.AwdFlagKeys.Add(key);
        var flags = Enumerable.Range(0, 2).Select(ordinal =>
        {
            var value = AwdFlagGenerator.Generate(new PerTeamFlagTemplate("flag", "[SECURE]", false), keyBytes,
                new(competitionId, entryId, teamId, current.Id, storeId, 0, ordinal, 1));
            var id = Guid.NewGuid(); var digest = ManageChallengeFlags.Hash(value);
            return new AwdRoundChallengeFlag { Id = id, CompetitionChallengeId = entryId, TeamId = teamId, Flag = value, FlagSha256 = digest,
                SpecificationKind = SpecificationKind.AwdRound, SpecificationId = AwdRoundSpecificationId.FromRound(current.Number).Value,
                ValidStart = current.StartsAt, ValidUntil = current.EndsAt, CreatedAt = clock.GetUtcNow(),
                AwdIssue = new() { FlagId = id, CompetitionId = competitionId, CompetitionChallengeId = entryId, TeamId = teamId, RoundId = current.Id,
                    RoundNumber = current.Number, RulesId = rules.Id, StoreId = storeId, GenerationSlot = 0, Ordinal = ordinal,
                    State = AwdFlagIssueState.Issued, DeliveryState = AwdFlagDeliveryState.Pending, DeliveryRuntimeId = message.RuntimeInstanceId,
                    IssuedAt = clock.GetUtcNow(), UpdatedAt = clock.GetUtcNow(), ActiveStartTicks = current.ActiveStartTicks, Digest = Convert.ToHexString(digest) } };
        }).ToArray();
        CryptographicOperations.ZeroMemory(keyBytes); db.ChallengeFlags.AddRange(flags); await db.SaveChangesAsync(ct);
        var reader = new AwdFlagInjectionWorkReader(fixture.Contexts, clock);
        var first = new InjectAwdFlag(message.RuntimeInstanceId, entryId, flags[0].Id, current.EndsAt, RuntimeExecutionFixture.RunnerId);
        var work = await reader.ReadAsync(first, ct); await Assert.That(work).IsNotNull();
        await Assert.That(work!.Timeout).IsEqualTo(TimeSpan.FromSeconds(totalRounds ? 30 : 15));
        var executed = await new AwdFlagInjectionExecutor(fixture.Providers).ExecuteAsync(work, ct);
        await Assert.That(executed.ExitCode).IsEqualTo(0);
        await reader.CompleteAsync(first, work, AwdFlagDeliveryState.Succeeded, AwdCheckReason.None, ct);
        await Assert.That(await reader.ReadAsync(first, ct)).IsNull();
        clock.UtcNow = current.EndsAt; await fixture.RefreshOwnerAsync(ct);
        using var mutations = new RunnerResourceMutationCoordinator();
        var handler = new AwdFlagInjectionHandler(reader, new AwdFlagInjectionExecutor(fixture.Providers), fixture.Application.Publisher,
            fixture.RunnerOptions, clock, mutations, fixture.Capacity);
        await Assert.That(await handler.Handle(first with { ChallengeFlagId = flags[1].Id }, ct)).IsEqualTo(MessageExecutionOutcome.Superseded);
        var receipt = (NoCTF.Application.Runtime.Provisioning.ContainerRuntimeReceiptData)target.ProviderReceipt;
        var unchanged = await fixture.Providers.Runtime(RuntimeProvider.Docker).ExecAsync(receipt.ToReceipt(), "service-0",
            ["/bin/sh", "-c", "test \"$(cat /issued-flag)\" = \"$1\" && test \"$(cat /delivery-count)\" = delivery", "verify-delivery", flags[0].Flag], TimeSpan.FromSeconds(5), ct);
        await Assert.That(unchanged.ExitCode).IsEqualTo(0);
        db.ChangeTracker.Clear(); var issued = await db.AwdFlagIssues.SingleAsync(value => value.FlagId == flags[0].Id, ct);
        await Assert.That(issued.Obligation).IsEqualTo(AwdFlagObligation.PlayerRequired);
        await Assert.That(competition.EndAt).IsEqualTo(start.AddSeconds(totalRounds ? 180 : 360));
    }

    private sealed class ObservationClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
