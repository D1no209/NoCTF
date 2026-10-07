using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Hosting.Authentication;

internal static class OfflineMfaRecovery
{
    internal static async Task ExecuteAsync(IServiceProvider services, IConfiguration configuration, CancellationToken ct)
    {
        var identity = configuration["mfa-recovery-user"]?.Trim();
        var reason = configuration["mfa-recovery-reason"];
        if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Both --mfa-recovery-user and --mfa-recovery-reason are required.");
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        var byId = Guid.TryParse(identity, out var id); var normalized = identity.ToUpperInvariant();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(value => value.Kind == UserKind.Human && value.Role == UserRole.Administrator
            && (byId ? value.Id == id : value.NormalizedEmail == normalized), ct);
        if (user is null) throw new InvalidOperationException("An active human platform administrator is required.");
        var result = await scope.ServiceProvider.GetRequiredService<IMfaManagementStore>().GrantRecoveryAsync(null, null, user.Id, reason, ct);
        if (!result.Succeeded) throw new InvalidOperationException($"Restricted MFA recovery failed: {result.FailureCode}.");
        Console.WriteLine($"Restricted recovery authorization {result.Value!.Id:N} created; expires {result.Value.ExpiresAt:O}. The Worker will send the link to the verified mailbox.");
    }
}
