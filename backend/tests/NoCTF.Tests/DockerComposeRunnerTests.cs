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
""";

        DockerComposeRunner.ValidateComposeYaml(yaml);
    }

    [Theory]
    [InlineData("privileged:")]
    [InlineData("network_mode: host")]
    [InlineData("/var/run/docker.sock")]
    [InlineData("devices:")]
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
}
