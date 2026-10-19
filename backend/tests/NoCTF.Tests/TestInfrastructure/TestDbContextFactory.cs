using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests;

internal sealed class TestDbContextFactory(DbContextOptions<NoCtfDbContext> options)
    : IDbContextFactory<NoCtfDbContext>
{
    public TestDbContextFactory(NoCtfDbContext db)
        : this((DbContextOptions<NoCtfDbContext>)db.GetService<IDbContextOptions>()) { }

    public NoCtfDbContext CreateDbContext() => new(options);

    public Task<NoCtfDbContext> CreateDbContextAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new NoCtfDbContext(options));
    }
}
