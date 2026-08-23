using System.Text.Json;
using NSubstitute;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.Application;

public sealed class ChallengeTemplateDefaultDefinitionTests
{
    [Test]
    public async Task Create_awdp_template_without_definition_uses_current_mode_default()
    {
        var store = Substitute.For<IChallengeBankStore>();
        CreateChallengeTemplateCommand? persisted = null;
        store.CreateAsync(
                Arg.Do<CreateChallengeTemplateCommand>(command => persisted = command),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChallengeTemplateWriteResult(
                ChallengeTemplateWriteState.Succeeded)));
        var useCase = new CreateChallengeTemplate(
            store,
            new GameModeChallengeConfigurationCatalog());

        var result = await useCase.ExecuteAsync(new(
            null,
            Guid.CreateVersion7(),
            GameMode.Awdp,
            ChallengeVisibility.Private,
            "AWDP template",
            null,
            "Pwn",
            string.Empty,
            DateTimeOffset.UtcNow));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(ReadSchemaVersion(persisted!.DefinitionJson))
            .IsEqualTo(AwdpChallengeConfiguration.CurrentSchemaVersion);
    }

    [Test]
    public async Task Update_awdp_template_without_definition_uses_current_mode_default()
    {
        var store = Substitute.For<IChallengeBankStore>();
        UpdateChallengeTemplateCommand? persisted = null;
        store.UpdateAsync(
                Arg.Do<UpdateChallengeTemplateCommand>(command => persisted = command),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChallengeTemplateWriteResult(
                ChallengeTemplateWriteState.Succeeded)));
        var useCase = new UpdateChallengeTemplate(
            store,
            new GameModeChallengeConfigurationCatalog());

        var result = await useCase.ExecuteAsync(new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            true,
            GameMode.Awdp,
            ChallengeVisibility.Private,
            "AWDP template",
            null,
            "Pwn",
            " ",
            DateTimeOffset.UtcNow));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(ReadSchemaVersion(persisted!.DefinitionJson))
            .IsEqualTo(AwdpChallengeConfiguration.CurrentSchemaVersion);
    }

    [Test]
    public async Task Domain_entities_do_not_silently_create_legacy_schema_versions()
    {
        await Assert.That(new Competition().ConfigurationJson).IsEmpty();
        await Assert.That(new Challenge().DefinitionJson).IsEmpty();
        await Assert.That(new CompetitionChallenge().RulesJson).IsEmpty();
    }

    private static int ReadSchemaVersion(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("schemaVersion").GetInt32();
    }
}
