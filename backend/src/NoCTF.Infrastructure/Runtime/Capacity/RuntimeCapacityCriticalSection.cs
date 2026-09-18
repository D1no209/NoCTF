using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Runtime.Capacity;

/// <summary>Short allocation/rebuild transactions only; never held across provider operations.</summary>
public static class RuntimeCapacityCriticalSection
{
    public static Task AcquireAsync(NoCtfDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"noctf:runtime-capacity"}, 0))", ct);
}
