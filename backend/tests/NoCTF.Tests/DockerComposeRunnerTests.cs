using NoCTF.Container.Docker;

namespace NoCTF.Tests;

public class DockerComposeRunnerTests
{
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
}
