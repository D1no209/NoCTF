using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Challenges;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Architecture;

public sealed class CompetitionChallengePersistenceRulesTests
{
    [Test]
    public async Task Aggregate_children_have_restrict_foreign_keys_and_no_independent_dbsets()
    {
        await using var db = new NoCtfDbContext(new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql("Host=localhost;Database=noctf_model_tests;Username=noctf;Password=noctf")
            .Options);

        var competitionChallenge = db.Model.FindEntityType(typeof(CompetitionChallenge));
        await Assert.That(competitionChallenge).IsNotNull();
        await Assert.That(competitionChallenge!.GetForeignKeys()
            .Any(key => key.Properties.Single().Name == nameof(CompetitionChallenge.CompetitionId)
                        && key.PrincipalEntityType.ClrType.Name == "Competition")).IsTrue();
        await Assert.That(competitionChallenge.GetForeignKeys()
            .Any(key => key.Properties.Single().Name == nameof(CompetitionChallenge.ChallengeId)
                        && key.PrincipalEntityType.ClrType == typeof(Challenge))).IsTrue();
        var hintType = db.Model.FindEntityType(typeof(CompetitionChallengeHint));
        await Assert.That(hintType).IsNotNull();
        await Assert.That(hintType!.GetForeignKeys().Single().DeleteBehavior)
            .IsEqualTo(DeleteBehavior.Restrict);
        await Assert.That(typeof(NoCtfDbContext).GetProperties()
            .Any(property => property.PropertyType == typeof(DbSet<CompetitionChallengeHint>)))
            .IsFalse();
        var attachmentType = db.Model.FindEntityType(typeof(ChallengeAttachment));
        await Assert.That(attachmentType).IsNotNull();
        await Assert.That(attachmentType!.GetForeignKeys().Single().DeleteBehavior)
            .IsEqualTo(DeleteBehavior.Restrict);
        await Assert.That(typeof(NoCtfDbContext).GetProperties()
            .Any(property => property.PropertyType == typeof(DbSet<ChallengeAttachment>)))
            .IsFalse();
    }
}
