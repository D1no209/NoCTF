using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Administration;

public static class PlatformPublicAddress
{
    public static async Task<Uri> ReadAsync(NoCtfDbContext db, CancellationToken ct)
    {
        var value = await db.PlatformSettings.AsNoTracking().Where(settings => settings.Id == 1)
            .Select(settings => settings.PublicBaseUrl).SingleAsync(ct);
        return new Uri(value.TrimEnd('/') + "/", UriKind.Absolute);
    }
}
