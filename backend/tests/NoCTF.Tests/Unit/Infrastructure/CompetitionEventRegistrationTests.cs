using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Competitions.Events;
using NoCTF.Infrastructure;
using NoCTF.Infrastructure.Competitions.Events;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class CompetitionEventRegistrationTests
{
    [Test]
    public async Task Competition_event_contracts_use_codegen_visible_registrations()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenApi:Exporting"] = "true",
                ["ConnectionStrings:Redis"] = "localhost:6379"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddNoCtfInfrastructure(configuration);

        foreach (var serviceType in new[]
                 {
                     typeof(ICompetitionEventStore),
                     typeof(ICompetitionEventRecorder)
                 })
        {
            var registration = services.Single(candidate =>
                candidate.ServiceType == serviceType);
            await Assert.That(registration.ImplementationType)
                .IsEqualTo(typeof(CompetitionEventStore));
            await Assert.That(registration.ImplementationFactory).IsNull();
        }
    }
}
