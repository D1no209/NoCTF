using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Ports;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

public sealed class EfAccessTokenVersionReader(NoCtfDbContext db) : IAccessTokenVersionReader
{
    public Task<bool> IsCurrentAsync(Guid userId, int tokenVersion, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(
            user => user.Id == userId && user.TokenVersion == tokenVersion,
            cancellationToken);
}
