using NoCTF.API.Endpoints.Admin;
using NoCTF.API.Endpoints.Competitions;

namespace NoCTF.Tests;

public class PenetrationEndpointRuntimeTests
{
    [Fact]
    public void PlayerRuntime_PreservesStableBusinessErrorCodes()
    {
        var exception = new InvalidOperationException("instance_busy");

        Assert.Equal("instance_busy", PenetrationEndpointRuntime.ErrorCodeFromException(exception));
        Assert.Equal(409, PenetrationEndpointRuntime.StatusFromException(exception));
    }

    [Fact]
    public void PlayerRuntime_RedactsInfrastructureErrors()
    {
        var exception = new InvalidOperationException("docker failed: /var/run/docker.sock token=secret");

        Assert.Equal("instance_operation_failed", PenetrationEndpointRuntime.ErrorCodeFromException(exception));
        Assert.Equal(500, PenetrationEndpointRuntime.StatusFromException(exception));
    }

    [Fact]
    public void AdminRuntime_RedactsInfrastructureErrors()
    {
        var exception = new InvalidOperationException("compose command exposed internal details");

        Assert.Equal("operation_failed", PenetrationAdminEndpointRuntime.ErrorCodeFromException(exception));
        Assert.Equal(500, PenetrationAdminEndpointRuntime.StatusFromException(exception));
    }
}
