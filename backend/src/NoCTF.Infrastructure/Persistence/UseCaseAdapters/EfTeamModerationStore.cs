using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Common;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Auditing;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfTeamModerationStore(NoCtfDbContext db) : ITeamModerationStore
{
    public Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken cancellationToken) =>
        db.Competitions.AsNoTracking().Where(item => item.Id == competitionId && !item.Deletion.IsDeleted)
            .Select(item => (CompetitionStatus?)item.Status).SingleOrDefaultAsync(cancellationToken);

    public async Task<TeamModerationStoreResult> ApplyAsync(
        TeamModerationCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var status = await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, cancellationToken);
        if (status is null)
            return new(TeamModerationFailure.CompetitionNotFound);
        if (status == CompetitionStatus.Finished)
            return new(TeamModerationFailure.CompetitionFinished);
        var team = await db.Teams.SingleOrDefaultAsync(
            item => item.CompetitionId == command.CompetitionId && item.Id == command.TeamId,
            cancellationToken);
        if (team is null)
            return new(TeamModerationFailure.TeamNotFound);
        if (team.Ban.IsBanned == command.Ban)
            return new();

        team.Ban.IsBanned = command.Ban;
        team.Ban.BannedAt = command.Ban ? command.OccurredAt : null;
        team.Ban.BannedById = command.Ban ? command.ActorId : null;
        team.Ban.Reason = command.Ban ? command.Reason : null;
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.CreateVersion7(command.OccurredAt),
            CompetitionId = command.CompetitionId,
            ActorId = command.ActorId,
            Action = command.Ban ? "team.ban" : "team.unban",
            SubjectType = "team",
            SubjectId = command.TeamId,
            MetadataJson = JsonSerializer.Serialize(new { command.Reason }),
            OccurredAt = command.OccurredAt
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new();
    }
}
