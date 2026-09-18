using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.AdjudicationPreview;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.GameplayFacts.AdjudicationPreview;

public sealed class HistoricalAdjudicationPreviewStore(NoCtfDbContext db) : IHistoricalAdjudicationEvidenceStore, IHistoricalAdjudicationEventStore
{
    public const int MaximumEventsPerFact = 128;
    private static readonly CompetitionEventKind[] EvidenceKinds =
    [
        CompetitionEventKind.GameplayFactReceived, CompetitionEventKind.GameplayFactAdjudicated,
        CompetitionEventKind.ScoringRecorded, CompetitionEventKind.FirstBloodAwarded,
        CompetitionEventKind.SecondBloodAwarded, CompetitionEventKind.ThirdBloodAwarded
    ];
    private static readonly CompetitionEventKind[] EligibilityKinds =
    [
        CompetitionEventKind.TrackConfigurationUpdated, CompetitionEventKind.TeamTrackChanged,
        CompetitionEventKind.TeamBanned, CompetitionEventKind.TeamUnbanned, CompetitionEventKind.TeamDeleted,
        CompetitionEventKind.TeamRegistrationChanged, CompetitionEventKind.TeamBanAppealAccepted,
        CompetitionEventKind.TeamBanCorrectionPublished
    ];

    public Task<HistoricalAdjudicationEvidencePage> ReadAsync(Guid competitionId, Guid? competitionChallengeId,
        DateTimeOffset? beforeOccurredAt, Guid? beforeId, int scanLimit, CancellationToken ct) =>
        ReadCoreAsync(competitionId, competitionChallengeId, beforeOccurredAt, beforeId, scanLimit, true, ct);

    public Task<HistoricalAdjudicationEvidencePage> ReadRestrictedAsync(Guid competitionId, Guid? competitionChallengeId,
        DateTimeOffset? beforeOccurredAt, Guid? beforeId, int scanLimit, CancellationToken ct) =>
        ReadCoreAsync(competitionId, competitionChallengeId, beforeOccurredAt, beforeId, scanLimit, false, ct);

    private async Task<HistoricalAdjudicationEvidencePage> ReadCoreAsync(Guid competitionId, Guid? competitionChallengeId,
        DateTimeOffset? beforeOccurredAt, Guid? beforeId, int scanLimit, bool includeInternalTeams, CancellationToken ct)
    {
        scanLimit = Math.Clamp(scanLimit, 1, 500);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var competition = await db.Competitions.IgnoreQueryFilters().AsNoTracking().Where(row => row.Id == competitionId)
            .Select(row => new { row.Mode, row.StartAt, row.EndAt, row.TracksEnabled, row.TrackConfigurationJson }).SingleOrDefaultAsync(ct);
        if (competition is null) return new(HistoricalAdjudicationPreviewReadState.CompetitionNotFound, []);
        if (competition.Mode is not (GameMode.Ctf or GameMode.Awdp)) return new(HistoricalAdjudicationPreviewReadState.Available, []);
        var tracks = CompetitionTrackConfiguration.EffectiveFor(competition.Mode, competition.TracksEnabled, competition.TrackConfigurationJson);
        var internalKeys = tracks.Tracks.Where(track => track.IsInternal).Select(track => track.Key.ToLowerInvariant()).ToArray();
        var hiddenTeams = db.Teams.IgnoreQueryFilters().Where(team => team.CompetitionId == competitionId
            && internalKeys.Contains(team.TrackKey.ToLower())).Select(team => (Guid?)team.Id);
        var query = db.GameplayFacts.AsNoTracking().Where(fact => fact.CompetitionId == competitionId
            && (competition.Mode == GameMode.Ctf ? fact.Kind == GameplayFactKind.FlagAttempt || fact.Kind == GameplayFactKind.FixAttempt
                : fact.Kind == GameplayFactKind.BreakAttempt));
        if (!includeInternalTeams) query = query.Where(fact => !hiddenTeams.Contains(fact.TeamId));
        CompetitionOfficialWindow? window = null;
        if (competition.Mode == GameMode.Ctf)
        {
            window = await CompetitionOfficialWindowReader.ReadAsync(db, competitionId, competition.StartAt, competition.EndAt, ct);
            query = query.Where(fact => fact.OccurredAt >= window.Value.StartAt && fact.OccurredAt < window.Value.EndAt);
        }
        if (competitionChallengeId is { } selected) query = query.Where(fact => fact.CompetitionChallengeId == selected);
        if (beforeOccurredAt is { } at && beforeId is { } before)
            query = query.Where(fact => fact.OccurredAt < at || fact.OccurredAt == at && fact.Id.CompareTo(before) < 0);
        var facts = await query.OrderByDescending(fact => fact.OccurredAt).ThenByDescending(fact => fact.Id).Take(scanLimit)
            .Select(fact => new FactCandidate(fact.Id, fact.CompetitionChallengeId, fact.TeamId, fact.Kind, fact.Result,
                fact.FailureCode, fact.OccurredAt, fact.State)).ToArrayAsync(ct);
        if (facts.Length == 0) return new(HistoricalAdjudicationPreviewReadState.Available, []);

        var challengeIds = facts.Select(fact => fact.CompetitionChallengeId).Distinct().ToArray();
        var challenges = await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking()
            .Where(challenge => challengeIds.Contains(challenge.Id))
            .Join(db.Challenges.IgnoreQueryFilters().AsNoTracking(), instance => instance.ChallengeId, template => template.Id,
                (instance, template) => new { instance.Id, Title = instance.CustomTitle ?? template.Title,
                    template.DefinitionJson, Deleted = instance.DeletedAt != null || template.DeletedAt != null })
            .ToDictionaryAsync(row => row.Id, ct);
        var interactions = competition.Mode == GameMode.Ctf
            ? challenges.ToDictionary(pair => pair.Key, pair => pair.Value.Deleted ? null : ReadInteraction(pair.Value.DefinitionJson))
            : new Dictionary<Guid, CtfInteractionKind?>();
        var flagIds = interactions.Where(pair => pair.Value == CtfInteractionKind.FlagSubmission).Select(pair => pair.Key).ToArray();
        var patchIds = interactions.Where(pair => pair.Value == CtfInteractionKind.PatchVerification).Select(pair => pair.Key).ToArray();
        var candidateTeamIds = facts.Where(fact => fact.TeamId != null).Select(fact => fact.TeamId!.Value).Distinct().ToArray();
        var officialStart = window?.StartAt ?? competition.StartAt;
        var officialEnd = window?.EndAt ?? competition.EndAt;
        // Read current qualification and the first completion together. DISTINCT ON preserves
        // the same (OccurredAt, Id) ordering without a second query for predecessor teams.
        var teamRows = await db.Database.SqlQuery<TeamCompletionRow>($"""
            WITH firsts AS (
                SELECT DISTINCT ON (fact.competition_challenge_id, fact.team_id)
                    fact.id, fact.competition_challenge_id, fact.team_id, fact.occurred_at
                FROM gameplay_facts fact
                WHERE {competition.Mode == GameMode.Ctf} AND fact.competition_id = {competitionId} AND fact.team_id IS NOT NULL
                  AND ((fact.kind = {(short)GameplayFactKind.FlagAttempt} AND fact.competition_challenge_id = ANY({flagIds}))
                    OR (fact.kind = {(short)GameplayFactKind.FixAttempt} AND fact.competition_challenge_id = ANY({patchIds})))
                  AND fact.result = {(short)GameplayFactResult.Correct}
                  AND fact.occurred_at >= {officialStart} AND fact.occurred_at < {officialEnd}
                  AND ({includeInternalTeams} OR NOT EXISTS (
                    SELECT 1 FROM teams hidden WHERE hidden.id = fact.team_id AND hidden.competition_id = {competitionId}
                      AND lower(hidden.track_key) = ANY({internalKeys})))
                ORDER BY fact.competition_challenge_id, fact.team_id, fact.occurred_at, fact.id
            )
            SELECT team.id AS team_id, team.name, team.registration_status, team.is_banned,
                team.deleted_at, team.track_key, team.registered_at,
                firsts.id AS fact_id, firsts.competition_challenge_id, firsts.occurred_at
            FROM teams team LEFT JOIN firsts ON firsts.team_id = team.id
            WHERE team.id = ANY({candidateTeamIds}) OR firsts.id IS NOT NULL
            """).ToArrayAsync(ct);
        var teams = teamRows.GroupBy(row => row.TeamId).ToDictionary(group => group.Key, group =>
        {
            var row = group.First();
            return new TeamEvidence(row.TeamId, row.Name, row.RegistrationStatus, row.IsBanned, row.DeletedAt, row.TrackKey, row.RegisteredAt);
        });
        var firstCorrects = teamRows.Where(row => row.FactId != null).Select(row =>
            new FirstCorrectFact(row.FactId!.Value, row.CompetitionChallengeId!.Value, row.TeamId, row.OccurredAt!.Value)).ToArray();

        var ids = facts.Select(fact => fact.Id).ToArray();
        var eventKinds = EvidenceKinds.Select(kind => (short)kind).ToArray();
        var subjectKind = (short)EntityReferenceKind.GameplayFact;
        var earliest = facts.Min(fact => fact.OccurredAt);
        var eligibilityKinds = competition.Mode == GameMode.Ctf ? EligibilityKinds.Select(kind => (short)kind).ToArray() : [];
        // LATERAL bounds each fact independently; a prolific fact cannot exhaust another fact's evidence budget.
        // Include the independently bounded eligibility prefix in the same snapshot/read round trip.
        var events = await db.CompetitionEvents.FromSqlInterpolated($"""
            SELECT evidence.* FROM unnest({ids}) AS requested(id)
            CROSS JOIN LATERAL (
                SELECT event.* FROM competition_events event
                WHERE event.competition_id = {competitionId} AND event.subject_type = {subjectKind}
                  AND event.subject_id = requested.id AND event.kind = ANY({eventKinds})
                ORDER BY event.occurred_at DESC, event.id DESC LIMIT {MaximumEventsPerFact + 1}
            ) evidence
            UNION ALL (
                SELECT event.* FROM competition_events event
                WHERE event.competition_id = {competitionId} AND event.kind = ANY({eligibilityKinds})
                  AND event.occurred_at >= {earliest}
                ORDER BY event.occurred_at DESC, event.id DESC LIMIT 65
            )
            """).AsNoTracking().ToArrayAsync(ct);
        var eventsByFact = events.Where(item => EvidenceKinds.Contains(item.Kind)).GroupBy(item => item.SubjectId).ToDictionary(group => group.Key,
            group => group.OrderByDescending(item => item.OccurredAt).ThenByDescending(item => item.Id).ToArray());
        var eligibilityEvents = events.Where(item => EligibilityKinds.Contains(item.Kind))
            .OrderByDescending(item => item.OccurredAt).ThenByDescending(item => item.Id).ToArray();
        var eligibilityEvidence = eligibilityEvents.ToDictionary(item => item.Id, ReadEvent);
        var eligibleTeams = teams.Values.Where(team =>
            CtfCompletionEligibility.CanParticipate(team.RegistrationStatus, team.IsBanned, team.DeletedAt != null)
            && (window is null || team.RegisteredAt < window.Value.EndAt)
            && CtfCompletionEligibility.Track(tracks, team.TrackKey).EarnsBlood).Select(team => team.Id).ToHashSet();
        bool Eligible(Guid teamId) => eligibleTeams.Contains(teamId);
        var firstsByChallenge = firstCorrects.GroupBy(item => item.CompetitionChallengeId).ToDictionary(group => group.Key, group => group.ToArray());
        var evidence = facts.Select(fact =>
        {
            var firsts = firstsByChallenge.GetValueOrDefault(fact.CompetitionChallengeId, []);
            var own = firsts.Length == 0 ? null : firsts.SingleOrDefault(item => item.TeamId == fact.TeamId);
            var earlier = firsts.Length == 0 ? [] : firsts.Where(item => item.TeamId != fact.TeamId
                && CtfCompletionEligibility.IsBefore(item.OccurredAt, item.Id, fact.OccurredAt, fact.Id)).ToArray();
            var currentEligible = fact.TeamId is { } teamId && Eligible(teamId);
            var ownEvents = eventsByFact.GetValueOrDefault(fact.Id, []);
            // The retained prefix is already sorted descending by (OccurredAt, Id).
            var timeline = ownEvents.Length == 0 ? [] : ownEvents.Take(MaximumEventsPerFact).Reverse().Select(ReadEvent).ToArray();
            var adjustments = eligibilityEvents.Length == 0 ? [] : eligibilityEvents.Where(item => item.OccurredAt >= fact.OccurredAt
                && (item.Kind == CompetitionEventKind.TrackConfigurationUpdated || item.TeamId == fact.TeamId
                    || earlier.Any(first => first.TeamId == item.TeamId))).Take(64).Select(item => eligibilityEvidence[item.Id]).ToArray();
            var knownInteraction = interactions.GetValueOrDefault(fact.CompetitionChallengeId);
            return new HistoricalAdjudicationEvidence(fact.Id, fact.CompetitionChallengeId,
                challenges.GetValueOrDefault(fact.CompetitionChallengeId)?.Title ?? fact.CompetitionChallengeId.ToString(),
                fact.TeamId, fact.TeamId is { } owner ? teams.GetValueOrDefault(owner)?.Name ?? owner.ToString() : null,
                competition.Mode, fact.Kind, fact.Result, fact.FailureCode, fact.OccurredAt,
                own is not null && CtfCompletionEligibility.IsBefore(own.OccurredAt, own.Id, fact.OccurredAt, fact.Id),
                earlier.Count(item => Eligible(item.TeamId)),
                competition.Mode == GameMode.Ctf && (!currentEligible || earlier.Any(item => !Eligible(item.TeamId)) || eligibilityEvents.Length > 64),
                timeline, timeline.Length == 0 ? [] : timeline.Where(item => HistoricalAdjudicationAnalyzer.IsBlood(item.Kind)).Select(item => ToBloodRank(item.Kind)).ToArray(),
                fact.State, ownEvents.Length > MaximumEventsPerFact || eligibilityEvents.Length > 64
                    ? AdjudicationEvidenceCompleteness.Truncated : AdjudicationEvidenceCompleteness.Complete,
                adjustments.Length > 0, currentEligible,
                competition.Mode != GameMode.Ctf || knownInteraction is { } interaction && CtfCompletionEligibility.Matches(fact.Kind, interaction), adjustments);
        }).ToArray();
        await transaction.CommitAsync(ct);
        return new(HistoricalAdjudicationPreviewReadState.Available, evidence);
    }

    internal static AdjudicationEventEvidence ReadEvent(CompetitionEvent item)
    {
        try
        {
            using var document = JsonDocument.Parse(item.PayloadJson);
            return new(item.Id, item.OccurredAt, item.Kind,
                ReadEnum<GameplayFactState>(document.RootElement, "gameplayFactState"),
                ReadEnum<GameplayFactResult>(document.RootElement, "gameplayFactResult"), item.ActorUserId, item.ParentEventId,
                GameplayFactId: item.SubjectType == EntityReferenceKind.GameplayFact ? item.SubjectId : null);
        }
        catch (JsonException) { return new(item.Id, item.OccurredAt, item.Kind, null, null, item.ActorUserId, item.ParentEventId, false,
            item.SubjectType == EntityReferenceKind.GameplayFact ? item.SubjectId : null); }
    }

    public async Task<HistoricalAdjudicationEventPage?> ReadEventsAsync(Guid competitionId, Guid gameplayFactId,
        DateTimeOffset? beforeOccurredAt, Guid? beforeId, int limit, bool includeInternalTeams, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var scope = await db.GameplayFacts.AsNoTracking().Where(fact => fact.Id == gameplayFactId && fact.CompetitionId == competitionId)
            .Join(db.Competitions.IgnoreQueryFilters().AsNoTracking(), fact => fact.CompetitionId, competition => competition.Id,
                (fact, competition) => new { fact.Kind, fact.TeamId, competition.Mode, competition.TracksEnabled, competition.TrackConfigurationJson })
            .SingleOrDefaultAsync(ct);
        if (scope is null || !(scope.Mode == GameMode.Ctf && scope.Kind is GameplayFactKind.FlagAttempt or GameplayFactKind.FixAttempt
            || scope.Mode == GameMode.Awdp && scope.Kind == GameplayFactKind.BreakAttempt)) return null;
        if (!includeInternalTeams && scope.TeamId is Guid teamId)
        {
            var trackKey = await db.Teams.IgnoreQueryFilters().AsNoTracking().Where(team => team.Id == teamId)
                .Select(team => team.TrackKey).SingleOrDefaultAsync(ct);
            var tracks = CompetitionTrackConfiguration.EffectiveFor(scope.Mode, scope.TracksEnabled, scope.TrackConfigurationJson);
            if (trackKey is null || CtfCompletionEligibility.Track(tracks, trackKey).IsInternal) return null;
        }
        var query = db.CompetitionEvents.AsNoTracking().Where(item => item.CompetitionId == competitionId
            && item.SubjectType == EntityReferenceKind.GameplayFact && item.SubjectId == gameplayFactId && EvidenceKinds.Contains(item.Kind));
        if (beforeOccurredAt is { } at && beforeId is { } id)
            query = query.Where(item => item.OccurredAt < at || item.OccurredAt == at && item.Id.CompareTo(id) < 0);
        var rows = await query.OrderByDescending(item => item.OccurredAt).ThenByDescending(item => item.Id)
            .Take(Math.Clamp(limit, 1, 100) + 1).ToArrayAsync(ct);
        var page = rows.Take(Math.Clamp(limit, 1, 100)).ToArray();
        var more = rows.Length > page.Length;
        await transaction.CommitAsync(ct);
        return new(page.Select(ReadEvent).ToArray(), more ? page[^1].OccurredAt : null, more ? page[^1].Id : null);
    }

    private static T? ReadEnum<T>(JsonElement root, string name) where T : struct, Enum =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && Enum.TryParse<T>(value.GetString(), out var parsed) && Enum.IsDefined(parsed) ? parsed : null;

    private static CtfInteractionKind? ReadInteraction(string json)
    {
        try { return CtfConfigurationUpgrader.ParseChallenge(json).InteractionKind; }
        catch (Exception exception) when (exception is JsonException or GameModeConfigurationException or InvalidOperationException) { return null; }
    }

    private static LeaderboardBloodRank ToBloodRank(CompetitionEventKind kind) => kind switch
    {
        CompetitionEventKind.FirstBloodAwarded => LeaderboardBloodRank.First,
        CompetitionEventKind.SecondBloodAwarded => LeaderboardBloodRank.Second,
        CompetitionEventKind.ThirdBloodAwarded => LeaderboardBloodRank.Third,
        _ => throw new InvalidOperationException("Not a blood event.")
    };

    private sealed record FactCandidate(Guid Id, Guid CompetitionChallengeId, Guid? TeamId, GameplayFactKind Kind,
        GameplayFactResult? Result, GameplayFactFailureCode? FailureCode, DateTimeOffset OccurredAt, GameplayFactState State);
    private sealed record FirstCorrectFact(Guid Id, Guid CompetitionChallengeId, Guid TeamId, DateTimeOffset OccurredAt);
    private sealed record TeamEvidence(Guid Id, string Name, TeamRegistrationStatus RegistrationStatus,
        bool IsBanned, DateTimeOffset? DeletedAt, string TrackKey, DateTimeOffset RegisteredAt);
    private sealed record TeamCompletionRow(Guid TeamId, string Name, TeamRegistrationStatus RegistrationStatus,
        bool IsBanned, DateTimeOffset? DeletedAt, string TrackKey, DateTimeOffset RegisteredAt,
        Guid? FactId, Guid? CompetitionChallengeId, DateTimeOffset? OccurredAt);
}
