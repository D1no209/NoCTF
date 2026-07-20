using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Challenges;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Architecture;

public sealed class CompetitionChallengePersistenceRulesTests
{
    [Test]
    public async Task Competition_challenge_has_template_and_competition_foreign_keys_and_owned_children()
    {
        await using var db = new NoCtfDbContext(new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

        var competitionChallenge = db.Model.FindEntityType(typeof(CompetitionChallenge));
        await Assert.That(competitionChallenge).IsNotNull();
        await Assert.That(competitionChallenge!.GetForeignKeys()
            .Any(key => key.Properties.Single().Name == nameof(CompetitionChallenge.CompetitionId)
                        && key.PrincipalEntityType.ClrType.Name == "Competition")).IsTrue();
        await Assert.That(competitionChallenge.GetForeignKeys()
            .Any(key => key.Properties.Single().Name == nameof(CompetitionChallenge.ChallengeId)
                        && key.PrincipalEntityType.ClrType == typeof(Challenge))).IsTrue();
        await Assert.That(db.Model.GetEntityTypes()
            .Any(type => type.ClrType == typeof(CompetitionChallengeHint) && type.IsOwned())).IsTrue();
        await Assert.That(db.Model.GetEntityTypes()
            .Any(type => type.ClrType == typeof(ChallengeAttachment) && type.IsOwned())).IsTrue();
    }
}
