using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Hosting.Observability;

namespace NoCTF.Tests.Architecture;

public sealed class ComposeDeploymentContractTests
{
    [Test]
    public async Task Compose_renders_five_services_with_directory_mounts_and_scoped_environment_files()
    {
        var repository = FindRepositoryRoot();
        var source = Path.Combine(repository, "deploy");
        var root = Path.Combine(Path.GetTempPath(), "noctf-compose-contract", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var values = new Dictionary<string, string>
            {
                ["NOCTF_PLATFORM_IMAGE"] = "ghcr.io/d1no209/noctf@sha256:"+new string('1', 64),
                ["POSTGRES_PASSWORD"] = new string('p', 32), ["JWT_SECRET"] = new string('j', 64),
                ["RUNNER_SCORING_SECRET"] = new string('r', 64), ["REGISTRY_HTTP_SECRET"] = new string('s', 64),
                ["EMAIL_VERIFICATION_ENCRYPTION_KEY"] = Convert.ToBase64String(new byte[32]),
                ["SEED_ADMIN_EMAIL"] = "admin@example.test", ["SEED_ADMIN_PASSWORD"] = "test-password-1234",
                ["NOCTF_PUBLIC_HOST"] = "noctf.example.test", ["NOCTF_PUBLIC_URL"] = "https://noctf.example.test",
                ["NOCTF_PROXY_NETWORK"] = "172.20.0.0/16", ["DOCKER_PUBLISHED_HOST"] = "challenges.example.test"
            };
            var lines = await File.ReadAllLinesAsync(Path.Combine(source, ".env.example"));
            await File.WriteAllLinesAsync(Path.Combine(root, ".env"), lines.Select(line =>
            {
                var equals = line.IndexOf('=');
                return equals > 0 && values.TryGetValue(line[..equals], out var value) ? line[..(equals + 1)] + value : line;
            }));
            foreach (var service in new[] { "noctf", "postgres", "registry" })
            {
                var target = Path.Combine(root, "env", service);
                Directory.CreateDirectory(target);
                File.Copy(Path.Combine(source, "env", service, ".env.example"), Path.Combine(target, ".env"));
            }
            var start = new ProcessStartInfo("docker") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var key in values.Keys.Concat(["COMPOSE_FILE", "COMPOSE_PROJECT_NAME"])) start.Environment.Remove(key);
            foreach (var argument in new[] { "compose", "--project-directory", root, "--env-file", Path.Combine(root, ".env"),
                "--file", Path.Combine(source, "docker-compose.yml"), "config", "--format", "json" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Docker Compose CLI is required for this contract test (the daemon is not used).");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Assert.That(process.ExitCode).IsEqualTo(0);
            _ = await error;
            using var json = JsonDocument.Parse(await output);
            var services = json.RootElement.GetProperty("services");
            await Assert.That(services.EnumerateObject().Select(item => item.Name))
                .IsEquivalentTo(["noctf", "postgres", "redis", "nats", "registry"]);
            foreach (var service in services.EnumerateObject())
            {
                foreach (var forbidden in new[] { "ports", "expose", "build", "healthcheck", "tmpfs", "group_add", "cap_add", "cap_drop", "security_opt", "read_only" })
                    await Assert.That(service.Value.TryGetProperty(forbidden, out _)).IsFalse();
                foreach (var volume in service.Value.GetProperty("volumes").EnumerateArray())
                    await Assert.That(volume.GetProperty("type").GetString()).IsEqualTo("bind");
            }
            await Assert.That(json.RootElement.GetProperty("networks").GetProperty("panel").GetProperty("name").GetString()).IsEqualTo("1panel-network");
            await Assert.That(services.GetProperty("registry").GetProperty("networks").GetProperty("panel").GetProperty("aliases")
                .EnumerateArray().Select(item => item.GetString())).Contains("noctf-registry");
            await Assert.That(services.GetProperty("registry").GetProperty("networks").GetProperty("default").GetProperty("aliases")
                .EnumerateArray().Select(item => item.GetString())).Contains("registry");
            await Assert.That(services.GetProperty("postgres").GetProperty("environment").TryGetProperty("Authentication__SigningKey", out _)).IsFalse();
            await Assert.That(services.GetProperty("registry").GetProperty("environment").TryGetProperty("POSTGRES_PASSWORD", out _)).IsFalse();
            await Assert.That(services.GetProperty("noctf").GetProperty("environment").GetProperty("Database__AutoMigrate").GetString()).IsEqualTo("true");
            await Assert.That(services.GetProperty("noctf").GetProperty("environment").GetProperty("Observability__Enabled").GetString()).IsEqualTo("false");
            var raw = await File.ReadAllTextAsync(Path.Combine(source, "docker-compose.yml"));
            await Assert.That(raw).DoesNotContain("environment:");
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task Disabled_telemetry_registers_no_exporter_or_instrumentation_services()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Observability:Enabled"] = "false" }).Build();
        var services = new ServiceCollection();
        services.AddNoCtfObservability(configuration, "disabled-test");
        await Assert.That(services).IsEmpty();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "deploy", "docker-compose.yml")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("NoCTF repository was not found.");
    }
}
