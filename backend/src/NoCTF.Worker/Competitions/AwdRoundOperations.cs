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
    public static async Task AdvanceAwdRoundAsync(
        AdvanceAwdRound message,
        IAwdRoundCoordinator coordinator,
        CancellationToken cancellationToken) =>
        _ = await coordinator.AdvanceAsync(message, cancellationToken);

    public static async Task GenerateAwdFlagsAsync(
        GenerateAwdFlags message,
        IAwdRoundCoordinator coordinator,
        CancellationToken cancellationToken) =>
        _ = await coordinator.GenerateFlagsAsync(message, cancellationToken);

    public static async Task AwdFlagInjectionFailedAsync(
        AwdFlagInjectionFailed message,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(candidate => candidate.Id == message.CompetitionId)
            .Select(candidate => new { candidate.OwnerId, candidate.ManagerIds })
            .SingleOrDefaultAsync(cancellationToken);
        if (competition is null)
            return;
        var existing = await db.Notifications.AsNoTracking()
            .AnyAsync(notification =>
                notification.TargetType == NotificationTargetType.CompetitionCollaborators
                && notification.TargetId == message.CompetitionId
                && notification.RelatedType == EntityReferenceKind.CompetitionChallenge
                && notification.RelatedId == message.CompetitionChallengeId
                && notification.Kind == NotificationKind.RuntimeStateChanged)
            ;
        if (existing)
            return;
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            code = "awd_flag_injection_failed",
            message.CompetitionChallengeId,
            message.ChallengeFlagId
        });
        db.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(message.OccurredAt),
            SourceType = NotificationSourceType.System,
            TargetType = NotificationTargetType.CompetitionCollaborators,
            TargetId = message.CompetitionId,
            Kind = NotificationKind.RuntimeStateChanged,
            ContentJson = payload,
            RelatedType = EntityReferenceKind.CompetitionChallenge,
            RelatedId = message.CompetitionChallengeId,
            SentAt = message.OccurredAt
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task AwdCheckerCallbackMissingAsync(
        AwdCheckerCallbackMissing message,
        IInternalResultStore results,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        var disposition = await results.RecordAwdAsync(AwdCheckResult.Create(
            message.RuntimeInstanceId,
            message.GameplayFactId,
            AwdServiceState.CheckerTimedOut,
            message.OccurredAt), cancellationToken);
        if (disposition != InternalResultDisposition.Applied)
            return;

        var competition = await db.Competitions.AsNoTracking()
            .Where(candidate => candidate.Id == message.CompetitionId)
            .Select(candidate => new { candidate.OwnerId, candidate.ManagerIds })
            .SingleOrDefaultAsync(cancellationToken);
        if (competition is null)
            return;
        var existing = await db.Notifications.AsNoTracking()
            .AnyAsync(notification =>
                notification.TargetType == NotificationTargetType.CompetitionCollaborators
                && notification.TargetId == message.CompetitionId
                && notification.RelatedType == EntityReferenceKind.GameplayFact
                && notification.RelatedId == message.GameplayFactId
                && notification.Kind == NotificationKind.ManagementFailure)
            ;
        if (existing)
            return;
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            code = "awd_checker_callback_missing",
            message.CompetitionChallengeId,
            message.RuntimeInstanceId,
            message.GameplayFactId
        });
        db.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(message.OccurredAt),
            SourceType = NotificationSourceType.System,
            TargetType = NotificationTargetType.CompetitionCollaborators,
            TargetId = message.CompetitionId,
            Kind = NotificationKind.ManagementFailure,
            ContentJson = payload,
            RelatedType = EntityReferenceKind.GameplayFact,
            RelatedId = message.GameplayFactId,
            SentAt = message.OccurredAt
        });
        await db.SaveChangesAsync(cancellationToken);
    }

}
