using System.Text.Json;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class GitOpsTemplateFixtureTests
{
    [Test]
    [Arguments("Ctf/None")]
    [Arguments("Ctf/Container")]
    [Arguments("Ctf/Compose")]
    [Arguments("Awd/Container")]
    [Arguments("Awd/Compose")]
    [Arguments("Awdp/Container")]
    [Arguments("Awdp/CheckerFixInput")]
    [Arguments("Koh/Container")]
    public async Task Materialized_template_matches_platform_configuration_contract(string scenario)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NoCTF.slnx")))
            directory = directory.Parent;
        if (directory is null) throw new DirectoryNotFoundException("NoCTF backend root was not found.");
        var path = Path.Combine(directory.FullName, "tests", "NoCTF.Tests", "Fixtures", "GitOps", "template-configurations.json");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var fixture = json.RootElement.EnumerateArray().Single(item => item.GetProperty("scenario").GetString() == scenario);
        var mode = Enum.Parse<GameMode>(fixture.GetProperty("mode").GetString()!);
        var catalog = new GameModeChallengeConfigurationCatalog();

        await Assert.That(catalog.ValidateDefinition(mode, fixture.GetProperty("definition").GetRawText())).IsEmpty();
        await Assert.That(catalog.ValidateRules(mode, fixture.GetProperty("rules").GetRawText(),
            GameModeDefaultConfiguration.GetCompetitionJson(mode), 1)).IsEmpty();
    }
}
