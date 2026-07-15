using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Permissions;
using NoCTF.Core;
using NoCTF.Infrastructure;
using Npgsql;

namespace NoCTF.API.Endpoints.Admin;

public class AddCollaboratorRequest
{
    public Guid UserId { get; set; }
    public string Role { get; set; } = "Observer";
}

public class AddCollaboratorEndpoint(ApplicationDbContext dbContext, ICompetitionPermissionService permissions) : Endpoint<AddCollaboratorRequest>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/competitions/{id}/collaborators");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(AddCollaboratorRequest req, CancellationToken ct)
    {
        var competitionId = Route<Guid>("id");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, competitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var targetsExist = await dbContext.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(competition =>
                competition.Id == competitionId &&
                dbContext.Users.Any(user => user.Id == req.UserId), ct);
        if (!targetsExist)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var exists = await dbContext.CompetitionCollaborators
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(cc => cc.CompetitionId == competitionId && cc.UserId == req.UserId, ct);

        if (exists)
        {
            ThrowError("User is already a collaborator.");
        }

        if (!Enum.TryParse<CollaboratorRole>(req.Role, ignoreCase: true, out var role))
            role = CollaboratorRole.Observer;

        var collaborator = new CompetitionCollaborator
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            UserId = req.UserId,
            Role = role,
            AddedAt = DateTime.UtcNow,
        };

        dbContext.CompetitionCollaborators.Add(collaborator);
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (IsCollaboratorConflict(exception))
        {
            dbContext.Entry(collaborator).State = EntityState.Detached;
            AddError("User is already a collaborator.");
            await SendErrorsAsync(409, ct);
            return;
        }

        await SendCreatedAtAsync<GetCollaboratorsEndpoint>(new { id = competitionId }, null, cancellation: ct);
    }

    internal static bool IsCollaboratorConflict(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_competitioncollaborators_competition_user"
        };
}
