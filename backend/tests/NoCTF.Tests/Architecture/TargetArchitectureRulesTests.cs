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
                    StringComparison.Ordinal)
                && !path.Contains(
                    $"{Path.DirectorySeparatorChar}Internal{Path.DirectorySeparatorChar}Generated{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            .ToArray();
        var violations = new List<string>();
        foreach (var file in files)
        {
            var source = await File.ReadAllTextAsync(file);
            // The bounded, best-effort log relay is not a durable business queue.
            if (source.Contains("System.Threading.Channels", StringComparison.Ordinal)
                    && !file.EndsWith($"{Path.DirectorySeparatorChar}PlatformLogBroadcastQueue.cs",
                        StringComparison.Ordinal)
                || source.Contains("Task.Run(", StringComparison.Ordinal)
                || Regex.IsMatch(source, @"\bHandleAsync\(")
                || LegacyDimensionRegex().IsMatch(source)
                || QuerySyntaxRegex().IsMatch(source))
                violations.Add(Path.GetRelativePath(BackendRoot, file));
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Protocol_enums_do_not_round_trip_through_runtime_strings()
    {
        var apiRoot = Path.Combine(BackendRoot, "src", "NoCTF.API");
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains(
                         $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                         StringComparison.Ordinal)))
        {
            if (EnumStringRoundTripRegex().IsMatch(await File.ReadAllTextAsync(file)))
                violations.Add(Path.GetRelativePath(BackendRoot, file));
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Business_code_uses_TimeProvider_instead_of_system_clock_reads()
    {
        var files = EnumerateProjectSourceFiles(
                "NoCTF.Application",
                "NoCTF.GameModes",
                "NoCTF.Infrastructure",
                "NoCTF.Runtime.Docker",
                "NoCTF.Runtime.Kubernetes",
                "NoCTF.Runner",
                "NoCTF.Worker")
            .Concat(Directory.EnumerateFiles(
                Path.Combine(BackendRoot, "src", "NoCTF.API", "Endpoints"),
                "*.cs",
                SearchOption.AllDirectories))
            .ToArray();
        var violations = new List<string>();
        foreach (var file in files)
        {
            var source = await File.ReadAllTextAsync(file);
            if (source.Contains("DateTimeOffset.UtcNow", StringComparison.Ordinal)
                || source.Contains("DateTime.UtcNow", StringComparison.Ordinal))
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
    public async Task Application_and_infrastructure_are_organized_by_capability()
    {
        var forbiddenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Ports",
            "UseCaseAdapters",
            "Services",
            "Helpers"
        };
        var violations = new[] { "NoCTF.Application", "NoCTF.Infrastructure" }
            .SelectMany(project => Directory.EnumerateDirectories(
                Path.Combine(BackendRoot, "src", project), "*", SearchOption.AllDirectories))
            .Where(path => forbiddenDirectories.Contains(Path.GetFileName(path)))
            .Select(path => Path.GetRelativePath(BackendRoot, path))
            .ToArray();

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Source_has_no_removed_horizontal_namespaces()
    {
        var files = EnumerateProjectSourceFiles("NoCTF.Application", "NoCTF.Infrastructure");
        var violations = new List<string>();
        foreach (var file in files)
        {
            var source = await File.ReadAllTextAsync(file);
            if (source.Contains(".Ports", StringComparison.Ordinal)
                || source.Contains(".Persistence.UseCaseAdapters", StringComparison.Ordinal))
                violations.Add(Path.GetRelativePath(BackendRoot, file));
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Infrastructure_types_are_named_for_their_business_responsibility()
    {
        var files = EnumerateProjectSourceFiles("NoCTF.Infrastructure");
        var violations = files
            .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith("Ef", StringComparison.Ordinal)
                || EfTypeNameRegex().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(BackendRoot, path))
            .ToArray();

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Infrastructure_composition_root_only_composes_capability_registrations()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            BackendRoot, "src", "NoCTF.Infrastructure", "ServiceRegistration.cs"));

        await Assert.That(source).DoesNotContain("services.AddScoped");
        await Assert.That(source).DoesNotContain("services.AddSingleton");
        await Assert.That(source).DoesNotContain("services.AddTransient");
        await Assert.That(source).DoesNotContain("services.AddHttpClient");
        await Assert.That(source).DoesNotContain("services.AddDbContext");
    }

    [Test]
    public async Task Runtime_provider_and_game_mode_catalogs_are_closed_enums()
    {
        await Assert.That(Enum.GetValues<GameMode>())
            .IsEquivalentTo([GameMode.Ctf, GameMode.Awd, GameMode.Awdp, GameMode.Koh, GameMode.LiveSolo]);
        await Assert.That(Enum.GetValues<RuntimeProvider>())
            .IsEquivalentTo([
                RuntimeProvider.Docker,
                RuntimeProvider.Kubernetes,
                RuntimeProvider.Libvirt]);
    }

    [Test]
    public async Task Initial_baseline_is_provider_scoped_relational_and_has_no_legacy_storage()
    {
        var migrationRoot = Path.Combine(
            BackendRoot, "src", "NoCTF.Persistence.PostgreSql", "Migrations");
        var baselines = Directory.EnumerateFiles(
                migrationRoot, "*_InitialBaseline.cs", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(baselines).Count().IsEqualTo(1);
        var migration = await File.ReadAllTextAsync(baselines[0]);
        await Assert.That(migration).DoesNotContain("jsonb");
        await Assert.That(migration).DoesNotContain("text[]");
        await Assert.That(migration).DoesNotContain("uuid[]");
        await Assert.That(migration).DoesNotContain("filter:");
        await Assert.That(migration).Contains("competition_collaborators");
        await Assert.That(migration).Contains("team_members");
        await Assert.That(migration).Contains("challenge_definitions");
        await Assert.That(migration).Contains("competition_challenge_rules");
        foreach (var removedTable in new[]
                 {
                     "runtime_operations",
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

    private static string[] EnumerateProjectSourceFiles(params string[] projects) =>
        projects
            .SelectMany(project => Directory.EnumerateFiles(
                Path.Combine(BackendRoot, "src", project), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
                && !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)
                && !path.Contains(
                    $"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            .ToArray();

    [GeneratedRegex(@"\b(from|where|select|join)\s+[A-Za-z_][A-Za-z0-9_]*\s+in\b")]
    private static partial Regex QuerySyntaxRegex();

    [GeneratedRegex(@"\b(ServiceId|StageId|RuntimeOperationId|ChallengeInstanceId)\b")]
    private static partial Regex LegacyDimensionRegex();

    [GeneratedRegex(@"Enum\.Parse<[^>]+>\([^\r\n;]*\.ToString\(\)\)")]
    private static partial Regex EnumStringRoundTripRegex();

    [GeneratedRegex(@"\b(class|record|struct)\s+Ef[A-Z][A-Za-z0-9_]*\b")]
    private static partial Regex EfTypeNameRegex();
}
