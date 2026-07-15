using NoCTF.Container.Docker;
using YamlDotNet.RepresentationModel;

namespace NoCTF.Tests;

public class DockerComposeRunnerTests
{
    [Theory]
    [InlineData("DOCKER_HOST")]
    [InlineData("COMPOSE_FILE")]
    [InlineData("PATH")]
    [InlineData("LD_PRELOAD")]
    [InlineData("HTTPS_PROXY")]
    public void ValidateProcessEnvironment_RejectsRunnerControlVariables(string key)
    {
        Assert.Throws<InvalidOperationException>(() =>
            DockerComposeRunner.ValidateProcessEnvironment(
                new Dictionary<string, string> { [key] = "attacker-controlled" }));
    }

    [Fact]
    public void ValidateProcessEnvironment_AllowsChallengeVariables()
    {
        DockerComposeRunner.ValidateProcessEnvironment(
            new Dictionary<string, string> { ["NOCTF_CHALLENGE_MODE"] = "production" });
    }

    [Fact]
    public async Task ReadBoundedOutputAsync_DrainsButRetainsOnlyConfiguredBytes()
    {
        var payload = new byte[DockerComposeRunner.MaxCapturedBytesPerStream + 32_768];
        Array.Fill(payload, (byte)'x');
        await using var stream = new MemoryStream(payload, writable: false);

        var result = await DockerComposeRunner.ReadBoundedOutputAsync(
            stream,
            DockerComposeRunner.MaxCapturedBytesPerStream);

        Assert.True(result.Truncated);
        Assert.Equal(DockerComposeRunner.MaxCapturedBytesPerStream, result.CapturedBytes);
        Assert.EndsWith("[output truncated]", result.Text, StringComparison.Ordinal);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void SanitizeFailureDiagnostic_RedactsSecretsAndBoundsErrorText()
    {
        const string secret = "super-secret-runner-token";
        var diagnostic = $"token={secret}\npostgres://user:password@db/noctf\n" +
                         new string('e', DockerComposeRunner.MaxFailureDiagnosticCharacters * 2);

        var result = DockerComposeRunner.SanitizeFailureDiagnostic(diagnostic, [secret]);

        Assert.DoesNotContain(secret, result, StringComparison.Ordinal);
        Assert.DoesNotContain("user:password", result, StringComparison.Ordinal);
        Assert.Contains("[redacted]", result, StringComparison.Ordinal);
        Assert.EndsWith("[diagnostic truncated]", result, StringComparison.Ordinal);
        Assert.True(result.Length < DockerComposeRunner.MaxFailureDiagnosticCharacters + 64);
    }

    [Fact]
    public void SanitizeFailureDiagnostic_RedactsEntireSensitiveAssignmentLine()
    {
        const string diagnostic = "runner failed\nAuthorization: Bearer token with spaces and details\nsafe diagnostic";

        var result = DockerComposeRunner.SanitizeFailureDiagnostic(diagnostic, null);

        Assert.DoesNotContain("Bearer", result, StringComparison.Ordinal);
        Assert.DoesNotContain("with spaces and details", result, StringComparison.Ordinal);
        Assert.Contains("Authorization: [redacted]", result, StringComparison.Ordinal);
        Assert.Contains("safe diagnostic", result, StringComparison.Ordinal);
    }

    [Fact]
    public void DeleteTemporaryFileBestEffort_DoesNotThrowWhenPathCannotBeDeletedAsAFile()
    {
        var directory = Directory.CreateTempSubdirectory("noctf-compose-cleanup-");
        try
        {
            DockerComposeRunner.DeleteTemporaryFileBestEffort(directory.FullName);

            Assert.True(directory.Exists);
        }
        finally
        {
            directory.Delete();
        }
    }

    [Fact]
    public void ValidateComposeYaml_AllowsBasicService()
    {
        var yaml = """
services:
  web:
    image: registry/challenge:latest
    security_opt:
      - no-new-privileges:true
    cap_drop:
      - ALL
    user: "1000:1000"
    read_only: true
    pids_limit: 128
    deploy:
      resources:
        limits:
          cpus: "0.50"
          memory: "256M"
""";

        DockerComposeRunner.ValidateComposeYaml(yaml);
    }

    [Fact]
    public void ApplyRuntimeLabels_OverridesForgedLabelsOnServicesAndOwnedResources()
    {
        var yaml = """
services:
  web:
    image: registry/challenge:latest
    labels:
      - noctf.managed=False
      - challenge-label=preserved
    volumes:
      - challenge-data:/data
    security_opt:
      - no-new-privileges:true
    cap_drop:
      - ALL
    user: "1000:1000"
    read_only: true
    pids_limit: 128
    deploy:
      resources:
        limits:
          cpus: "0.50"
          memory: "256M"
volumes:
  challenge-data:
""";
        var labels = new Dictionary<string, string>
        {
            [DockerProvider.ManagedLabel] = bool.TrueString,
            [DockerProvider.ComposeProjectLabel] = "noctf-compose-test",
            [DockerProvider.RequestFingerprintLabel] = new string('a', 64),
            ["competitionId"] = Guid.NewGuid().ToString("D")
        };

        var effective = DockerComposeRunner.ApplyRuntimeLabels(yaml, labels);

        DockerComposeRunner.ValidateComposeYaml(effective);
        var stream = new YamlStream();
        stream.Load(new StringReader(effective));
        var root = Assert.IsType<YamlMappingNode>(stream.Documents[0].RootNode);
        var service = ChildMapping(ChildMapping(root, "services"), "web");
        var volume = ChildMapping(ChildMapping(root, "volumes"), "challenge-data");
        var network = ChildMapping(ChildMapping(root, "networks"), "default");
        foreach (var owner in new[] { service, volume, network })
        {
            var actual = ChildMapping(owner, "labels");
            Assert.Equal(bool.TrueString, Scalar(actual, DockerProvider.ManagedLabel));
            Assert.Equal("noctf-compose-test", Scalar(actual, DockerProvider.ComposeProjectLabel));
            Assert.Equal(new string('a', 64), Scalar(actual, DockerProvider.RequestFingerprintLabel));
        }
        Assert.Equal("preserved", Scalar(ChildMapping(service, "labels"), "challenge-label"));
        Assert.DoesNotContain("noctf.managed: False", effective, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateComposeYaml_AllowsNoctfOrchestrationExtension()
    {
        var yaml = """
services:
  web:
    image: registry/challenge:latest
    x-noctf-orchestration: |-
      {"kubernetes":{"labels":{"range":"built-in"}}}
    security_opt:
      - no-new-privileges:true
    cap_drop:
      - ALL
    user: "1000:1000"
    read_only: true
    pids_limit: 128
    deploy:
      resources:
        limits:
          cpus: "0.50"
          memory: "256M"
""";

        DockerComposeRunner.ValidateComposeYaml(yaml);
    }

    [Theory]
    [InlineData("00")]
    [InlineData("+0")]
    [InlineData("-0")]
    [InlineData("000:1000")]
    public void ValidateComposeYaml_RejectsNumericRootUserVariants(string user)
    {
        var yaml = $"""
services:
  web:
    image: registry/challenge:latest
    security_opt:
      - no-new-privileges:true
    cap_drop:
      - ALL
    user: "{user}"
    read_only: true
    pids_limit: 128
    deploy:
      resources:
        limits:
          cpus: "0.50"
          memory: "256M"
""";

        Assert.Throws<InvalidOperationException>(() => DockerComposeRunner.ValidateComposeYaml(yaml));
    }

    [Fact]
    public void ValidateComposeYaml_AllowsNoNewPrivilegesSecurityOpt()
    {
        var yaml = """
services:
  web:
    image: registry/challenge:latest
    security_opt:
      - no-new-privileges:true
    cap_drop:
      - ALL
    user: "1000:1000"
    read_only: true
    pids_limit: 128
    deploy:
      resources:
        limits:
          cpus: "0.50"
          memory: "256M"
""";

        DockerComposeRunner.ValidateComposeYaml(yaml);
    }

    [Theory]
    [InlineData("privileged:")]
    [InlineData("network_mode: host")]
    [InlineData("/var/run/docker.sock")]
    [InlineData("devices:")]
    [InlineData("build: .")]
    [InlineData("env_file: .env")]
    [InlineData("networks: [host]")]
    [InlineData("container_name: challenge-web")]
    public void ValidateComposeYaml_RejectsForbiddenDirectives(string directive)
    {
        var yaml = $"""
services:
  web:
    image: registry/challenge:latest
    {directive} true
""";

        Assert.Throws<InvalidOperationException>(() => DockerComposeRunner.ValidateComposeYaml(yaml));
    }

    [Theory]
    [InlineData("external: true")]
    [InlineData("driver_opts:\n      type: none\n      device: /")]
    [InlineData("name: shared-volume")]
    public void ValidateComposeYaml_RejectsUnsafeTopLevelVolumes(string directive)
    {
        var yaml = $"""
services:
  web:
    image: registry/challenge:latest
    volumes:
      - challenge-data:/data
volumes:
  challenge-data:
    {directive}
""";

        Assert.Throws<InvalidOperationException>(() => DockerComposeRunner.ValidateComposeYaml(yaml));
    }

    [Theory]
    [InlineData("external: true")]
    [InlineData("ipam:\n      config:\n        - subnet: 172.16.0.0/16")]
    [InlineData("name: shared-network")]
    [InlineData("driver: macvlan")]
    public void ValidateComposeYaml_RejectsUnsafeTopLevelNetworks(string directive)
    {
        var yaml = $"""
services:
  web:
    image: registry/challenge:latest
networks:
  default:
    {directive}
""";

        Assert.Throws<InvalidOperationException>(() => DockerComposeRunner.ValidateComposeYaml(yaml));
    }

    [Fact]
    public void ValidateComposeYaml_RejectsVariableInterpolation()
    {
        var yaml = """
services:
  web:
    image: registry/challenge:latest
    environment:
      RUNNER_TOKEN: ${Runner__ApiKey}
    security_opt:
      - no-new-privileges:true
    cap_drop:
      - ALL
    user: "1000:1000"
    read_only: true
    pids_limit: 128
    deploy:
      resources:
        limits:
          cpus: "0.50"
          memory: "256M"
""";

        Assert.Throws<InvalidOperationException>(() => DockerComposeRunner.ValidateComposeYaml(yaml));
    }

    [Theory]
    [InlineData("3.0", "256M")]
    [InlineData("0.50", "3GiB")]
    [InlineData("not-a-number", "256M")]
    public void ValidateComposeYaml_RejectsInvalidResourceLimits(string cpus, string memory)
    {
        var yaml = $"""
services:
  web:
    image: registry/challenge:latest
    security_opt:
      - no-new-privileges:true
    cap_drop:
      - ALL
    user: "1000:1000"
    read_only: true
    pids_limit: 128
    deploy:
      resources:
        limits:
          cpus: "{cpus}"
          memory: "{memory}"
""";

        Assert.Throws<InvalidOperationException>(() => DockerComposeRunner.ValidateComposeYaml(yaml));
    }

    [Theory]
    [InlineData("seccomp=unconfined")]
    [InlineData("apparmor=unconfined")]
    [InlineData("no-new-privileges:false")]
    public void ValidateComposeYaml_RejectsUnsafeSecurityOptions(string option)
    {
        var yaml = $"""
services:
  web:
    image: registry/challenge:latest
    security_opt:
      - {option}
""";

        Assert.Throws<InvalidOperationException>(() => DockerComposeRunner.ValidateComposeYaml(yaml));
    }

    [Fact]
    public void ValidateComposeYaml_RejectsNestedHostNetworkMode()
    {
        var yaml = """
services:
  web:
    image: registry/challenge:latest
    network_mode:
      host
""";

        Assert.Throws<InvalidOperationException>(() => DockerComposeRunner.ValidateComposeYaml(yaml));
    }

    [Theory]
    [InlineData("/etc/passwd:/tmp/passwd:ro")]
    [InlineData("./data:/data")]
    [InlineData("C:\\\\ctf\\\\data:/data")]
    public void ValidateComposeYaml_RejectsHostPathVolumeMounts(string volume)
    {
        var yaml = $"""
services:
  web:
    image: registry/challenge:latest
    volumes:
      - "{volume}"
""";

        Assert.Throws<InvalidOperationException>(() => DockerComposeRunner.ValidateComposeYaml(yaml));
    }

    [Fact]
    public void ValidateComposeYaml_AllowsNamedVolumes()
    {
        var yaml = """
services:
  web:
    image: registry/challenge:latest
    volumes:
      - challenge-data:/data
    security_opt:
      - no-new-privileges:true
    cap_drop:
      - ALL
    user: "1000:1000"
    read_only: true
    pids_limit: 128
    deploy:
      resources:
        limits:
          cpus: "0.50"
          memory: "256M"
volumes:
  challenge-data:
""";

        DockerComposeRunner.ValidateComposeYaml(yaml);
    }

    private static YamlMappingNode ChildMapping(YamlMappingNode owner, string key)
        => Assert.IsType<YamlMappingNode>(owner.Children.Single(pair =>
            string.Equals(
                Assert.IsType<YamlScalarNode>(pair.Key).Value,
                key,
                StringComparison.OrdinalIgnoreCase)).Value);

    private static string Scalar(YamlMappingNode owner, string key)
        => Assert.IsType<YamlScalarNode>(owner.Children.Single(pair =>
            string.Equals(
                Assert.IsType<YamlScalarNode>(pair.Key).Value,
                key,
                StringComparison.OrdinalIgnoreCase)).Value).Value ?? string.Empty;
}
