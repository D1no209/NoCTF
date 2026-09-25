using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests;

internal sealed class TestDbContextFactory(DbContextOptions<NoCtfDbContext> options)
    : IDbContextFactory<NoCtfDbContext>
{
    public NoCtfDbContext CreateDbContext() => new(options);

    public Task<NoCtfDbContext> CreateDbContextAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new NoCtfDbContext(options));
    }
}
