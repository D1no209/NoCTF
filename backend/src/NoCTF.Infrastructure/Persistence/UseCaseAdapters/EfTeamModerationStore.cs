using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Common;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Auditing;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfTeamModerationStore(NoCtfDbContext db) : ITeamModerationStore
{
    public async Task<OperationResult> ApplyAsync(
        TeamModerationCommand command,
        CancellationToken cancellationToken)
    {
        var team = await db.Teams.SingleOrDefaultAsync(
            item => item.CompetitionId == command.CompetitionId && item.Id == command.TeamId,
            cancellationToken);
        if (team is null)
            return OperationResult.Failure("team_not_found", "The team was not found.");
        if (team.Ban.IsBanned == command.Ban)
            return OperationResult.Success();

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
        return OperationResult.Success();
    }
}
