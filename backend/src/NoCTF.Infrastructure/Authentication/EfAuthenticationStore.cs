using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Ports;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

public sealed class EfAuthenticationStore(NoCtfDbContext db) : IUserAuthenticationStore
{
    public async Task<AuthenticatedUser?> FindByLoginAsync(string login, CancellationToken cancellationToken)
    {
        var normalized = login.Trim().ToUpperInvariant();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            item => item.NormalizedEmail == normalized
                || item.NormalizedUserName == normalized
                || item.Id.ToString() == login,
            cancellationToken);
        return user is null ? null : new(user.Id, user.UserName, user.Role.ToString(), user.TokenVersion);
    }

    public async Task<bool> VerifyPasswordAsync(Guid userId, string password, CancellationToken cancellationToken)
    {
        var hash = await db.Users.Where(item => item.Id == userId).Select(item => item.PasswordHash)
            .SingleOrDefaultAsync(cancellationToken);
        return hash is not null && PasswordHash.Verify(password, hash);
    }

    public async Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        return user is null ? null : new(user.Id, user.UserName, user.Role.ToString(), user.TokenVersion);
    }

    private static class PasswordHash
    {
        public static bool Verify(string password, string encoded)
        {
            try
            {
                var parts = encoded.Split('.', 3);
                if (parts.Length != 3) return false;
                var salt = Convert.FromBase64String(parts[1]);
                var expected = Convert.FromBase64String(parts[2]);
                var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 120_000, HashAlgorithmName.SHA256, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException) { return false; }
        }
    }

}
