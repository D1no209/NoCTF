using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Identity.Passkeys;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication.Passkeys;

// Bridges only the account reads required by the official verifier. No Identity account lifecycle replaces NoCTF use cases.
public sealed class PasskeyIdentityStore(NoCtfDbContext db) : IUserPasskeyStore<User>
{
    public void Dispose() { }
    public Task<string> GetUserIdAsync(User user, CancellationToken ct) => Task.FromResult(user.Id.ToString("N"));
    public Task<string?> GetUserNameAsync(User user, CancellationToken ct) => Task.FromResult<string?>(user.UserName);
    public Task<string?> GetNormalizedUserNameAsync(User user, CancellationToken ct) => Task.FromResult<string?>(user.NormalizedUserName);
    public Task<User?> FindByIdAsync(string id, CancellationToken ct) => Guid.TryParse(id, out var value)
        ? db.Users.SingleOrDefaultAsync(user => user.Id == value && user.Kind == UserKind.Human && user.AccountStatus == UserAccountStatus.Active, ct)
        : Task.FromResult<User?>(null);
    public Task<User?> FindByNameAsync(string normalized, CancellationToken ct) => db.Users.SingleOrDefaultAsync(user =>
        user.NormalizedUserName == normalized && user.Kind == UserKind.Human && user.AccountStatus == UserAccountStatus.Active, ct);
    public Task<User?> FindByPasskeyIdAsync(byte[] id, CancellationToken ct) => db.UserPasskeys.Where(value => value.CredentialId == id).Select(value => value.User).SingleOrDefaultAsync(ct);
    public async Task<IList<UserPasskeyInfo>> GetPasskeysAsync(User user, CancellationToken ct) =>
        (await db.UserPasskeys.Where(value => value.UserId == user.Id).ToArrayAsync(ct)).Select(ToIdentity).ToList();
    public async Task<UserPasskeyInfo?> FindPasskeyAsync(User user, byte[] id, CancellationToken ct)
    {
        var value = await db.UserPasskeys.SingleOrDefaultAsync(value => value.UserId == user.Id && value.CredentialId == id, ct);
        return value is null ? null : ToIdentity(value);
    }
    public Task SetUserNameAsync(User user, string? name, CancellationToken ct) => throw Unsupported();
    public Task SetNormalizedUserNameAsync(User user, string? name, CancellationToken ct) => throw Unsupported();
    public Task<IdentityResult> CreateAsync(User user, CancellationToken ct) => throw Unsupported();
    public Task<IdentityResult> UpdateAsync(User user, CancellationToken ct) => throw Unsupported();
    public Task<IdentityResult> DeleteAsync(User user, CancellationToken ct) => throw Unsupported();
    public Task AddOrUpdatePasskeyAsync(User user, UserPasskeyInfo passkey, CancellationToken ct) => throw Unsupported();
    public Task RemovePasskeyAsync(User user, byte[] id, CancellationToken ct) => throw Unsupported();
    private static InvalidOperationException Unsupported() => new("Account and credential changes must use the NoCTF Application use cases.");
    private static UserPasskeyInfo ToIdentity(UserPasskey value) => new(value.CredentialId, value.PublicKey, value.CreatedAt, value.SignCount,
        value.Transports.OrderBy(item => item.Position).Select(item => PasskeyProtocol.TransportText(item.Transport)).ToArray(),
        value.IsUserVerified, value.IsBackupEligible, value.IsBackedUp, value.AttestationObject, value.ClientDataJson) { Name = value.Name };
}
