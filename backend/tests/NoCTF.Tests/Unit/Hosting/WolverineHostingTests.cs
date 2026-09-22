using JasperFx.CodeGeneration.Model;
using Microsoft.Extensions.Configuration;
using NoCTF.Hosting;
using Wolverine;

namespace NoCTF.Tests.Unit.Hosting;

public sealed class WolverineHostingTests
{
    [Test]
    public async Task Persistence_forbids_service_location()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] =
                    "Host=localhost;Database=noctf;Username=noctf;Password=noctf",
                ["ConnectionStrings:Nats"] = "nats://localhost:4222"
            })
            .Build();
        var options = new WolverineOptions();

        options.ConfigureNoCtfPersistence(
            configuration,
            HostRoles.Only(HostRole.Worker));

        await Assert.That(options.ServiceLocationPolicy)
            .IsEqualTo(ServiceLocationPolicy.NotAllowed);
    }
}
