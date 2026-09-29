using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Application.Administration;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Domain.Shared;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Exports;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Infrastructure.Authentication;

namespace NoCTF.Infrastructure.Administration;

public sealed class PlatformAuditLogStore(NoCtfDbContext db) : IPlatformAuditLogStore
{
    private static readonly CompetitionEventKind[] AuditedCompetitionEventKinds =
    [
        CompetitionEventKind.CompetitionCreated,
        CompetitionEventKind.CompetitionUpdated,
        CompetitionEventKind.CompetitionDeleted,
        CompetitionEventKind.CompetitionLifecycleChanged,
        CompetitionEventKind.LeaderboardVisibilityChanged,
        CompetitionEventKind.ChallengeCreated,
        CompetitionEventKind.ChallengeUpdated,
        CompetitionEventKind.ChallengePublished,
        CompetitionEventKind.ChallengeUnpublished,
        CompetitionEventKind.ChallengeDeleted,
        CompetitionEventKind.HintPublished,
        CompetitionEventKind.TeamRegistrationChanged,
        CompetitionEventKind.TeamDeleted,
        CompetitionEventKind.TeamBanned,
        CompetitionEventKind.TeamUnbanned,
        CompetitionEventKind.ProtectedGameplayFactValueAccessed,
        CompetitionEventKind.GameplayFactPatchDownloaded,
        CompetitionEventKind.CheatIncidentConfirmed,
        CompetitionEventKind.CheatIncidentDismissed,
        CompetitionEventKind.CheatIncidentSuperseded,
        CompetitionEventKind.CheatIncidentCorrected,
        CompetitionEventKind.CompetitionArchiveExported,
        CompetitionEventKind.TeamBanAppealUpheld,
        CompetitionEventKind.TeamBanAppealAccepted,
        CompetitionEventKind.TeamBanCorrectionPublished,
        CompetitionEventKind.RuntimeForceTerminationRequested,
        CompetitionEventKind.RuntimeForceTerminationCompleted,
        CompetitionEventKind.RuntimeForceTerminationFailed,
        CompetitionEventKind.AnnouncementPublished,
        CompetitionEventKind.ChallengeDescriptionUpdated,
        CompetitionEventKind.TrackConfigurationUpdated,
        CompetitionEventKind.TeamTrackChanged,
        CompetitionEventKind.TrackRegistrationPolicyUpdated
    ];

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<PlatformAuditView>> QueryAsync(
        PlatformAuditQuery query,
        CancellationToken ct)
    {
        var items = new List<PlatformAuditView>(query.Limit * 2);
        if (query.Kind is null
            or PlatformAuditKind.CompetitionLifecycle
            or PlatformAuditKind.CompetitionLeaderboardVisibility
            or PlatformAuditKind.CompetitionEvent)
        {
            var competitionEvents = db.CompetitionEvents.AsNoTracking()
                .Where(item => item.ActorUserId != null
                    && AuditedCompetitionEventKinds.Contains(item.Kind));
            if (query.From is not null)
                competitionEvents = competitionEvents.Where(item =>
                    item.OccurredAt >= query.From.Value);
            if (query.To is not null)
                competitionEvents = competitionEvents.Where(item =>
                    item.OccurredAt <= query.To.Value);
            if (query.CompetitionId is not null)
                competitionEvents = competitionEvents.Where(item =>
                    item.CompetitionId == query.CompetitionId.Value);
            if (query.ActorId is not null)
                competitionEvents = competitionEvents.Where(item =>
                    item.ActorUserId == query.ActorId.Value);
            if (query.BeforeOccurredAt is DateTimeOffset beforeOccurredAt
                && query.BeforeId is Guid beforeId)
            {
                competitionEvents = competitionEvents.Where(item =>
                    item.OccurredAt < beforeOccurredAt
                    || item.OccurredAt == beforeOccurredAt
                    && item.Id.CompareTo(beforeId) < 0);
            }
            competitionEvents = query.Kind switch
            {
                PlatformAuditKind.CompetitionLifecycle => competitionEvents.Where(item =>
                    item.Kind == CompetitionEventKind.CompetitionLifecycleChanged),
                PlatformAuditKind.CompetitionLeaderboardVisibility => competitionEvents.Where(item =>
                    item.Kind == CompetitionEventKind.LeaderboardVisibilityChanged),
                PlatformAuditKind.CompetitionEvent => competitionEvents.Where(item =>
                    item.Kind != CompetitionEventKind.CompetitionLifecycleChanged
                    && item.Kind != CompetitionEventKind.LeaderboardVisibilityChanged),
                _ => competitionEvents
            };

            var eventItems = await competitionEvents
                .OrderByDescending(item => item.OccurredAt)
                .ThenByDescending(item => item.Id)
                .Take(query.Limit)
                .ToArrayAsync(ct);
            var competitionIds = eventItems
                .Select(item => item.CompetitionId)
                .Distinct()
                .ToArray();
            var competitionTitles = await db.Competitions.AsNoTracking()
                .Where(item => competitionIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, item => item.Title, ct);
            items.AddRange(eventItems.Select(item => new PlatformAuditView(
                item.Id,
                MapKind(item.Kind),
                item.CompetitionId,
                item.CompetitionId,
                item.ActorUserId,
                item.PreviousCompetitionStatus,
                item.CompetitionStatus,
                item.PreviousLeaderboardVisibility,
                item.LeaderboardVisibility,
                null,
                null,
                item.Kind,
                item.Level,
                item.Visibility,
                Reference(item, EntityReferenceKind.User),
                Reference(item, EntityReferenceKind.Team) ?? (item.Kind == CompetitionEventKind.GameplayFactPatchDownloaded ? item.TeamId : null),
                Reference(item, EntityReferenceKind.CompetitionChallenge) ?? (item.Kind == CompetitionEventKind.GameplayFactPatchDownloaded ? item.CompetitionChallengeId : null),
                Reference(item, EntityReferenceKind.RuntimeInstance),
                Reference(item, EntityReferenceKind.GameplayFact),
                Reference(item, EntityReferenceKind.Notification),
                null,
                null,
                null,
                competitionTitles.GetValueOrDefault(item.CompetitionId),
                item.Reason,
                item.Automatic,
                item.OccurredAt,
                Reference(item, EntityReferenceKind.File))));
        }

        if (query.Kind is null
            or PlatformAuditKind.UserAccountLifecycle
            or PlatformAuditKind.PlatformAdministration
            or PlatformAuditKind.CompetitionAdministration)
        {
            var auditFacts = db.Notifications.AsNoTracking().Where(notification =>
                (notification.Kind == NotificationKind.UserAccountLifecycleChanged
                    || notification.Kind == NotificationKind.CompetitionForceDeleted
                    || notification.Kind == NotificationKind.PlatformAuditExported
                    || notification.Kind == NotificationKind.PlatformUserAccessTokenIssued
                    || notification.Kind == NotificationKind.PlatformUserAccessTokenRevoked
                    || notification.Kind == NotificationKind.PlatformUserTokensInvalidated
                    || notification.Kind == NotificationKind.SsoProviderConfigurationChanged
                    || notification.Kind == NotificationKind.SsoExternalIdentityBindingChanged)
                && notification.TargetType == NotificationTargetType.PlatformAdministrators);
            auditFacts = query.Kind switch
            {
                PlatformAuditKind.UserAccountLifecycle => auditFacts.Where(notification =>
                    notification.Kind == NotificationKind.UserAccountLifecycleChanged),
                PlatformAuditKind.PlatformAdministration => auditFacts.Where(notification =>
                    notification.Kind == NotificationKind.PlatformAuditExported
                    || notification.Kind == NotificationKind.PlatformUserAccessTokenIssued
                    || notification.Kind == NotificationKind.PlatformUserAccessTokenRevoked
                    || notification.Kind == NotificationKind.PlatformUserTokensInvalidated
                    || notification.Kind == NotificationKind.SsoProviderConfigurationChanged
                    || notification.Kind == NotificationKind.SsoExternalIdentityBindingChanged),
                PlatformAuditKind.CompetitionAdministration => auditFacts.Where(notification =>
                    notification.Kind == NotificationKind.CompetitionForceDeleted),
                _ => auditFacts
            };
            if (query.From is not null)
                auditFacts = auditFacts.Where(notification =>
                    notification.SentAt >= query.From.Value);
            if (query.To is not null)
                auditFacts = auditFacts.Where(notification =>
                    notification.SentAt <= query.To.Value);
            if (query.ActorId is not null)
                auditFacts = auditFacts.Where(notification =>
                    notification.SourceType == NotificationSourceType.User
                    && notification.SourceId == query.ActorId.Value);
            if (query.CompetitionId is Guid competitionId)
                auditFacts = auditFacts.Where(notification =>
                    notification.RelatedType == EntityReferenceKind.Competition
                    && notification.RelatedId == competitionId);
            if (query.BeforeOccurredAt is DateTimeOffset beforeOccurredAt
                && query.BeforeId is Guid beforeId)
            {
                auditFacts = auditFacts.Where(notification =>
                    notification.SentAt < beforeOccurredAt
                    || notification.SentAt == beforeOccurredAt
                    && notification.Id.CompareTo(beforeId) < 0);
            }

            var auditItems = await auditFacts
                .OrderByDescending(notification => notification.SentAt)
                .ThenByDescending(notification => notification.Id)
                .Take(query.Limit)
                .ToArrayAsync(ct);
            items.AddRange(auditItems.Select(ToAuditView));
        }

        return items
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.Id)
            .Take(query.Limit)
            .ToArray();
    }

    private static PlatformAuditKind MapKind(CompetitionEventKind kind) => kind switch
    {
        CompetitionEventKind.CompetitionLifecycleChanged =>
            PlatformAuditKind.CompetitionLifecycle,
        CompetitionEventKind.LeaderboardVisibilityChanged =>
            PlatformAuditKind.CompetitionLeaderboardVisibility,
        _ => PlatformAuditKind.CompetitionEvent
    };

    private static Guid? Reference(CompetitionEvent item, EntityReferenceKind kind) =>
        item.SubjectType == kind ? item.SubjectId : item.RelatedType == kind ? item.RelatedId : null;

    private static PlatformAuditView ToAuditView(Notification notification)
    {
        if (notification.Kind == NotificationKind.SsoExternalIdentityBindingChanged)
        {
            var bindingAction = (SsoBindingAuditAction)(notification.ActionValue
                ?? throw new InvalidOperationException(
                    $"Notification {notification.Id} has no SSO binding audit action."));
            return new(
                Id: notification.Id,
                Kind: PlatformAuditKind.PlatformAdministration,
                SubjectId: notification.UserId!.Value,
                CompetitionId: null,
                ActorId: notification.SourceId,
                FromCompetitionStatus: null,
                ToCompetitionStatus: null,
                FromLeaderboardVisibility: null,
                ToLeaderboardVisibility: null,
                UserAccountAction: null,
                PlatformAdministrationAction: bindingAction switch
                {
                    SsoBindingAuditAction.Bound =>
                        PlatformAdministrationAction.SsoExternalIdentityBound,
                    SsoBindingAuditAction.Unbound
                        or SsoBindingAuditAction.AdministrativelyUnbound =>
                        PlatformAdministrationAction.SsoExternalIdentityUnbound,
                    _ => throw new InvalidOperationException(
                        $"Unsupported SSO binding audit action {bindingAction}.")
                },
                CompetitionEventKind: null,
                CompetitionEventLevel: null,
                CompetitionEventVisibility: null,
                RelatedUserId: notification.UserId,
                TeamId: null,
                CompetitionChallengeId: null,
                RuntimeInstanceId: null,
                GameplayFactId: null,
                QuestionId: null,
                GameplayFactKind: null,
                GameplayFactState: null,
                GameplayFactResult: null,
                SubjectDisplayName: notification.ProviderName,
                Reason: null,
                Automatic: false,
                OccurredAt: notification.SentAt);
        }
        if (notification.Kind == NotificationKind.SsoProviderConfigurationChanged)
        {
            var ssoAction = (SsoProviderAuditAction)(notification.ActionValue
                ?? throw new InvalidOperationException(
                    $"Notification {notification.Id} has no SSO provider audit action."));
            return new(
                Id: notification.Id,
                Kind: PlatformAuditKind.PlatformAdministration,
                SubjectId: notification.SsoProviderId ?? Notification.PlatformAdministratorsTargetId,
                CompetitionId: null,
                ActorId: notification.SourceId,
                FromCompetitionStatus: null,
                ToCompetitionStatus: null,
                FromLeaderboardVisibility: null,
                ToLeaderboardVisibility: null,
                UserAccountAction: null,
                PlatformAdministrationAction: ssoAction switch
                {
                    SsoProviderAuditAction.GlobalConfigurationUpdated =>
                        PlatformAdministrationAction.SsoGlobalConfigurationUpdated,
                    SsoProviderAuditAction.ProviderCreated =>
                        PlatformAdministrationAction.SsoProviderCreated,
                    SsoProviderAuditAction.ProviderUpdated =>
                        PlatformAdministrationAction.SsoProviderUpdated,
                    SsoProviderAuditAction.ProviderSecretReplaced =>
                        PlatformAdministrationAction.SsoProviderSecretReplaced,
                    _ => throw new InvalidOperationException(
                        $"Unsupported SSO audit action {ssoAction}.")
                },
                CompetitionEventKind: null,
                CompetitionEventLevel: null,
                CompetitionEventVisibility: null,
                RelatedUserId: null,
                TeamId: null,
                CompetitionChallengeId: null,
                RuntimeInstanceId: null,
                GameplayFactId: null,
                QuestionId: null,
                GameplayFactKind: null,
                GameplayFactState: null,
                GameplayFactResult: null,
                SubjectDisplayName: notification.ProviderName ?? "Single sign-on",
                Reason: null,
                Automatic: false,
                OccurredAt: notification.SentAt);
        }
        if (notification.Kind == NotificationKind.CompetitionForceDeleted)
        {
            var competitionId = notification.CompetitionId
                ?? throw new InvalidOperationException(
                    $"Notification {notification.Id} has no deleted competition id.");
            return new(
                Id: notification.Id,
                Kind: PlatformAuditKind.CompetitionAdministration,
                SubjectId: competitionId,
                CompetitionId: competitionId,
                ActorId: notification.SourceId,
                FromCompetitionStatus: null,
                ToCompetitionStatus: null,
                FromLeaderboardVisibility: null,
                ToLeaderboardVisibility: null,
                UserAccountAction: null,
                PlatformAdministrationAction: null,
                CompetitionEventKind: null,
                CompetitionEventLevel: CompetitionEventLevel.Warning,
                CompetitionEventVisibility: CompetitionEventVisibility.Staff,
                RelatedUserId: null,
                TeamId: null,
                CompetitionChallengeId: null,
                RuntimeInstanceId: null,
                GameplayFactId: null,
                QuestionId: null,
                GameplayFactKind: null,
                GameplayFactState: null,
                GameplayFactResult: null,
                SubjectDisplayName: notification.Title,
                Reason: notification.Reason,
                Automatic: false,
                OccurredAt: notification.SentAt);
        }
        if (notification.Kind == NotificationKind.PlatformAuditExported)
        {
            return new(
                Id: notification.Id,
                Kind: PlatformAuditKind.PlatformAdministration,
                SubjectId: Notification.PlatformAdministratorsTargetId,
                CompetitionId: notification.CompetitionId,
                ActorId: notification.SourceId,
                FromCompetitionStatus: null,
                ToCompetitionStatus: null,
                FromLeaderboardVisibility: null,
                ToLeaderboardVisibility: null,
                UserAccountAction: null,
                PlatformAdministrationAction: PlatformAdministrationAction.AuditArchiveExported,
                CompetitionEventKind: null,
                CompetitionEventLevel: CompetitionEventLevel.Information,
                CompetitionEventVisibility: CompetitionEventVisibility.Staff,
                RelatedUserId: notification.ActorUserId,
                TeamId: null,
                CompetitionChallengeId: null,
                RuntimeInstanceId: null,
                GameplayFactId: null,
                QuestionId: null,
                GameplayFactKind: null,
                GameplayFactState: null,
                GameplayFactResult: null,
                SubjectDisplayName: "Platform audit",
                Reason: null,
                Automatic: false,
                OccurredAt: notification.SentAt);
        }
        if (notification.Kind is NotificationKind.PlatformUserAccessTokenIssued
            or NotificationKind.PlatformUserAccessTokenRevoked
            or NotificationKind.PlatformUserTokensInvalidated)
        {
            var tokenAction = (PlatformUserTokenAdministrationAction)(notification.ActionValue
                ?? throw new InvalidOperationException(
                    $"Notification {notification.Id} has no token audit action."));
            return new(
                Id: notification.Id,
                Kind: PlatformAuditKind.PlatformAdministration,
                SubjectId: notification.UserId!.Value,
                CompetitionId: null,
                ActorId: notification.SourceId,
                FromCompetitionStatus: null,
                ToCompetitionStatus: null,
                FromLeaderboardVisibility: null,
                ToLeaderboardVisibility: null,
                UserAccountAction: null,
                PlatformAdministrationAction: tokenAction switch
                {
                    PlatformUserTokenAdministrationAction.AccessTokenIssued =>
                        PlatformAdministrationAction.UserAccessTokenIssued,
                    PlatformUserTokenAdministrationAction.AccessTokenRevoked =>
                        PlatformAdministrationAction.UserAccessTokenRevoked,
                    PlatformUserTokenAdministrationAction.TokensInvalidated =>
                        PlatformAdministrationAction.UserTokensInvalidated,
                    _ => throw new InvalidOperationException(
                        $"Unsupported platform token action {tokenAction}.")
                },
                CompetitionEventKind: null,
                CompetitionEventLevel: null,
                CompetitionEventVisibility: null,
                RelatedUserId: notification.UserId,
                TeamId: null,
                CompetitionChallengeId: null,
                RuntimeInstanceId: null,
                GameplayFactId: null,
                QuestionId: null,
                GameplayFactKind: null,
                GameplayFactState: null,
                GameplayFactResult: null,
                SubjectDisplayName: notification.UserName,
                Reason: notification.Reason,
                Automatic: false,
                OccurredAt: notification.SentAt,
                JwtId: notification.JwtId,
                TokenExpiresAt: notification.PayloadExpiresAt,
                TokenVersion: notification.Count);
        }
        var targetUserId = notification.UserId
            ?? throw new InvalidOperationException(
                $"Notification {notification.Id} has no target user id.");
        var lifecycleAction = notification.UserLifecycleAction
            ?? throw new InvalidOperationException(
                $"Notification {notification.Id} has no lifecycle action.");
        return new(
            Id: notification.Id,
            Kind: PlatformAuditKind.UserAccountLifecycle,
            SubjectId: targetUserId,
            CompetitionId: null,
            ActorId: notification.SourceId,
            FromCompetitionStatus: null,
            ToCompetitionStatus: null,
            FromLeaderboardVisibility: null,
            ToLeaderboardVisibility: null,
            UserAccountAction: lifecycleAction,
            PlatformAdministrationAction: null,
            CompetitionEventKind: null,
            CompetitionEventLevel: null,
            CompetitionEventVisibility: null,
            RelatedUserId: targetUserId,
            TeamId: null,
            CompetitionChallengeId: null,
            RuntimeInstanceId: null,
            GameplayFactId: null,
            QuestionId: null,
            GameplayFactKind: null,
            GameplayFactState: null,
            GameplayFactResult: null,
            SubjectDisplayName: notification.UserName,
            Reason: notification.Reason,
            Automatic: notification.Automatic ?? false,
            OccurredAt: notification.SentAt);
    }

}
