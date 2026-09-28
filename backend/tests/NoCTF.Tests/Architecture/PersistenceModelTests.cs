using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Architecture;

public sealed class PersistenceModelTests
{
    [Test]
    public async Task Mutable_aggregates_use_provider_neutral_concurrency_stamps()
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

        await Assert.That(concurrencyProperties).Contains("Competition.ConcurrencyStamp");
        await Assert.That(concurrencyProperties).Contains("Team.ConcurrencyStamp");
        await Assert.That(concurrencyProperties).Contains("RuntimeInstance.ConcurrencyStamp");
    }

    private static NoCtfDbContext CreateDb() => new(
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql("Host=localhost;Database=noctf_model_tests;Username=noctf;Password=noctf")
            .Options);
}
