using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Notifications;

/// <summary>Resolves current audience membership for a content-free realtime hint.</summary>
public sealed class NotificationChangeAudienceResolver(IDbContextFactory<NoCtfDbContext> dbFactory)
{
    public async Task<IReadOnlyList<Guid>> ResolveAsync(
        IReadOnlyList<NotificationAudience> audiences,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var userIds = audiences
            .Where(item => item.TargetType == NotificationTargetType.User)
            .Select(item => item.TargetId)
            .ToHashSet();

        var teamIds = audiences
            .Where(item => item.TargetType == NotificationTargetType.TeamMembers)
            .Select(item => item.TargetId)
            .Distinct()
            .ToArray();
        if (teamIds.Length > 0)
        {
            var members = await db.Teams.AsNoTracking()
                .Where(team => teamIds.Contains(team.Id) && team.DeletedAt == null)
                .SelectMany(team => team.Members.Select(member => member.UserId))
                .ToArrayAsync(ct);
            userIds.UnionWith(members);
        }

        var participantCompetitionIds = audiences
            .Where(item => item.TargetType == NotificationTargetType.CompetitionParticipants)
            .Select(item => item.TargetId)
            .Distinct()
            .ToArray();
        if (participantCompetitionIds.Length > 0)
        {
            var members = await db.Teams.AsNoTracking()
                .Where(team => participantCompetitionIds.Contains(team.CompetitionId)
                    && team.DeletedAt == null
                    && team.RegistrationStatus == TeamRegistrationStatus.Approved
                    && !team.IsBanned)
                .SelectMany(team => team.Members.Select(member => member.UserId))
                .ToArrayAsync(ct);
            userIds.UnionWith(members);
        }

        var collaboratorCompetitionIds = audiences
            .Where(item => item.TargetType == NotificationTargetType.CompetitionCollaborators)
            .Select(item => item.TargetId)
            .Distinct()
            .ToArray();
        if (collaboratorCompetitionIds.Length > 0)
        {
            var competitions = await db.Competitions.AsNoTracking()
                .Where(competition => collaboratorCompetitionIds.Contains(competition.Id)
                    && competition.DeletedAt == null)
                .Select(competition => new
                {
                    competition.OwnerId,
                    Collaborators = competition.Collaborators.Select(item => item.UserId).ToArray()
                })
                .ToArrayAsync(ct);
            foreach (var competition in competitions)
            {
                userIds.Add(competition.OwnerId);
                userIds.UnionWith(competition.Collaborators);
            }
        }

        if (audiences.Any(item => item.TargetType == NotificationTargetType.PlatformAdministrators))
        {
            var administrators = await db.Users.AsNoTracking()
                .Where(user => user.Role == UserRole.Administrator
                    && user.AccountStatus == UserAccountStatus.Active)
                .Select(user => user.Id)
                .ToArrayAsync(ct);
            userIds.UnionWith(administrators);
        }

        var threadRoots = audiences
            .Where(item => item.ThreadRootId.HasValue)
            .Select(item => item.ThreadRootId!.Value)
            .Distinct()
            .ToArray();
        if (threadRoots.Length > 0)
        {
            var participants = await db.Notifications.AsNoTracking()
                .Where(notification => notification.SourceType == NotificationSourceType.User
                    && notification.SourceId.HasValue
                    && (notification.ThreadRootId.HasValue
                        && threadRoots.Contains(notification.ThreadRootId.Value)
                        || threadRoots.Contains(notification.Id)))
                .Select(notification => notification.SourceId!.Value)
                .Distinct()
                .ToArrayAsync(ct);
            userIds.UnionWith(participants);
        }

        return userIds.Order().ToArray();
    }
}
