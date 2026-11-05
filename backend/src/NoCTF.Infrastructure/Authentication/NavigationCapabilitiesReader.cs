using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Account;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

public sealed class NavigationCapabilitiesReader(NoCtfDbContext db) : INavigationCapabilitiesReader
{
    public Task<NavigationCapabilities?> ReadAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking().IgnoreAutoIncludes()
            .Where(user => user.Id == userId && user.AccountStatus == UserAccountStatus.Active)
            .Select(user => new NavigationCapabilities(
                user.Role == UserRole.Administrator || db.Competitions.Any(competition => competition.DeletedAt == null
                    && (competition.OwnerId == user.Id || competition.Collaborators.Any(collaborator => collaborator.UserId == user.Id)))))
            .SingleOrDefaultAsync(ct);
}
