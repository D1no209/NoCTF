using System.Text.RegularExpressions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;

namespace NoCTF.Tests.Architecture;

public sealed partial class TargetArchitectureRulesTests
{
    private static readonly string BackendRoot = FindBackendRoot();

    [Test]
    public async Task Removed_domain_models_are_absent()
    {
        var domain = typeof(Competition).Assembly;
        var removedNames = new[]
        {
            "Penetration",
            "RuntimeOperation",
            "ChallengeInstance",
            "FixSubmissionRecord",
            "CompetitionCollaborator",
            "TeamMember",
            "AuditEntry"
        };
        var violations = domain.GetTypes()
            .Where(type => removedNames.Any(name =>
                type.Name.Contains(name, StringComparison.Ordinal)))
            .Select(type => type.FullName)
            .ToArray();

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Source_has_no_channel_queue_or_linq_query_syntax()
    {
        var files = Directory.EnumerateFiles(
                Path.Combine(BackendRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
                && !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            .ToArray();
        var violations = new List<string>();
        foreach (var file in files)
        {
            var source = await File.ReadAllTextAsync(file);
            if (source.Contains("System.Threading.Channels", StringComparison.Ordinal)
                || source.Contains("Task.Run(", StringComparison.Ordinal)
                || source.Contains("RequiredMappingStrategy.Both", StringComparison.Ordinal)
                || source.Contains("HandleAsync(", StringComparison.Ordinal)
                || LegacyDimensionRegex().IsMatch(source)
                || QuerySyntaxRegex().IsMatch(source))
                violations.Add(Path.GetRelativePath(BackendRoot, file));
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Api_has_no_shared_models_files_or_provider_project_references()
    {
        var apiRoot = Path.Combine(BackendRoot, "src", "NoCTF.API");
        var modelFiles = Directory.EnumerateFiles(
                apiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).EndsWith(
                "Models.cs", StringComparison.Ordinal)
                || Path.GetFileName(path).Equals("Dtos.cs", StringComparison.Ordinal)
                || Path.GetFileName(path).Equals("CommonModels.cs", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(BackendRoot, path))
            .ToArray();
        var project = await File.ReadAllTextAsync(
            Path.Combine(apiRoot, "NoCTF.API.csproj"));

        await Assert.That(modelFiles).IsEmpty();
        await Assert.That(project).DoesNotContain("NoCTF.Runtime.Docker");
        await Assert.That(project).DoesNotContain("NoCTF.Runtime.Kubernetes");
        await Assert.That(project).DoesNotContain("NoCTF.Runtime.Libvirt");
    }

    [Test]
    public async Task Runtime_provider_and_game_mode_catalogs_are_closed_enums()
    {
        await Assert.That(Enum.GetValues<GameMode>())
            .IsEquivalentTo([GameMode.Ctf, GameMode.Awd, GameMode.Awdp, GameMode.Koh]);
        await Assert.That(Enum.GetValues<RuntimeProvider>())
            .IsEquivalentTo([
                RuntimeProvider.Docker,
                RuntimeProvider.Kubernetes,
                RuntimeProvider.Libvirt]);
    }

    [Test]
    public async Task Initial_baseline_has_only_restrict_foreign_keys_and_no_legacy_schema()
    {
        var migrationRoot = Path.Combine(
            BackendRoot, "src", "NoCTF.Infrastructure", "Migrations");
        var baselines = Directory.EnumerateFiles(
                migrationRoot, "*_InitialBaseline.cs", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(baselines).Count().IsEqualTo(1);
        var migration = await File.ReadAllTextAsync(baselines[0]);
        await Assert.That(migration).DoesNotContain("ReferentialAction.Cascade");
        foreach (var removedTable in new[]
                 {
                     "runtime_operations",
                     "competition_collaborators",
                     "team_members",
                     "challenge_instances",
                     "fix_submission_records"
                 })
            await Assert.That(migration).DoesNotContain($"name: \"{removedTable}\"");
    }

    private static string FindBackendRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src"))
                && File.Exists(Path.Combine(current.FullName, "NoCTF.slnx")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the backend repository root.");
    }

    [GeneratedRegex(@"\b(from|where|select|join)\s+[A-Za-z_][A-Za-z0-9_]*\s+in\b")]
    private static partial Regex QuerySyntaxRegex();

    [GeneratedRegex(@"\b(ServiceId|StageId|RuntimeOperationId|ChallengeInstanceId)\b")]
    private static partial Regex LegacyDimensionRegex();
}
