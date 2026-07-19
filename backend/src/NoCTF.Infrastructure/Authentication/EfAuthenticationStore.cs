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

    public async Task<string> CreateRefreshTokenAsync(
        Guid userId,
        string? ipAddress,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var familyId = Guid.NewGuid();
        db.RefreshSessions.Add(new RefreshSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = Hash(token),
            CreatedAt = now,
            ExpiresAt = now.AddDays(7),
            CreatedByIp = ipAddress
        });
        await db.SaveChangesAsync(cancellationToken);
        return token;
    }

    public async Task<RefreshRotation?> RotateRefreshAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var session = await db.RefreshSessions.SingleOrDefaultAsync(
            item => item.TokenHash == tokenHash,
            cancellationToken);
        if (session is null) return null;
        if (session.ConsumedAt is not null || session.RevokedAt is not null || session.ExpiresAt <= now)
            return new(session.Id, session.UserId, session.FamilyId, string.Empty, session.ExpiresAt, true);

        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var replacement = new RefreshSession
        {
            Id = Guid.NewGuid(),
            UserId = session.UserId,
            FamilyId = session.FamilyId,
            TokenHash = Hash(refreshToken),
            CreatedAt = now,
            ExpiresAt = now.AddDays(7)
        };
        session.ConsumedAt = now;
        session.ReplacedBySessionId = replacement.Id;
        db.RefreshSessions.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(replacement.Id, replacement.UserId, replacement.FamilyId, refreshToken, replacement.ExpiresAt, false);
    }

    public Task RevokeRefreshFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshSessions.Where(item => item.FamilyId == familyId && item.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RevokedAt, now), cancellationToken);

    public async Task RevokeRefreshTokenAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var familyId = await db.RefreshSessions.AsNoTracking()
            .Where(item => item.TokenHash == tokenHash)
            .Select(item => (Guid?)item.FamilyId)
            .SingleOrDefaultAsync(cancellationToken);
        if (familyId is not null)
            await RevokeRefreshFamilyAsync(familyId.Value, now, cancellationToken);
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

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
