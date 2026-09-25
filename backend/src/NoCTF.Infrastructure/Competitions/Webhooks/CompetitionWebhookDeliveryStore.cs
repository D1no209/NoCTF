using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Webhooks;

public sealed record CompetitionWebhookOptions(
    Uri PublicBaseUrl,
    int TimeoutSeconds,
    IReadOnlySet<string> PrivateNetworkAllowList,
    IReadOnlySet<string> InsecureHttpHostAllowList);

public sealed class CompetitionWebhookDeliveryStore(
    NoCtfDbContext db,
    PlatformSecretProtector secrets,
    GetChallenge getChallenge,
    GetCompetitionTracks getTracks,
    ILeaderboardCache leaderboard,
    CompetitionWebhookOptions options,
    TimeProvider timeProvider) : ICompetitionWebhookDeliveryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<CompetitionWebhookDispatchBatch> PrepareBatchAsync(
        DispatchCompetitionWebhooks command,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var source = await db.CompetitionEvents.AsNoTracking()
            .Where(item => item.Id == command.EventId
                && item.CompetitionId == command.CompetitionId)
            .Select(item => new { item.Visibility, item.Kind, item.OccurredAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (source is null
            || source.Visibility != CompetitionEventVisibility.Public
            || CompetitionWebhookEventTypes.From(source.Kind) is null)
            return new([]);

        var configuration = await db.Competitions.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.Id == command.CompetitionId && item.DeletedAt == null)
            .Select(item => item.WebhookConfiguration)
            .SingleOrDefaultAsync(cancellationToken);
        if (configuration is null)
            return new([]);

        var page = configuration.Targets
            .Where(item => command.AfterTargetId is null
                || item.Id.CompareTo(command.AfterTargetId.Value) > 0)
            .OrderBy(item => item.Id)
            .Take(batchSize + 1)
            .ToArray();
        var deliveries = page.Take(batchSize)
            .Where(item => item.Enabled
                && item.EnabledAt is DateTimeOffset enabledAt
                && source.OccurredAt >= enabledAt)
            .Select(item => new DeliverCompetitionWebhook(
                command.CompetitionId,
                command.EventId,
                item.Id,
                source.OccurredAt))
            .ToArray();
        return new(
            deliveries,
            page.Length > batchSize ? page[batchSize - 1].Id : null);
    }

    public async Task<CompetitionWebhookDelivery> PrepareDeliveryAsync(
        DeliverCompetitionWebhook command,
        CancellationToken cancellationToken)
    {
        var competition = await db.Competitions.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == command.CompetitionId, cancellationToken);
        if (competition is null || competition.DeletedAt is not null)
            return Missing(command);
        var target = competition.WebhookConfiguration.Targets
            .SingleOrDefault(item => item.Id == command.TargetId);
        if (target is null)
            return Missing(command);
        if (!target.Enabled
            || target.EnabledAt is not DateTimeOffset enabledAt
            || command.OccurredAt < enabledAt)
            return Suppressed(command);

        var competitionEvent = await db.CompetitionEvents.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == command.EventId
                && item.CompetitionId == command.CompetitionId, cancellationToken);
        if (competitionEvent is null)
            return Missing(command);
        var eventType = CompetitionWebhookEventTypes.From(competitionEvent.Kind);
        if (competitionEvent.Visibility != CompetitionEventVisibility.Public
            || eventType is null)
            return Suppressed(command);

        var body = await BuildBodyAsync(
            competition,
            competitionEvent,
            eventType,
            cancellationToken);
        var previousSecret = target.PreviousSecretCiphertext is { Length: > 0 }
            && target.PreviousSecretValidUntil > timeProvider.GetUtcNow()
                ? secrets.Unprotect(
                    target.PreviousSecretCiphertext,
                    PlatformSecretPurpose.CompetitionWebhookSecret,
                    competition.Id,
                    target.Id)
                : null;
        return new(
            CompetitionWebhookDeliveryReadState.Ready,
            command.CompetitionId,
            command.EventId,
            command.TargetId,
            new Uri(target.EndpointUrl),
            body,
            secrets.Unprotect(
                target.CurrentSecretCiphertext,
                PlatformSecretPurpose.CompetitionWebhookSecret,
                competition.Id,
                target.Id),
            previousSecret);
    }

    public async Task<CompetitionWebhookDelivery> PrepareTestDeliveryAsync(
        TestCompetitionWebhook command,
        CancellationToken cancellationToken)
    {
        var competition = await db.Competitions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == command.CompetitionId, cancellationToken);
        var target = competition?.WebhookConfiguration.Targets
            .SingleOrDefault(item => item.Id == command.TargetId);
        if (competition is null || target is null)
        {
            return new(
                CompetitionWebhookDeliveryReadState.Missing,
                command.CompetitionId,
                command.DeliveryId,
                command.TargetId);
        }
        var now = timeProvider.GetUtcNow();
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            specversion = "1.0",
            id = command.DeliveryId,
            source = new Uri(options.PublicBaseUrl,
                $"api/v1/competitions/{competition.Id}").AbsoluteUri,
            type = "com.noctf.webhook.test.v1",
            subject = $"competitions/{competition.Id}",
            time = command.RequestedAt,
            datacontenttype = "application/json",
            dataschema = new Uri(options.PublicBaseUrl,
                "schemas/webhooks/competition-events-v1.schema.json").AbsoluteUri,
            data = new
            {
                capturedAt = now,
                @event = new { competitionId = competition.Id },
                resources = new { competition = BuildCompetitionResource(competition) }
            }
        }, JsonOptions);
        var previousSecret = target.PreviousSecretCiphertext is { Length: > 0 }
            && target.PreviousSecretValidUntil > now
                ? secrets.Unprotect(
                    target.PreviousSecretCiphertext,
                    PlatformSecretPurpose.CompetitionWebhookSecret,
                    competition.Id,
                    target.Id)
                : null;
        return new(
            CompetitionWebhookDeliveryReadState.Ready,
            competition.Id,
            command.DeliveryId,
            target.Id,
            new Uri(target.EndpointUrl),
            body,
            secrets.Unprotect(
                target.CurrentSecretCiphertext,
                PlatformSecretPurpose.CompetitionWebhookSecret,
                competition.Id,
                target.Id),
            previousSecret);
    }

    public async Task DisableGoneAsync(
        Guid competitionId,
        Guid targetId,
        Uri expectedEndpoint,
        DateTimeOffset disabledAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var competition = await db.Competitions.SingleOrDefaultAsync(
            item => item.Id == competitionId,
            cancellationToken);
        var target = competition?.WebhookConfiguration.Targets
            .SingleOrDefault(item => item.Id == targetId);
        if (target is null
            || !target.Enabled
            || !string.Equals(target.EndpointUrl, expectedEndpoint.AbsoluteUri, StringComparison.Ordinal))
            return;
        target.Enabled = false;
        target.EnabledAt = null;
        target.DisabledReason = CompetitionWebhookDisabledReason.ReceiverGone;
        target.UpdatedAt = disabledAt;
        competition!.UpdatedAt = disabledAt;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<byte[]> BuildBodyAsync(
        Competition competition,
        CompetitionEvent competitionEvent,
        string eventType,
        CancellationToken cancellationToken)
    {
        var challengeId = competitionEvent.CompetitionChallengeId;
        var competitionResource = BuildCompetitionResource(competition);
        JsonElement? challengeResource = challengeId is Guid id
            ? await BuildChallengeResourceAsync(competition, id, cancellationToken)
            : null;
        JsonElement? announcementResource = competitionEvent.Kind == CompetitionEventKind.AnnouncementPublished
            ? await BuildAnnouncementResourceAsync(competitionEvent.QuestionId, cancellationToken)
            : null;
        JsonElement? leaderboardResource = CompetitionWebhookEventTypes.RequiresLeaderboard(
                competitionEvent.Kind)
            ? await BuildLeaderboardResourceAsync(competition, cancellationToken)
            : null;
        var capturedAt = timeProvider.GetUtcNow();
        var source = new Uri(
            options.PublicBaseUrl,
            $"api/v1/competitions/{competition.Id}").AbsoluteUri;
        var schema = new Uri(
            options.PublicBaseUrl,
            "schemas/webhooks/competition-events-v1.schema.json").AbsoluteUri;
        var payload = new
        {
            specversion = "1.0",
            id = competitionEvent.Id,
            source,
            type = eventType,
            subject = $"competitions/{competition.Id}",
            time = competitionEvent.OccurredAt,
            datacontenttype = "application/json",
            dataschema = schema,
            data = new
            {
                capturedAt,
                @event = new
                {
                    competitionId = competition.Id,
                    competitionChallengeId = challengeId,
                    hintId = competitionEvent.HintId,
                    notificationId = competitionEvent.QuestionId,
                    teamId = competitionEvent.TeamId,
                    gameplayFactId = competitionEvent.GameplayFactId,
                    from = competitionEvent.PreviousCompetitionStatus,
                    to = competitionEvent.CompetitionStatus,
                    award = CompetitionWebhookEventTypes.Award(competitionEvent.Kind),
                    outcome = competitionEvent.GameplayFactResult
                },
                resources = new
                {
                    competition = competitionResource,
                    challenge = challengeResource,
                    announcement = announcementResource,
                    leaderboard = leaderboardResource
                }
            }
        };
        return JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
    }

    private JsonElement BuildCompetitionResource(Competition value)
    {
        var now = timeProvider.GetUtcNow();
        var visibility = CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
            value.FrozenStartAt,
            value.HiddenStartAt,
            now);
        var posterUrl = value.PosterFileId is Guid fileId
            ? $"/api/v1/competitions/{value.Id}/poster?revision={fileId:N}"
            : null;
        return JsonSerializer.SerializeToElement(new
        {
            value.Id,
            value.Title,
            value.Description,
            PosterUrl = posterUrl,
            value.Mode,
            StartTime = value.StartAt,
            EndTime = value.EndAt,
            value.Status,
            value.TeamRegistrationAutoApprove,
            value.AllowTeamRegistrationWhileRunning,
            value.MaxTeamMembers,
            value.MaxConcurrentRuntimeInstancesPerTeam,
            value.OwnerId,
            LeaderboardVisibility = visibility,
            value.DeletedAt,
            AdministrationRole = (string?)null,
            value.MaxActiveQuestionsPerTeam,
            value.MaxParticipantMessagesBeforeHandlerReply,
            value.AllowChallengeOwnersToHandleQuestions,
            value.PracticeModeEnabled,
            value.TracksEnabled,
            value.AccessMode,
            value.WriteUpSubmissionRequired,
            value.WriteUpSubmissionDeadlineHours,
            WriteUpSubmissionDeadlineAt = CompetitionWriteUpPolicy.DeadlineAt(
                value.EndAt,
                value.WriteUpSubmissionDeadlineHours)
        }, JsonOptions);
    }

    private async Task<JsonElement?> BuildChallengeResourceAsync(
        Competition competition,
        Guid competitionChallengeId,
        CancellationToken cancellationToken)
    {
        var view = await getChallenge.ExecuteAsync(
            competition.Id,
            competitionChallengeId,
            includeUnpublished: false,
            includeDeleted: false,
            cancellationToken);
        if (view is null)
            return null;
        var visibility = CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
            competition.FrozenStartAt,
            competition.HiddenStartAt,
            timeProvider.GetUtcNow());
        var dataScope = visibility switch
        {
            CompetitionLeaderboardVisibility.Blackout => LeaderboardDataScope.Hidden,
            CompetitionLeaderboardVisibility.Frozen => LeaderboardDataScope.Frozen,
            _ => LeaderboardDataScope.Live
        };
        var hints = await db.CompetitionChallenges.AsNoTracking()
            .Where(item => item.Id == competitionChallengeId)
            .SelectMany(item => item.Hints)
            .Where(item => item.PublishedAt != null && item.HiddenAt == null)
            .OrderBy(item => item.PublishedAt)
            .Select(item => new
            {
                item.Id,
                item.Cost,
                PublishedAt = item.PublishedAt!.Value,
                Content = (string?)null,
                IsUnlocked = false,
                CanUnlock = false
            })
            .ToArrayAsync(cancellationToken);
        return JsonSerializer.SerializeToElement(new
        {
            view.Id,
            view.CompetitionId,
            view.ChallengeId,
            view.Title,
            view.CustomTitle,
            view.Description,
            view.Direction,
            view.Order,
            view.IsPublished,
            view.DeletedAt,
            view.HasRuntime,
            view.CreatedAt,
            view.UpdatedAt,
            ControlFlag = (string?)null,
            Urls = (IReadOnlyList<string>?)null,
            LeaderboardVisibility = visibility,
            DataScope = dataScope,
            MaximumFlagAttempts = (int?)null,
            AcceptedFlagAttempts = (int?)null,
            RemainingFlagAttempts = (int?)null,
            SolvedByMyTeam = false,
            view.UsesDynamicFlag,
            Hints = hints,
            view.InteractionKind,
            PatchVerificationAvailable = false,
            MaximumPatchAttempts = (int?)null,
            AcceptedPatchAttempts = (int?)null,
            RemainingPatchAttempts = (int?)null,
            PatchVerificationState = (string?)null,
            PatchVerificationResult = (string?)null,
            PatchVerificationFailureCode = (string?)null,
            PatchVerificationRuntimeInstanceId = (Guid?)null,
            PatchVerificationRuntimeState = (string?)null
        }, JsonOptions);
    }

    private async Task<JsonElement?> BuildAnnouncementResourceAsync(
        Guid? notificationId,
        CancellationToken cancellationToken)
    {
        if (notificationId is not Guid id)
            return null;
        var row = await db.Notifications.AsNoTracking()
            .Where(item => item.Id == id
                && item.Kind == NotificationKind.CompetitionAnnouncement
                && item.TargetType == NotificationTargetType.CompetitionParticipants)
            .Select(item => new { item.Id, item.Title, item.Body, item.SentAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
            return null;
        return JsonSerializer.SerializeToElement(new
        {
            row.Id,
            row.Title,
            row.Body,
            PublishedAt = row.SentAt
        }, JsonOptions);
    }

    private async Task<JsonElement> BuildLeaderboardResourceAsync(
        Competition competition,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var visibility = CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
            competition.FrozenStartAt,
            competition.HiddenStartAt,
            now);
        var dataScope = visibility switch
        {
            CompetitionLeaderboardVisibility.Blackout => LeaderboardDataScope.Hidden,
            CompetitionLeaderboardVisibility.Frozen => LeaderboardDataScope.Frozen,
            _ => LeaderboardDataScope.Live
        };
        ScoreboardSnapshot snapshot;
        if (dataScope == LeaderboardDataScope.Hidden)
        {
            snapshot = new(competition.Id, 0, 0, now, null, [], []);
        }
        else
        {
            var projection = dataScope == LeaderboardDataScope.Frozen
                ? await leaderboard.GetFrozenScoreboardAsync(competition.Id, cancellationToken)
                : await leaderboard.GetScoreboardAsync(competition.Id, cancellationToken);
            if (projection is not null)
            {
                projection = ScoreboardAudienceProjection.Filter(
                    projection,
                    canObserve: false);
                var tracks = await getTracks.ExecuteAsync(
                    competition.Id,
                    viewerUserId: null,
                    includeInternal: false,
                    includeInvitationCodes: false,
                    cancellationToken);
                if (tracks is not null)
                {
                    projection = ScoreboardAudienceProjection.FilterTracks(
                        projection,
                        tracks,
                        canViewInternalTracks: false);
                }
            }
            snapshot = projection?.Snapshot
                ?? new ScoreboardSnapshot(competition.Id, 0, 0, now, null, [], []);
            snapshot = snapshot with
            {
                Visibility = visibility,
                DataScope = dataScope,
                DataAsOf = dataScope == LeaderboardDataScope.Frozen
                    ? snapshot.DataAsOf
                    : snapshot.GeneratedAt
            };
        }
        return SerializeScoreboard(snapshot with
        {
            Visibility = visibility,
            DataScope = dataScope
        });
    }

    private static JsonElement SerializeScoreboard(ScoreboardSnapshot snapshot) =>
        JsonSerializer.SerializeToElement(new
        {
            snapshot.CompetitionId,
            Version = snapshot.Version.ToString(CultureInfo.InvariantCulture),
            SchemaRevision = snapshot.SchemaRevision.ToString(CultureInfo.InvariantCulture),
            snapshot.GeneratedAt,
            snapshot.CurrentRoundId,
            snapshot.Actors,
            snapshot.Teams,
            snapshot.Tracks,
            snapshot.TracksEnabled,
            snapshot.CurrentChallengeScores,
            snapshot.Visibility,
            snapshot.DataScope,
            snapshot.DataAsOf
        }, JsonOptions);

    private static CompetitionWebhookDelivery Missing(DeliverCompetitionWebhook command) => new(
        CompetitionWebhookDeliveryReadState.Missing,
        command.CompetitionId,
        command.EventId,
        command.TargetId);

    private static CompetitionWebhookDelivery Suppressed(DeliverCompetitionWebhook command) => new(
        CompetitionWebhookDeliveryReadState.Suppressed,
        command.CompetitionId,
        command.EventId,
        command.TargetId);
}

public static class CompetitionWebhookEventTypes
{
    public static string? From(CompetitionEventKind kind) => kind switch
    {
        CompetitionEventKind.CompetitionLifecycleChanged =>
            "com.noctf.competition.lifecycle.changed.v1",
        CompetitionEventKind.ChallengePublished =>
            "com.noctf.competition.challenge.published.v1",
        CompetitionEventKind.ChallengeDescriptionUpdated =>
            "com.noctf.competition.challenge.updated.v1",
        CompetitionEventKind.HintPublished =>
            "com.noctf.competition.hint.published.v1",
        CompetitionEventKind.AnnouncementPublished =>
            "com.noctf.competition.announcement.published.v1",
        CompetitionEventKind.TeamBanned =>
            "com.noctf.competition.team.banned.v1",
        CompetitionEventKind.TeamBanCorrectionPublished =>
            "com.noctf.competition.team.ban.corrected.v1",
        CompetitionEventKind.FirstBloodAwarded
            or CompetitionEventKind.SecondBloodAwarded
            or CompetitionEventKind.ThirdBloodAwarded =>
            "com.noctf.competition.blood.awarded.v1",
        CompetitionEventKind.AwdpBreakResolved =>
            "com.noctf.competition.awdp.break.resolved.v1",
        CompetitionEventKind.AwdpFixResolved =>
            "com.noctf.competition.awdp.fix.resolved.v1",
        _ => null
    };

    public static bool RequiresLeaderboard(CompetitionEventKind kind) => kind is
        CompetitionEventKind.TeamBanned
        or CompetitionEventKind.TeamBanCorrectionPublished
        or CompetitionEventKind.FirstBloodAwarded
        or CompetitionEventKind.SecondBloodAwarded
        or CompetitionEventKind.ThirdBloodAwarded
        or CompetitionEventKind.AwdpBreakResolved
        or CompetitionEventKind.AwdpFixResolved;

    public static string? Award(CompetitionEventKind kind) => kind switch
    {
        CompetitionEventKind.FirstBloodAwarded => "First",
        CompetitionEventKind.SecondBloodAwarded => "Second",
        CompetitionEventKind.ThirdBloodAwarded => "Third",
        _ => null
    };
}
