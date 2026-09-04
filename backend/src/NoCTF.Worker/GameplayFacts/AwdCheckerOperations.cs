using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Platform;
using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Domain.Notifications;
using System.Text.Json;
using System.Buffers.Binary;
using System.Security.Cryptography;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Awdp.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.Worker.Runtime;
using CompetitionLifecycleAdvancer = NoCTF.Application.Competitions.Lifecycle.AdvanceCompetitionLifecycleUseCase;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Worker;

internal static partial class BackendMessageOperations
{
    public static async Task DispatchAwdCheckersAsync(
        DispatchAwdCheckers message,
        NoCtfDbContext db,
        AwdCheckerConfigurationCatalog configurations,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken) =>
        _ = await ExecuteAwdCheckerDispatchAsync(
            message,
            db,
            configurations,
            outbox,
            cancellationToken);

    public static async Task<MessageExecutionOutcome> ExecuteAwdCheckerDispatchAsync(
        DispatchAwdCheckers message,
        NoCtfDbContext db,
        AwdCheckerConfigurationCatalog configurations,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        const int batchSize = 500;
        var targets = await db.RuntimeInstances
            .Where(runtime => runtime.State == RuntimeState.Running
                && runtime.RunnerId != null
                && runtime.ProviderReceiptJson != null
                && (message.AfterRuntimeInstanceId == null
                    || runtime.Id.CompareTo(message.AfterRuntimeInstanceId.Value) > 0))
            .Join(
                db.CompetitionChallenges,
                runtime => runtime.CompetitionChallengeId,
                challenge => challenge.Id,
                (runtime, challenge) => new { Runtime = runtime, Challenge = challenge })
            .Join(
                db.Competitions,
                pair => pair.Runtime.CompetitionId,
                competition => competition.Id,
                (pair, competition) => new
                {
                    pair.Runtime,
                    pair.Challenge,
                    Competition = competition
                })
            .Join(
                db.Challenges,
                target => target.Challenge.ChallengeId,
                challenge => challenge.Id,
                (target, challenge) => new
                {
                    target.Runtime,
                    target.Challenge,
                    target.Competition,
                    Template = challenge
                })
            .Where(target => target.Competition.Mode == NoCTF.Domain.Competitions.GameMode.Awd
                && target.Competition.Status == NoCTF.Domain.Competitions.CompetitionStatus.Running)
            .OrderBy(target => target.Runtime.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var applied = false;
        foreach (var target in targets)
        {
            var settings = configurations.Get(
                target.Competition.ConfigurationJson,
                target.Challenge.RulesJson,
                target.Template.DefinitionJson);
            if (settings.Checker is not { } checker)
                continue;

            var lastCheckAt = await db.GameplayFacts.AsNoTracking()
                .Where(fact => fact.CompetitionId == target.Runtime.CompetitionId
                    && fact.CompetitionChallengeId == target.Runtime.CompetitionChallengeId
                    && fact.TeamId == target.Runtime.TeamId
                    && fact.Kind == GameplayFactKind.AwdServiceTransition)
                .MaxAsync(fact => (DateTimeOffset?)fact.OccurredAt, cancellationToken);
            if (lastCheckAt is { } previous
                && previous.AddSeconds(settings.CheckerIntervalSeconds) > message.At)
                continue;

            var factId = CreateAwdCheckerFactId(target.Runtime.Id, message.At);
            var factExists = await db.GameplayFacts.AsNoTracking()
                .AnyAsync(fact => fact.Id == factId, cancellationToken);
            if (factExists)
                continue;

            var deadline = message.At.AddSeconds(checker.TimeoutSeconds);
            db.GameplayFacts.Add(new GameplayFact
            {
                Id = factId,
                CompetitionId = target.Runtime.CompetitionId!.Value,
                CompetitionChallengeId = target.Runtime.CompetitionChallengeId!.Value,
                TeamId = target.Runtime.TeamId,
                Kind = GameplayFactKind.AwdServiceTransition,
                OccurredAt = message.At,
                State = GameplayFactState.Processing,
                UpdatedAt = message.At
            });
            await outbox.PublishToRunnerNodeAsync(new RunAwdChecker(
                target.Runtime.Id,
                target.Runtime.CompetitionChallengeId!.Value,
                factId,
                target.Runtime.RunnerId!,
                deadline));
            await outbox.ScheduleAsync(new AwdCheckerCallbackMissing(
                target.Runtime.CompetitionId!.Value,
                target.Runtime.CompetitionChallengeId!.Value,
                target.Runtime.Id,
                factId,
                deadline), deadline);
            applied = true;
        }

        var pageIsFull = targets.Count == batchSize;
        if (pageIsFull)
            await outbox.PublishAsync(new DispatchAwdCheckers(message.At, targets[^1].Runtime.Id));
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return applied ? MessageExecutionOutcome.Applied : MessageExecutionOutcome.Idempotent;
    }

    private static Guid CreateAwdCheckerFactId(Guid runtimeInstanceId, DateTimeOffset occurredAt)
    {
        Span<byte> input = stackalloc byte[24];
        runtimeInstanceId.TryWriteBytes(input[..16]);
        BinaryPrimitives.WriteInt64BigEndian(input[16..], occurredAt.UtcTicks);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }

}
