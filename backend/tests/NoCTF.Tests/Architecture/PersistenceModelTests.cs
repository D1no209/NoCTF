using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Architecture;

public sealed class PersistenceModelTests
{
    [Test]
    public async Task Business_entities_use_last_write_wins()
    {
        await using var db = CreateDb();

        var concurrencyProperties = db.Model.GetEntityTypes()
            .Where(entityType => entityType.ClrType.Namespace?.StartsWith(
                "NoCTF.Domain",
                StringComparison.Ordinal) == true)
            .SelectMany(entityType => entityType.GetProperties()
                .Where(property => property.IsConcurrencyToken)
                .Select(property => $"{entityType.ClrType.Name}.{property.Name}"))
            .ToArray();

        await Assert.That(concurrencyProperties).IsEmpty();
    }

    private static NoCtfDbContext CreateDb() => new(
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql("Host=localhost;Database=noctf_model_tests;Username=noctf;Password=noctf")
            .Options);
}
