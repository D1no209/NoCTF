using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using NoCTF.Application.Messaging;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runner.Messages;
using Wolverine.Attributes;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class AwdFlagInjectionHandlerTests
{
    [Test]
    public async Task Provider_handler_explicitly_opts_out_of_ambient_database_transactions()
    {
        await Assert.That(typeof(AwdFlagInjectionHandler).GetCustomAttributes(typeof(NonTransactionalAttribute), inherit: true)).HasSingleItem();
    }

    [Test, Arguments(0), Arguments(17), Timeout(300_000)]
    public async Task Named_service_receives_flag_as_data_and_duplicate_delivery_does_not_repeat_partial_writes(int exitCode, CancellationToken ct)
    {
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct);
        var db = fixture.Db; var ownerId = fixture.Application.OwnerId; var now = DateTimeOffset.UtcNow;
        var competitionId = Guid.NewGuid(); var challengeId = Guid.NewGuid(); var teamId = Guid.NewGuid();
        db.Competitions.Add(new AwdCompetition { Id = competitionId, OwnerId = ownerId, Title = "Named injection",
            StartAt = now.AddMinutes(-1), EndAt = now.AddHours(1), Status = CompetitionStatus.Running,
            ModeConfiguration = CompetitionModeConfigurationDefaults.Create(GameMode.Awd, competitionId) });
        db.CompetitionChallenges.Add(new AwdCompetitionChallenge { Id = challengeId, CompetitionId = competitionId,
            ChallengeId = fixture.Application.TemplateId, Rules = new AwdCompetitionChallengeRules(), IsPublished = true });
        db.Teams.Add(new Team { Id = teamId, CompetitionId = competitionId, CaptainId = ownerId, MemberIds = [ownerId],
            Name = "injection", InvitationToken = new string('i', 32), RegistrationStatus = TeamRegistrationStatus.Approved });
        await db.SaveChangesAsync(ct);
        var provision = await fixture.QueueAsync(2, ct, new PlayerRuntimeInstance { Id = Guid.NewGuid(), CompetitionId = competitionId,
            CompetitionChallengeId = challengeId, TeamId = teamId, State = RuntimeState.Queued, RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker, CreatedAt = now });
        var created = await fixture.Handler.ProvisionContainerAsync(provision, ct) as NoCTF.Application.Runtime.Instances.RuntimeProvisioned
            ?? throw new InvalidOperationException("The injection target did not start.");
        await fixture.PersistAsync(created, ct);
        var receipt = (ContainerRuntimeReceiptData)created.ProviderReceipt;
        var endsAt = DateTimeOffset.UtcNow.AddMinutes(2);
        endsAt = endsAt.AddTicks(-(endsAt.Ticks % TimeSpan.TicksPerMicrosecond));
        var round = new AwdRound { Id = Guid.NewGuid(), CompetitionId = competitionId, Number = 1, StartsAt = now,
            EndsAt = endsAt, ActiveEndTicks = TimeSpan.FromMinutes(2).Ticks };
        var storeId = Guid.NewGuid();
        var rules = new AwdRoundRules { Id = Guid.NewGuid(), CompetitionId = competitionId, CompetitionChallengeId = challengeId,
            RoundId = round.Id, EffectiveAt = now, CheckerIntervalSeconds = 60,
            Stores = [new() { StoreId = storeId, Name = "private", TargetServiceName = "service-1", DeliveryMode = AwdFlagDeliveryMode.RunnerCommand,
                InjectionCommand = $"printf '%s' \"$FLAG\" > /delivered-flag; printf delivery >> /delivery-count; exit {exitCode}", InjectionTimeoutSeconds = 10 }] };
        var flagId = Guid.NewGuid(); var flagValue = "flag{a'b;$(touch /shell-injected)}";
        var key = new AwdFlagKey { Id = Guid.NewGuid(), CompetitionId = competitionId, Version = 1, CreatedAt = now };
        var secrets = new PlatformSecretProtector(Options.Create(new EmailVerificationProtectionOptions { EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));
        key.ProtectedSecret = secrets.Protect(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), PlatformSecretPurpose.AwdFlagKey, competitionId, key.Id);
        db.AwdFlagKeys.Add(key);
        db.AwdRounds.Add(round); db.AwdRoundRules.Add(rules);
        db.ChallengeFlags.Add(new AwdRoundChallengeFlag { Id = flagId, CompetitionChallengeId = challengeId, TeamId = teamId,
            Flag = flagValue, FlagSha256 = ManageChallengeFlags.Hash(flagValue), SpecificationKind = SpecificationKind.AwdRound,
            SpecificationId = AwdRoundSpecificationId.FromRound(1).Value, ValidStart = now, ValidUntil = endsAt,
            AwdIssue = new() { FlagId = flagId, CompetitionId = competitionId, CompetitionChallengeId = challengeId, TeamId = teamId,
                RoundId = round.Id, RoundNumber = 1, RulesId = rules.Id, StoreId = storeId, State = AwdFlagIssueState.Issued,
                Digest = Convert.ToHexString(ManageChallengeFlags.Hash(flagValue)),
                DeliveryState = AwdFlagDeliveryState.Pending, DeliveryRuntimeId = provision.RuntimeInstanceId, IssuedAt = now, UpdatedAt = now } });
        await db.SaveChangesAsync(ct);
        using var mutations = new RunnerResourceMutationCoordinator();
        var handler = new AwdFlagInjectionHandler(new AwdFlagInjectionWorkReader(fixture.Contexts, TimeProvider.System),
            new AwdFlagInjectionExecutor(fixture.Providers), fixture.Application.Publisher, fixture.RunnerOptions, TimeProvider.System,
            mutations, fixture.Capacity);
        var message = new InjectAwdFlag(provision.RuntimeInstanceId, challengeId, flagId, endsAt, RuntimeExecutionFixture.RunnerId);
        await Assert.That(await handler.Handle(message, ct)).IsEqualTo(MessageExecutionOutcome.Applied);
        await Assert.That(await handler.Handle(message, ct)).IsEqualTo(MessageExecutionOutcome.Superseded);
        db.ChangeTracker.Clear();
        var issuance = await db.AwdFlagIssues.SingleAsync(value => value.FlagId == flagId, ct);
        await Assert.That(issuance.DeliveryState).IsEqualTo(exitCode == 0 ? AwdFlagDeliveryState.Succeeded : AwdFlagDeliveryState.Unconfirmed);
        await Assert.That(issuance.DeliveryAttempts).IsEqualTo(1);
        await Assert.That(issuance.Obligation).IsEqualTo(exitCode == 0 ? AwdFlagObligation.PlayerRequired : AwdFlagObligation.PlatformPending);
        var read = await fixture.Providers.Runtime(RuntimeProvider.Docker).ExecAsync(receipt.ToReceipt(), "service-1",
            ["/bin/sh", "-c", "test \"$(cat /delivered-flag)\" = \"$1\" && test \"$(cat /delivery-count)\" = delivery && test ! -e /shell-injected", "test-delivery", flagValue],
            TimeSpan.FromSeconds(5), ct);
        await Assert.That(read.ExitCode).IsEqualTo(0);
        var other = await fixture.Providers.Runtime(RuntimeProvider.Docker).ExecAsync(receipt.ToReceipt(), "service-0",
            ["/bin/sh", "-c", "test ! -e /delivered-flag"], TimeSpan.FromSeconds(5), ct);
        await Assert.That(other.ExitCode).IsEqualTo(0);
    }
}
