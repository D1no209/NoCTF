using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Visibility;

public sealed class CompetitionVisibilityStore(
    NoCtfDbContext db,
    ICompetitionEventRecorder? eventRecorder = null,
    NoCTF.Infrastructure.Competitions.Management.CompetitionReadModelCache? readModels = null)
    : ICompetitionVisibilityStore
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CompetitionVisibilityConfigurationView?> GetAsync(
        Guid competitionId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId)
            .SingleOrDefaultAsync(ct);
        return competition is null ? null : View(competition, now);
    }

    public async Task<CompetitionVisibilityMutationResult> UpdateAsync(
        UpdateCompetitionVisibilityCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);
        if (await CompetitionStateReader.ReadAsync(db, command.CompetitionId, ct) is null)
            return new(CompetitionVisibilityMutationState.NotFound);

        var competition = await db.Competitions
            .SingleAsync(item => item.Id == command.CompetitionId, ct);
        var validationFailure = CompetitionVisibilityRules.Validate(
            competition.Status,
            competition.StartAt,
            competition.EndAt,
            command);
        if (validationFailure is { } state)
            return new(state, View(competition, command.Now));

        var before = CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
            competition.FrozenStartAt,
            competition.HiddenStartAt,
            command.Now);
        competition.FrozenStartAt = TruncateToMicroseconds(command.FrozenStartAt);
        competition.HiddenStartAt = TruncateToMicroseconds(command.HiddenStartAt);
        competition.UpdatedAt = command.Now;
        var after = CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
            competition.FrozenStartAt,
            competition.HiddenStartAt,
            command.Now);

        await events.RecordAsync(new(
            competition.Id,
            CompetitionEventKind.LeaderboardVisibilityChanged,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Public,
            command.Now,
            ActorUserId: command.ActorId,
            PayloadJson: JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                from = before,
                to = after,
                frozenStartAt = competition.FrozenStartAt,
                hiddenStartAt = competition.HiddenStartAt,
                actorUserId = command.ActorId,
                operatedAt = command.Now,
                reason = NormalizeReason(command.Reason)
            }, JsonOptions),
            CompetitionStatus: competition.Status,
            LeaderboardVisibility: after), ct);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (readModels is not null)
            await readModels.InvalidateAsync(competition.Id, ct);
        return new(
            CompetitionVisibilityMutationState.Updated,
            View(competition, command.Now));
    }

    private static CompetitionVisibilityConfigurationView View(
        Competition competition,
        DateTimeOffset now) =>
        new(
            competition.Id,
            competition.Status,
            competition.StartAt,
            competition.EndAt,
            CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
                competition.FrozenStartAt,
                competition.HiddenStartAt,
                now),
            competition.FrozenStartAt,
            competition.HiddenStartAt);

    private static string? NormalizeReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    private static DateTimeOffset? TruncateToMicroseconds(DateTimeOffset? value) =>
        value is { } timestamp
            ? timestamp.AddTicks(-(timestamp.Ticks % TimeSpan.TicksPerMicrosecond))
            : null;
}
