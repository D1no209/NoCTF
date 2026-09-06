using System.Text;
using System.Text.Json;
using NoCTF.Runtime.Docker.Containers;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class DockerRegistryAuthenticationTests
{
    [Test]
    [Arguments("registry.example.test", "registry.example.test/team/image:tag", true)]
    [Arguments("https://registry.example.test", "registry.example.test/team/image:tag", true)]
    [Arguments("registry.example.test", "registry.example.test.attacker/team/image:tag", false)]
    [Arguments("https://index.docker.io/v1/", "alpine:3", true)]
    public async Task Credentials_are_sent_only_to_the_matching_registry(string server, string image, bool matches)
    {
        var root = Path.Combine(Path.GetTempPath(), "noctf-registry-auth", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "config.json"), JsonSerializer.Serialize(new
            {
                auths = new Dictionary<string, object> { [server] = new { auth = Convert.ToBase64String(Encoding.UTF8.GetBytes("test-user:test:password")) } }
            }));
            var result = DockerRegistryAuthentication.Read(image, root);
            await Assert.That(result.Username).IsEqualTo(matches ? "test-user" : null);
            await Assert.That(result.Password).IsEqualTo(matches ? "test:password" : null);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task Invalid_login_files_do_not_echo_secret_contents()
    {
        var root = Path.Combine(Path.GetTempPath(), "noctf-registry-auth", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "config.json"), """{"auths":{"registry.example.test":{"auth":"sensitive-invalid-base64"}}}""");
            try
            {
                _ = DockerRegistryAuthentication.Read("registry.example.test/team/image", root);
                throw new Exception("Invalid credentials were accepted.");
            }
            catch (InvalidOperationException exception)
            {
                await Assert.That(exception.Message).DoesNotContain("sensitive-invalid-base64");
                await Assert.That(exception.InnerException).IsNull();
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
