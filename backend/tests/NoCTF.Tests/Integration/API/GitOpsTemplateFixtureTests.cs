using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Endpoints.Administration.Challenges;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Integration.API;

[Category("Integration")]
public sealed class GitOpsTemplateFixtureTests
{
    [Test]
    [Timeout(120_000)]
    public async Task Repository_exports_every_typed_definition_and_rules_shape(CancellationToken ct)
    {
        var templateRoot = Environment.GetEnvironmentVariable("NOCTF_GITOPS_TEMPLATE_ROOT");
        if (string.IsNullOrWhiteSpace(templateRoot))
        {
            Skip.Test("Set NOCTF_GITOPS_TEMPLATE_ROOT to the pinned challenge-template checkout.");
            return;
        }
        var script = Path.Combine(Path.GetFullPath(templateRoot), ".github", "scripts", "repository.cs");
        if (!File.Exists(script))
            throw new DirectoryNotFoundException("Challenge-template scripts were not found.");
        var output = Path.Combine(Path.GetTempPath(), "noctf-gitops-fixtures", $"{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        try
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = templateRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] { "run", "--file", script, "--", "contract-fixtures", "--output", output })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start the GitOps fixture exporter.");
            var standardOutput = process.StandardOutput.ReadToEndAsync(ct);
            var standardError = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"GitOps fixture export failed: {await standardOutput}{await standardError}");

            var fixtures = JsonNode.Parse(await File.ReadAllTextAsync(output, ct))!.AsArray();
            await Assert.That(fixtures.Select(item => item!["scenario"]!.GetValue<string>()).ToArray())
                .IsEquivalentTo([
                    "Ctf/None", "Ctf/Container", "Ctf/Services", "Awd/Container",
                    "Awd/Services", "Awdp/Container", "Koh/Container", "Awdp/CheckerFixInput"
                ]);
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            };
            foreach (var fixture in fixtures.Select(item => item!.AsObject()))
            {
                var mode = Enum.Parse<GameModeProtocol>(fixture["mode"]!.GetValue<string>());
                var definition = fixture["definition"]!.Deserialize<ChallengeDefinitionContract>(options)!;
                var rules = fixture["rules"]!.Deserialize<CompetitionChallengeRulesContract>(options)!;
                await Assert.That(ChallengeDefinitionContractMapper.HasValidShape(definition)).IsTrue();
                await Assert.That(CompetitionChallengeRulesContractMapper.HasValidShape(rules)).IsTrue();
                _ = ChallengeDefinitionContractMapper.ToDomain(
                    Guid.NewGuid(), (GameMode)mode, definition);
                _ = CompetitionChallengeRulesContractMapper.ToDomain(
                    Guid.NewGuid(), (GameMode)mode, rules);
            }
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }
}
