using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.RefreshSession;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

public sealed class AccessTokenVersionReader(NoCtfDbContext db) : IAccessTokenVersionReader
{
    public Task<bool> IsCurrentAsync(Guid userId, int tokenVersion, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(
            user => user.Id == userId
                && user.AccountStatus == NoCTF.Domain.Identity.UserAccountStatus.Active
                && user.TokenVersion == tokenVersion,
            cancellationToken);
}
