using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.DataExports;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Architecture;

public sealed class PersistenceConcurrencyModelTests
{
    [Test]
    public async Task Aggregate_versions_are_provider_neutral_concurrency_tokens()
    {
        await using var db = CreateDb();

        foreach (var entityType in new[]
                 {
                     typeof(Competition),
                     typeof(Challenge),
                     typeof(Team),
                     typeof(User),
                     typeof(CompetitionChallenge)
                 })
        {
            var property = db.Model.FindEntityType(entityType)!
                .FindProperty("ConcurrencyVersion");

            await Assert.That(property).IsNotNull();
            await Assert.That(property!.IsConcurrencyToken).IsTrue();
            await Assert.That(property.ValueGenerated)
                .IsEqualTo(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never);
        }
    }

    [Test]
    public async Task Active_data_export_slot_has_a_unique_index()
    {
        await using var db = CreateDb();
        var entity = db.Model.FindEntityType(typeof(DataExport))!;
        var index = entity.GetIndexes().Single(candidate =>
            candidate.Properties.Select(property => property.Name).SequenceEqual(
                ["RequestedByUserId", "Scope", "ActiveSlot"]));

        await Assert.That(index.IsUnique).IsTrue();
    }

    private static NoCtfDbContext CreateDb() => new(
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseInMemoryDatabase($"concurrency-model-{Guid.NewGuid():N}")
            .Options);
}
