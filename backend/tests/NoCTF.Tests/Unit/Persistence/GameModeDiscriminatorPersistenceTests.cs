using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Unit.Persistence;

public sealed class GameModeDiscriminatorPersistenceTests
{
    [Test]
    public async Task Mode_hierarchies_use_stable_string_discriminators()
    {
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        await using var db = new NoCtfDbContext(options);
        var roots = new[]
        {
            typeof(Competition),
            typeof(CompetitionModeConfiguration),
            typeof(Challenge),
            typeof(CompetitionChallenge),
            typeof(ChallengeDefinition),
            typeof(CompetitionChallengeRules)
        };
        var expected = new Dictionary<GameMode, string>
        {
            [GameMode.Ctf] = "ctf",
            [GameMode.Awd] = "awd",
            [GameMode.Awdp] = "awdp",
            [GameMode.Koh] = "koh",
            [GameMode.LiveSolo] = "livesolo"
        };

        foreach (var rootType in roots)
        {
            var entity = db.Model.FindEntityType(rootType)
                ?? throw new InvalidOperationException($"Missing EF hierarchy {rootType.Name}.");
            var property = entity.FindDiscriminatorProperty()
                ?? throw new InvalidOperationException($"Missing discriminator for {rootType.Name}.");
            await Assert.That(property.Name).IsEqualTo(nameof(Competition.Mode));
            await Assert.That(property.GetMaxLength()).IsEqualTo(8);
            var converter = property.GetValueConverter()
                ?? throw new InvalidOperationException($"Missing mode converter for {rootType.Name}.");
            foreach (var (mode, value) in expected)
            {
                await Assert.That(converter.ConvertToProvider(mode)).IsEqualTo(value);
                await Assert.That(converter.ConvertFromProvider(value)).IsEqualTo(mode);
            }
        }
    }
}
