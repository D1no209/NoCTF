using DotNet.Testcontainers.Builders;

namespace NoCTF.Tests.Integration;

internal static class DockerIntegrationTest
{
    private const string RequireDockerEnvironmentVariable = "NOCTF_REQUIRE_DOCKER_INTEGRATION";
    private const string UnavailableReason =
        "Integration skipped: Docker is unavailable or its endpoint is misconfigured.";

    public static async Task RunAsync(Func<Task> test)
    {
        try
        {
            await test();
        }
        catch (DockerUnavailableException)
        {
            if (string.Equals(
                    Environment.GetEnvironmentVariable(RequireDockerEnvironmentVariable),
                    "true",
                    StringComparison.OrdinalIgnoreCase))
                throw;
            Skip.Test(UnavailableReason);
        }
    }
}
