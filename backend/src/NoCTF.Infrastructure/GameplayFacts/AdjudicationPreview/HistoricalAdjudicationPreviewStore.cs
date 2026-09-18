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

public sealed class HistoricalAdjudicationPreviewStore(NoCtfDbContext db) : IHistoricalAdjudicationEvidenceStore
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

    public async Task<HistoricalAdjudicationEvidencePage> ReadAsync(Guid competitionId, Guid? competitionChallengeId,
        DateTimeOffset? beforeOccurredAt, Guid? beforeId, int scanLimit, CancellationToken ct)
    {
        scanLimit = Math.Clamp(scanLimit, 1, 500);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var competition = await db.Competitions.IgnoreQueryFilters().AsNoTracking().Where(row => row.Id == competitionId)
            .Select(row => new { row.Mode, row.StartAt, row.EndAt, row.TracksEnabled, row.TrackConfigurationJson }).SingleOrDefaultAsync(ct);
        if (competition is null) return new(HistoricalAdjudicationPreviewReadState.CompetitionNotFound, []);
        if (competition.Mode is not (GameMode.Ctf or GameMode.Awdp)) return new(HistoricalAdjudicationPreviewReadState.Available, []);
        var query = db.GameplayFacts.AsNoTracking().Where(fact => fact.CompetitionId == competitionId
            && (competition.Mode == GameMode.Ctf ? fact.Kind == GameplayFactKind.FlagAttempt || fact.Kind == GameplayFactKind.FixAttempt
                : fact.Kind == GameplayFactKind.BreakAttempt));
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
        var firstCorrects = competition.Mode == GameMode.Ctf
            ? await db.GameplayFacts.AsNoTracking().Where(fact => fact.CompetitionId == competitionId && fact.TeamId != null
                && (fact.Kind == GameplayFactKind.FlagAttempt && flagIds.Contains(fact.CompetitionChallengeId)
                    || fact.Kind == GameplayFactKind.FixAttempt && patchIds.Contains(fact.CompetitionChallengeId))
                && fact.Result == GameplayFactResult.Correct && fact.OccurredAt >= window!.Value.StartAt && fact.OccurredAt < window.Value.EndAt)
                .GroupBy(fact => new { fact.CompetitionChallengeId, fact.TeamId })
                .Select(group => group.OrderBy(fact => fact.OccurredAt).ThenBy(fact => fact.Id)
                    .Select(fact => new FirstCorrectFact(fact.Id, fact.CompetitionChallengeId, fact.TeamId!.Value, fact.OccurredAt)).First())
                .ToArrayAsync(ct)
            : [];

        var ids = facts.Select(fact => fact.Id).ToArray();
        var eventKinds = EvidenceKinds.Select(kind => (short)kind).ToArray();
        var subjectKind = (short)EntityReferenceKind.GameplayFact;
        // LATERAL bounds each fact independently; a prolific fact cannot exhaust another fact's evidence budget.
        var events = await db.CompetitionEvents.FromSqlInterpolated($"""
            SELECT evidence.* FROM unnest({ids}) AS requested(id)
            CROSS JOIN LATERAL (
                SELECT event.* FROM competition_events event
                WHERE event.competition_id = {competitionId} AND event.subject_type = {subjectKind}
                  AND event.subject_id = requested.id AND event.kind = ANY({eventKinds})
                ORDER BY event.occurred_at DESC, event.id DESC LIMIT {MaximumEventsPerFact + 1}
            ) evidence
            """).AsNoTracking().ToArrayAsync(ct);
        var eventsByFact = events.GroupBy(item => item.SubjectId).ToDictionary(group => group.Key,
            group => group.OrderByDescending(item => item.OccurredAt).ThenByDescending(item => item.Id).ToArray());
        var earliest = facts.Min(fact => fact.OccurredAt);
        var eligibilityEvents = competition.Mode == GameMode.Ctf
            ? await db.CompetitionEvents.AsNoTracking().Where(item => item.CompetitionId == competitionId
                    && EligibilityKinds.Contains(item.Kind) && item.OccurredAt >= earliest)
                .OrderByDescending(item => item.OccurredAt).ThenByDescending(item => item.Id).Take(65).ToArrayAsync(ct)
            : [];
        var teamsIds = facts.Where(fact => fact.TeamId != null).Select(fact => fact.TeamId!.Value)
            .Concat(firstCorrects.Select(fact => fact.TeamId)).Distinct().ToArray();
        var teams = await db.Teams.IgnoreQueryFilters().AsNoTracking().Where(team => teamsIds.Contains(team.Id))
            .Select(team => new TeamEvidence(team.Id, team.Name, team.RegistrationStatus, team.IsBanned,
                team.DeletedAt, team.TrackKey, team.RegisteredAt)).ToDictionaryAsync(team => team.Id, ct);
        var tracks = CompetitionTrackConfiguration.EffectiveFor(competition.Mode, competition.TracksEnabled, competition.TrackConfigurationJson);
        bool Eligible(Guid teamId) => teams.TryGetValue(teamId, out var team)
            && CtfCompletionEligibility.CanParticipate(team.RegistrationStatus, team.IsBanned, team.DeletedAt != null)
            && (window is null || team.RegisteredAt < window.Value.EndAt)
            && CtfCompletionEligibility.Track(tracks, team.TrackKey).EarnsBlood;
        var firstsByChallenge = firstCorrects.GroupBy(item => item.CompetitionChallengeId).ToDictionary(group => group.Key, group => group.ToArray());
        var evidence = facts.Select(fact =>
        {
            var firsts = firstsByChallenge.GetValueOrDefault(fact.CompetitionChallengeId, []);
            var own = firsts.SingleOrDefault(item => item.TeamId == fact.TeamId);
            var earlier = firsts.Where(item => item.TeamId != fact.TeamId
                && CtfCompletionEligibility.IsBefore(item.OccurredAt, item.Id, fact.OccurredAt, fact.Id)).ToArray();
            var currentEligible = fact.TeamId is { } teamId && Eligible(teamId);
            var ownEvents = eventsByFact.GetValueOrDefault(fact.Id, []);
            var timeline = ownEvents.Take(MaximumEventsPerFact).Select(ReadEvent)
                .OrderBy(item => item.OccurredAt).ThenBy(item => item.EventId).ToArray();
            var adjustments = eligibilityEvents.Where(item => item.OccurredAt >= fact.OccurredAt
                && (item.Kind == CompetitionEventKind.TrackConfigurationUpdated || item.TeamId == fact.TeamId
                    || earlier.Any(first => first.TeamId == item.TeamId))).Take(64).Select(ReadEvent).ToArray();
            var knownInteraction = interactions.GetValueOrDefault(fact.CompetitionChallengeId);
            return new HistoricalAdjudicationEvidence(fact.Id, fact.CompetitionChallengeId,
                challenges.GetValueOrDefault(fact.CompetitionChallengeId)?.Title ?? fact.CompetitionChallengeId.ToString(),
                fact.TeamId, fact.TeamId is { } owner ? teams.GetValueOrDefault(owner)?.Name ?? owner.ToString() : null,
                competition.Mode, fact.Kind, fact.Result, fact.FailureCode, fact.OccurredAt,
                own is not null && CtfCompletionEligibility.IsBefore(own.OccurredAt, own.Id, fact.OccurredAt, fact.Id),
                earlier.Count(item => Eligible(item.TeamId)),
                competition.Mode == GameMode.Ctf && (!currentEligible || earlier.Any(item => !Eligible(item.TeamId)) || eligibilityEvents.Length > 64),
                timeline, timeline.Where(item => HistoricalAdjudicationAnalyzer.IsBlood(item.Kind)).Select(item => ToBloodRank(item.Kind)).ToArray(),
                fact.State, ownEvents.Length > MaximumEventsPerFact ? AdjudicationEvidenceCompleteness.Truncated : AdjudicationEvidenceCompleteness.Complete,
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
                ReadEnum<GameplayFactResult>(document.RootElement, "gameplayFactResult"), item.ActorUserId, item.ParentEventId);
        }
        catch (JsonException) { return new(item.Id, item.OccurredAt, item.Kind, null, null, item.ActorUserId, item.ParentEventId, false); }
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
}
