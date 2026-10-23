using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Scoring;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Competitions.Scoring;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Scoring;

public sealed class CompetitionScorePresetStore(NoCtfDbContext db, IChallengeConfigurationCatalog configurations,
    ICompetitionEventRecorder events, IPostCommitMessagePublisher messages) : ICompetitionScorePresetStore
{
    public async Task<CompetitionScorePresetsResult> ListAsync(Guid competitionId, CancellationToken ct)
    {
        var mode = await db.Competitions.Where(item => item.Id == competitionId).Select(item => (GameMode?)item.Mode).SingleOrDefaultAsync(ct);
        if (mode is null) return new(Failure: CompetitionScorePresetFailure.CompetitionNotFound);
        if (mode != GameMode.Ctf) return new(Failure: CompetitionScorePresetFailure.UnsupportedMode);
        var presets = await db.Set<CompetitionScorePreset>().AsNoTracking().Where(item => item.CompetitionId == competitionId)
            .OrderBy(item => item.NormalizedName).ToArrayAsync(ct);
        var tags = await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CompetitionId == competitionId)
            .SelectMany(item => item.Tags.Select(tag => tag.NormalizedName)).ToArrayAsync(ct);
        return new(presets.Select(preset => new CompetitionScorePresetView(preset, tags.Count(name => name == preset.NormalizedName))).ToArray());
    }

    public async Task<CompetitionScorePresetMutationResult> SaveAsync(Guid competitionId, Guid? presetId,
        string name, ScoreCurveValue curve, DateTimeOffset now, CancellationToken ct)
    {
        try { return await RetryAsync(() => SaveOnceAsync(competitionId, presetId, name, curve, now, ct), ct); }
        catch (DbUpdateException)
        {
            messages.DiscardPendingMessages();
            db.ChangeTracker.Clear();
            var key = CompetitionChallengeTags.NormalizeName(name);
            if (await db.Set<CompetitionScorePreset>().AnyAsync(item => item.CompetitionId == competitionId
                && item.Id != presetId && item.NormalizedName == key, ct))
                return new(Failure: CompetitionScorePresetFailure.NameConflict);
            throw;
        }
    }

    private async Task<CompetitionScorePresetMutationResult> SaveOnceAsync(Guid competitionId, Guid? presetId,
        string name, ScoreCurveValue curve, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, System.Data.IsolationLevel.Serializable, ct);
        var competition = await db.Competitions.AsSplitQuery().SingleOrDefaultAsync(item => item.Id == competitionId, ct);
        if (competition is null) return new(Failure: CompetitionScorePresetFailure.CompetitionNotFound);
        if (competition.Mode != GameMode.Ctf) return new(Failure: CompetitionScorePresetFailure.UnsupportedMode);
        var presets = await db.Set<CompetitionScorePreset>().Where(item => item.CompetitionId == competitionId).ToArrayAsync(ct);
        var current = presetId is null ? null : presets.SingleOrDefault(item => item.Id == presetId);
        if (presetId is not null && current is null) return new(Failure: CompetitionScorePresetFailure.PresetNotFound);
        var normalized = CompetitionChallengeTags.NormalizeName(name);
        if (presets.Any(item => item.Id != presetId && item.NormalizedName == normalized))
            return new(Failure: CompetitionScorePresetFailure.NameConflict);
        var oldName = current?.NormalizedName;
        var challenges = await db.CompetitionChallenges.IgnoreQueryFilters().AsSplitQuery()
            .Where(item => item.CompetitionId == competitionId
                && item.Tags.Any(tag => tag.NormalizedName == normalized || tag.NormalizedName == oldName))
            .ToArrayAsync(ct);
        var eligibleTeams = await db.Teams.CountAsync(item => item.CompetitionId == competitionId
            && item.RegistrationStatus == TeamRegistrationStatus.Approved && !item.IsBanned, ct);
        var validationRules = challenges.Select(item => item.Rules).OfType<CtfCompetitionChallengeRules>()
            .Select(item => item.ScoreSettlementMode).Append(null).Distinct();
        foreach (var settlement in validationRules)
        {
            var errors = configurations.Validate(GameMode.Ctf,
                new CtfCompetitionChallengeRules { HasScoreCurve = true, ScoreCurve = curve, ScoreSettlementMode = settlement },
                competition.ModeConfiguration!, eligibleTeams);
            if (errors.Count > 0) return new(Failure: CompetitionScorePresetFailure.InvalidCurve, Detail: string.Join(" ", errors));
        }
        current ??= new CompetitionScorePreset { Id = Guid.NewGuid(), CompetitionId = competitionId, Name = name, NormalizedName = normalized };
        var otherNames = presets.Where(item => item.Id != current.Id).Select(item => item.NormalizedName).ToHashSet(StringComparer.Ordinal);
        var replaced = 0;
        foreach (var challenge in challenges)
        {
            if (challenge.Tags.Any(tag => otherNames.Contains(tag.NormalizedName))) replaced++;
            var names = challenge.Tags.OrderBy(tag => tag.Position)
                .Where(tag => !otherNames.Contains(tag.NormalizedName))
                .Select(tag => tag.NormalizedName == oldName || tag.NormalizedName == normalized ? name : tag.Name)
                .DistinctBy(CompetitionChallengeTags.NormalizeName).ToArray();
            CompetitionChallengeTags.Replace(challenge.Tags, names);
            challenge.UpdatedAt = now;
        }
        if (presetId is null) db.Add(current);
        current.Name = name;
        current.NormalizedName = normalized;
        current.ScoreCurve = curve;
        current.UpdatedAt = now;
        competition.UpdatedAt = now;
        await events.RecordAsync(new(competitionId, CompetitionEventKind.CompetitionUpdated,
            CompetitionEventLevel.Information, CompetitionEventVisibility.Staff, now), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await transaction.FlushMessagesAsync(messages);
        return new(new(current, challenges.Length), challenges.Length, replaced);
    }

    public Task<CompetitionScorePresetMutationResult> DeleteAsync(Guid competitionId, Guid presetId, DateTimeOffset now, CancellationToken ct) =>
        RetryAsync(async () =>
        {
            await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, System.Data.IsolationLevel.Serializable, ct);
            var competition = await db.Competitions.SingleOrDefaultAsync(item => item.Id == competitionId, ct);
            if (competition is null) return new(Failure: CompetitionScorePresetFailure.CompetitionNotFound);
            if (competition.Mode != GameMode.Ctf) return new(Failure: CompetitionScorePresetFailure.UnsupportedMode);
            var preset = await db.Set<CompetitionScorePreset>().SingleOrDefaultAsync(item => item.CompetitionId == competitionId && item.Id == presetId, ct);
            if (preset is null) return new(Failure: CompetitionScorePresetFailure.PresetNotFound);
            var affected = await db.CompetitionChallenges.IgnoreQueryFilters().CountAsync(item => item.CompetitionId == competitionId
                && item.Tags.Any(tag => tag.NormalizedName == preset.NormalizedName), ct);
            db.Remove(preset);
            competition.UpdatedAt = now;
            await events.RecordAsync(new(competitionId, CompetitionEventKind.CompetitionUpdated,
                CompetitionEventLevel.Information, CompetitionEventVisibility.Staff, now), ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await transaction.FlushMessagesAsync(messages);
            return new(AffectedChallengeCount: affected);
        }, ct);

    private async Task<CompetitionScorePresetMutationResult> RetryAsync(Func<Task<CompetitionScorePresetMutationResult>> action, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await action(); }
            catch (Exception exception) when (attempt < 2 && RelationalRetry.IsTransientConcurrency(exception))
            {
                messages.DiscardPendingMessages();
                db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5, 31)), ct);
            }
        }
    }
}
